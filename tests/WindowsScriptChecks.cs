using System.Text;
using IgezziGuard;
internal static class WindowsScriptChecks
{
    private static async Task<IReadOnlyList<DiagnosticResult>> Fixture(string id,string mock)
    {
        var module=WindowsDiagnosticCatalog.Create(includeExternal:true).OfType<WindowsDiagnosticModule>().Single(m=>m.Id==id);
        // Function stubs shadow every native provider used by the selected script. Use the production probe so stray stderr fails here too.
        var json=await new WindowsDiagnosticProbe().ReadAsync(mock+"\n"+module.Script,20,CancellationToken.None);
        var rows=System.Text.Json.JsonSerializer.Deserialize<List<ProbeValue>>(json)!;
        var now=DateTimeOffset.UtcNow;
        return rows.Select(r=>DiagnosticMapping.Map(id,module.Category,r,now,now)).ToArray();
    }
    internal static async Task Run()
    {
        // Read-only native call that triggers module auto-loading; progress records must not reach stderr.
        var native=await WindowsCommand.PowerShellCapture("Get-CimInstance Win32_OperatingSystem | Out-Null; 'ok'",CancellationToken.None,60);
        DiagnosticChecks.Check(native.StandardOutput.Trim()=="ok" && native.StandardError.Length==0,"PowerShell progress records do not reach collector stderr");
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
        // Network probes: several DNS names, so one name blocked by a router is not reported as a DNS failure.
        var probeScript=WindowsDiagnosticCatalog.Create(includeExternal:true).OfType<WindowsDiagnosticModule>().Single(m=>m.Id=="network-probes").Script;
        DiagnosticChecks.Check(NetworkDiagnostics.DnsTestNames.All(n=>probeScript.Contains("'"+n+"'") && WindowsDiagnosticCatalog.ProbeDisclosure.Contains(n)),"network probes and their disclosure use the Connect DNS test names");
        const string noGateway="function Get-NetRoute { @() }";
        // TcpClient stand-in: the fixture never opens a real connection.
        const string fakeTcp="""
            function New-Object { param($TypeName) if($TypeName -eq 'System.Net.Sockets.TcpClient'){$o=[pscustomobject]@{Connected=$true};$o|Add-Member ScriptMethod ConnectAsync { param($h,$p) [System.Threading.Tasks.Task]::CompletedTask };$o|Add-Member ScriptMethod Dispose {};return $o}; Microsoft.PowerShell.Utility\New-Object @args }
            """;
        var allFail=await Fixture("network-probes",noGateway+"\nfunction Resolve-DnsName { throw 'Fixture NXDOMAIN' }");
        var dnsDown=allFail.Single(r=>r.FindingId=="dns");
        DiagnosticChecks.Check(dnsDown.Severity==FindingSeverity.Warning && allFail.Single(r=>r.FindingId=="internet").Severity==FindingSeverity.Unknown && FindingAnalysis.Recommend(dnsDown)?.RepairActionId=="dns-cache-flush","no name resolves: DNS warning, TCP skipped, DNS-cache refresh proposed");
        var filtered=await Fixture("network-probes",noGateway+"\n"+fakeTcp+"\nfunction Resolve-DnsName { param($Name) if($Name -eq 'example.com'){throw 'Fixture NXDOMAIN'}; [pscustomobject]@{Name=$Name} }");
        var dnsPartial=filtered.Single(r=>r.FindingId=="dns");
        DiagnosticChecks.Check(dnsPartial.Severity==FindingSeverity.Informational && dnsPartial.Evidence.Contains("example.com") && FindingAnalysis.Recommend(dnsPartial)?.RepairActionId is null,"one blocked name (real router case): information only, no DNS repair proposed");
        DiagnosticChecks.Check(filtered.Single(r=>r.FindingId=="internet") is {Severity:FindingSeverity.Healthy} tcp && tcp.Evidence.Contains("www.microsoft.com:443"),"TCP probe uses the first name that resolved");
        var allOk=await Fixture("network-probes",noGateway+"\n"+fakeTcp+"\nfunction Resolve-DnsName { param($Name) [pscustomobject]@{Name=$Name} }");
        DiagnosticChecks.Check(allOk.Single(r=>r.FindingId=="dns").Severity==FindingSeverity.Healthy,"every name resolves: DNS healthy");
        // Parse fixed scripts using the Windows PowerShell parser; never invoke any of their commands.
        foreach(var script in WindowsDiagnosticCatalog.Create(includeExternal:true).OfType<WindowsDiagnosticModule>().Select(m=>m.Script).Append(ActivationDiagnostic.Script).Append(UpdateHealth.Script).Append(BatteryStartup.Script)){
            var encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
            var parse="$s=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+encoded+"'));$tokens=$null;$errors=$null;[void][System.Management.Automation.Language.Parser]::ParseInput($s,[ref]$tokens,[ref]$errors);if($errors.Count -gt 0){throw ($errors.Message -join '; ')};'Parsed'";
            var result=await WindowsCommand.PowerShellCapture(parse,CancellationToken.None,20);
            DiagnosticChecks.Check(result.StandardOutput.Trim()=="Parsed","fixed diagnostic script parses without execution");
        }
    }
}
