using System.Text;
using IgezziGuard;
internal static class WindowsScriptChecks
{
    private static async Task<IReadOnlyList<DiagnosticResult>> Fixture(string id,string mock)
    {
        var module=WindowsDiagnosticCatalog.Create().OfType<WindowsDiagnosticModule>().Single(m=>m.Id==id);
        // Function stubs shadow every native provider used by the selected script.
        var captured=await WindowsCommand.PowerShellCapture(mock+"\n"+module.Script,CancellationToken.None,20);
        var rows=System.Text.Json.JsonSerializer.Deserialize<List<ProbeValue>>(captured.StandardOutput)!;
        var now=DateTimeOffset.UtcNow;
        return rows.Select(r=>DiagnosticMapping.Map(id,module.Category,r,now,now)).ToArray();
    }
    internal static async Task Run()
    {
        var storage=await Fixture("storage", """
            function Get-CimInstance { [pscustomobject]@{FreeSpace=20;Size=100} }
            function Get-PhysicalDisk { [pscustomobject]@{HealthStatus='Unknown';OperationalStatus='Unknown';FriendlyName='Fixture'} }
            function Get-StorageReliabilityCounter { throw 'Fixture unsupported' }
            """);
        DiagnosticChecks.Check(storage.Single(r=>r.FindingId=="health").Severity==FindingSeverity.Unknown && storage.Single(r=>r.FindingId=="reliability").Outcome==CollectionOutcome.Unavailable,"unsupported storage health is not hardware failure");
        var devices=await Fixture("devices", """
            function Get-CimInstance { param($ClassName)
                if($ClassName -eq 'Win32_PnPSignedDriver'){return @()}
                @([pscustomobject]@{DeviceID='disabled-fixture';Name='Disabled';ConfigManagerErrorCode=22},[pscustomobject]@{DeviceID='missing-fixture';Name='Missing driver';ConfigManagerErrorCode=28})
            }
            """);
        DiagnosticChecks.Check(devices.Single(r=>r.FindingId=="disabled-fixture").Severity==FindingSeverity.Informational && devices.Single(r=>r.FindingId=="missing-fixture").Severity==FindingSeverity.Warning,"disabled device is not critical and problem code remains actionable");
        var events=await Fixture("events", """
            function Get-WinEvent {
                @([pscustomobject]@{ProviderName='disk';Id=7;TimeCreated=(Get-Date)},[pscustomobject]@{ProviderName='disk';Id=7;TimeCreated=(Get-Date)},[pscustomobject]@{ProviderName='Unrelated';Id=7;TimeCreated=(Get-Date)},[pscustomobject]@{ProviderName='Microsoft-Windows-Kernel-Power';Id=41;TimeCreated=(Get-Date)})
            }
            """);
        DiagnosticChecks.Check(events.Count==1 && events[0].Metadata["value"]=="2","event rules aggregate repeated allowlisted signals and suppress unknown providers and single events");
        var security=await Fixture("security", """
            function Get-CimInstance { param($Namespace,$ClassName,$Filter)
                if($Namespace -eq 'root/SecurityCenter2'){return [pscustomobject]@{displayName='Fixture third-party antivirus';productState=0}}
                [pscustomobject]@{State='Running';StartMode='Auto'}
            }
            function Get-MpComputerStatus { [pscustomobject]@{RealTimeProtectionEnabled=$false;AMRunningMode='Normal'} }
            function Get-NetFirewallProfile { [pscustomobject]@{Name='Fixture';Enabled=$true} }
            """);
        DiagnosticChecks.Check(security.Single(r=>r.FindingId=="realtime").Severity==FindingSeverity.Unknown,"inactive Defender with third-party antivirus is not unprotected verdict");
        // Parse fixed scripts using the Windows PowerShell parser; never invoke any of their commands.
        foreach(var script in WindowsDiagnosticCatalog.Create(includeExternal:true).OfType<WindowsDiagnosticModule>().Select(m=>m.Script).Append(ActivationDiagnostic.Script)){
            var encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
            var parse="$s=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+encoded+"'));$tokens=$null;$errors=$null;[void][System.Management.Automation.Language.Parser]::ParseInput($s,[ref]$tokens,[ref]$errors);if($errors.Count -gt 0){throw ($errors.Message -join '; ')};'Parsed'";
            var result=await WindowsCommand.PowerShellCapture(parse,CancellationToken.None,20);
            DiagnosticChecks.Check(result.StandardOutput.Trim()=="Parsed","fixed diagnostic script parses without execution");
        }
    }
}
