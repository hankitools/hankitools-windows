using System.Security.Principal;
using System.Text.Json;

namespace IgezziGuard;

internal sealed class WindowsRepairEnvironment : IRepairEnvironment
{
    public async Task<RepairEnvironment> ReadAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return new(false, false, false, false, new HashSet<string>(), false);
        using var identity = WindowsIdentity.GetCurrent();
        bool admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        var data = await WindowsCommand.PowerShellCapture("""
            $os=Get-CimInstance Win32_OperatingSystem;
            $restart=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired');
            $rename=Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue;
            $services=@(Get-CimInstance Win32_Service -Filter "Name='TrustedInstaller'" | Where-Object StartMode -ne 'Disabled' | Select-Object -ExpandProperty Name);
            $busy=@(Get-Process -Name dism,sfc,TiWorker -ErrorAction SilentlyContinue).Count -gt 0;
            [pscustomobject]@{Client=($os.ProductType -eq 1);Restart=($restart -or $null -ne $rename);Conflict=$busy;Services=$services}|ConvertTo-Json
            """, token);
        using var json = JsonDocument.Parse(data.StandardOutput); var r = json.RootElement;
        return new(r.GetProperty("Client").GetBoolean(), admin, r.GetProperty("Restart").GetBoolean(), r.GetProperty("Conflict").GetBoolean(),
            r.GetProperty("Services").EnumerateArray().Select(v => v.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase), false);
    }
}
internal sealed class WindowsRestoreProtection : IRestoreProtection
{
    public async Task<RestoreResult> CreateAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return new(RestoreState.Unsupported, "Windows client System Restore is required.");
        try {
            // Unique description + new sequence prove this request created a point; command success alone is insufficient.
            var label = "Hanki approved repair " + Guid.NewGuid().ToString("N");
            var text = await WindowsCommand.PowerShellCapture("$before=@(Get-ComputerRestorePoint | Select-Object -ExpandProperty SequenceNumber); Checkpoint-Computer -Description " + WindowsCommand.Quote(label) +
                " -RestorePointType MODIFY_SETTINGS; $point=@(Get-ComputerRestorePoint | Where-Object { $_.Description -eq " + WindowsCommand.Quote(label) +
                " -and $_.SequenceNumber -notin $before }); if($point.Count -eq 1){[string]$point[0].SequenceNumber}else{'UNAVAILABLE'}", token, 180);
            return int.TryParse(text.StandardOutput.Trim(), out var sequence) && sequence > 0
                ? new(RestoreState.Created, "A new restore point was confirmed. This is not a complete data backup.", sequence.ToString())
                : new(RestoreState.Unavailable, "No new restore point was confirmed. Windows policy, protection settings or creation-frequency limits may prevent it.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new(RestoreState.Failed, "Restore-point creation failed; no protection is claimed."); }
    }
}
internal sealed class WindowsServicingRepair(RepairDefinition definition) : IRepairAction
{
    public RepairDefinition Definition => definition;
    public async Task<RepairExecutionResult> ExecuteAsync(CancellationToken token)
    {
        // Closed allowlist: no command comes from a result, saved report, user input or AI.
        if (Definition.Id == "dns-cache-flush") {
            await WindowsCommand.RunCaptured(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), ["/flushdns"], token, 30);
            return new(true, false, "Windows DNS-cache refresh command completed. Fresh probes determine whether the observed lookup improved; no persistent resolver setting changed.");
        }
        if (Definition.Id == "dism-restore") {
            var output = await WindowsCommand.PowerShellCapture("$r=Repair-WindowsImage -Online -RestoreHealth -NoRestart; [pscustomobject]@{Restart=[bool]$r.RestartNeeded}|ConvertTo-Json", token, 1800);
            using var json = JsonDocument.Parse(output.StandardOutput);
            return new(true, json.RootElement.GetProperty("Restart").GetBoolean(), "DISM command completed. Diagnostic verification determines the repair outcome.");
        }
        if (Definition.Id == "sfc-repair") {
            await WindowsCommand.RunCaptured(Path.Combine(Environment.SystemDirectory, "sfc.exe"), ["/scannow"], token, 1800);
            var environment = await new WindowsRepairEnvironment().ReadAsync(token);
            return new(true, environment.RestartPending, "SFC command completed. Diagnostic verification determines the repair outcome.");
        }
        throw new InvalidOperationException("Unsupported servicing action.");
    }
    internal static IRepairAction[] Catalog() => [
        new WindowsServicingRepair(new("dns-cache-flush", "Refresh Windows DNS cache", "Clear cached DNS resolver entries. Windows repopulates them as needed. Verification queries example.com and performs the disclosed network probes; DNS server configuration stays unchanged.", RepairRisk.Low, true, true, false, false, [], "network-probes", "Transient cache only; no configuration rollback needed. Existing cached entries are not restored.", RestartRequirement.None)),
        new WindowsServicingRepair(new("dism-restore", "Repair Windows component store", "Run DISM RestoreHealth using configured Windows repair sources. It may download replacement components. No automatic restart.", RepairRisk.Moderate, true, true, true, false, ["TrustedInstaller"], "dism", "No per-file Hanki undo. Use Windows recovery/restore options where available; keep a backup.")),
        new WindowsServicingRepair(new("sfc-repair", "Repair protected Windows files", "Run SFC scannow to replace damaged protected system files. No automatic restart.", RepairRisk.Moderate, true, false, true, false, ["TrustedInstaller"], "sfc", "No per-file Hanki undo. Use Windows recovery/restore options where available; keep a backup."))
    ];
}
