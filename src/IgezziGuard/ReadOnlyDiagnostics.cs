using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace IgezziGuard;

internal static class ReadOnlyDiagnostics
{
    public static async Task<string> CrashLogs(CancellationToken token)
    {
        const string script = "& { $ErrorActionPreference='Stop'; foreach($log in @('System','Application')) { 'LOG: '+$log; try { Get-WinEvent -FilterHashtable @{LogName=$log;Level=1,2,3;StartTime=(Get-Date).AddDays(-7)} -MaxEvents 50 | ForEach-Object { $m=$_.Message; if($m -and $m.Length -gt 500){$m=$m.Substring(0,500)+' [truncated]'}; [pscustomobject]@{Time=$_.TimeCreated.ToString('o');Provider=$_.ProviderName;Id=$_.Id;Level=$_.LevelDisplayName;RecordId=$_.RecordId;Message=$m} } | ConvertTo-Json -Depth 3 } catch { 'Log query unavailable or no matching events: '+$_.Exception.Message } } }";
        return $"Hanki Diagnose • {DateTimeOffset.Now:O}\r\nLast 7 days; newest 50 warning/error/critical events per System and Application log. Messages capped at 500 characters. Not a complete event export.\r\n" +
            "Interpretation: Kernel-Power 41 records an unclean shutdown, not its root cause. Look for correlated bugcheck, WHEA, storage or driver events near the crash time. Warnings alone are not proof of a fault. No dump analysis or repair is performed.\r\n" +
            "Reports can contain usernames, computer names, paths and application/customer data. Review before sharing.\r\n\r\n" + await PowerShell(script, token);
    }
    public static async Task<string> Defender(CancellationToken token)
    {
        const string script = "& { $s=$null; $p=$null; $se=$null; $pe=$null; try { $s=Get-MpComputerStatus | Select-Object AMRunningMode,AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,BehaviorMonitorEnabled,IoavProtectionEnabled,NISEnabled,IsTamperProtected,AntivirusSignatureVersion,AntivirusSignatureLastUpdated } catch { $se=$_.Exception.Message }; try { $p=Get-MpPreference | Select-Object DisableRealtimeMonitoring,DisableBehaviorMonitoring,DisableIOAVProtection,DisableScriptScanning,ExclusionPath,ExclusionProcess,ExclusionExtension,ExclusionIpAddress } catch { $pe=$_.Exception.Message }; [pscustomobject]@{Status=$s;Preferences=$p;StatusError=$se;PreferencesError=$pe} | ConvertTo-Json -Depth 5 }";
        var result = await WindowsCommand.PowerShellCapture(script, token, 45);
        return $"Checked {DateTimeOffset.Now:f}\r\n\r\n" + DefenderAuditSummary.Format(result.StandardOutput, result.StandardError);
    }

    public static async Task<string> Network(CancellationToken token)
    {
        var report = new StringBuilder($"Hanki Connect — Wi-Fi & ICMP • {DateTimeOffset.Now:O}\r\n");
        report.AppendLine("Wi-Fi output below is native, localised Windows text. It may expose SSID/BSSID, MAC and network names. Location permission may be required; no permissions are changed by Hanki.");
        report.AppendLine(await Command(Path.Combine(Environment.SystemDirectory, "netsh.exe"), ["wlan", "show", "interfaces"], token));
        var targets = new HashSet<IPAddress> { IPAddress.Parse("1.1.1.1") };
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            token.ThrowIfCancellationRequested();
            try {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var address in adapter.GetIPProperties().GatewayAddresses.Select(g => g.Address))
                    if (address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any) && targets.Count < 5) targets.Add(address);
            }
            catch (NetworkInformationException ex) { report.AppendLine("Gateway lookup incomplete: " + ex.Message); }
        }
        var results = await Task.WhenAll(targets.Select(async target => {
            var replies = new List<long>(); int sent = 0, errors = 0;
            using var ping = new Ping();
            for (int i = 0; i < 10; i++) {
                token.ThrowIfCancellationRequested(); sent++;
                try { var reply = await ping.SendPingAsync(target, 1500).WaitAsync(token); if (reply.Status == IPStatus.Success) replies.Add(reply.RoundtripTime); }
                catch (PingException) { errors++; }
                if (i < 9) await Task.Delay(250, token);
            }
            return $"{target}: {DiagnosticRules.PingSummary(sent, replies)} Local ping errors: {errors}.";
        }));
        foreach (var line in results) report.AppendLine(line);
        report.AppendLine("No throughput/speed test or traceroute. VPNs, firewalls and multiple adapters affect routes. A good gateway response and poor external response narrow investigation but do not identify the cause.");
        return report.ToString();
    }
    private static Task<string> PowerShell(string script, CancellationToken token) => Command(
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
        ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(
            "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new(); $ProgressPreference='SilentlyContinue'; " + script))], token);

    // Only fixed internal read-only commands call this method. No log/user content is interpolated.
    private static async Task<string> Command(string executable, string[] arguments, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = executable.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8 : null };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None); var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try {
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout; var error = await stderr;
            return output + (error.Length > 0 ? "\r\nErrors: " + error : "") + $"\r\nCommand exit code: {process.ExitCode}\r\n";
        }
        catch (OperationCanceledException) {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await Task.WhenAll(stdout, stderr);
            if (token.IsCancellationRequested) throw;
            return "Read-only command timed out; output incomplete. No settings changed.";
        }
    }
}
