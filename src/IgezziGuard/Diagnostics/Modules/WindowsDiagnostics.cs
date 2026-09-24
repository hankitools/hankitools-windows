using System.Text.Json;

namespace IgezziGuard;

internal interface IDiagnosticProbe
{
    Task<string> ReadAsync(string script, int timeoutSeconds, CancellationToken token);
}
internal sealed class WindowsDiagnosticProbe : IDiagnosticProbe
{
    public async Task<string> ReadAsync(string script, int timeoutSeconds, CancellationToken token)
    {
        var output = await WindowsCommand.PowerShellCapture(script, token, timeoutSeconds);
        // Do not parse presentation text or discard unexpected diagnostic streams.
        if (!string.IsNullOrWhiteSpace(output.StandardError)) throw new IOException("The collector reported tool errors.");
        return output.StandardOutput;
    }
}
internal sealed record ProbeValue(string Id, string State, string Evidence, double? Value = null, double? Total = null);

internal sealed class WindowsDiagnosticModule(string id, string name, DiagnosticCategory category,
    DiagnosticRequirements requirements, string script, IDiagnosticProbe probe, int timeout = 60) : IDiagnosticModule
{
    public string Id => id;
    internal string Script => script;
    public string DisplayName => name;
    public DiagnosticCategory Category => category;
    public DiagnosticRequirements Requirements => requirements;
    public async Task<IReadOnlyList<DiagnosticResult>> CollectAsync(DiagnosticContext context, IProgress<DiagnosticProgress>? progress, CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow;
        progress?.Report(new(Id, DisplayName + " — collecting Windows evidence"));
        var json = await probe.ReadAsync(script, timeout, cancellationToken);
        var values = JsonSerializer.Deserialize<List<ProbeValue>>(json) ?? throw new IOException("No evidence returned.");
        return values.Select(v => DiagnosticMapping.Map(Id, Category, v, start, DateTimeOffset.UtcNow)).ToArray();
    }
}

internal static class DiagnosticMapping
{
    internal static DiagnosticResult Map(string module, DiagnosticCategory category, ProbeValue v, DateTimeOffset start, DateTimeOffset end)
    {
        if (module == "sfc" && v.State == "sfc-output") v = v with { State = SfcState(v.Evidence, v.Value) };
        var outcome = v.State == "unavailable" ? CollectionOutcome.Unavailable : v.State == "failed" ? CollectionOutcome.Failed : CollectionOutcome.Completed;
        var severity = v.State switch {
            "healthy" => FindingSeverity.Healthy, "warning" or "repairable" => FindingSeverity.Warning,
            "critical" or "nonrepairable" => FindingSeverity.Critical, "info" or "registered" => FindingSeverity.Informational,
            _ => FindingSeverity.Unknown
        };
        string explanation = v.State switch {
            "healthy" => "Windows reported no issue in this specific check. This is not a guarantee of overall PC health.",
            "repairable" => "Windows reports repairable component-store corruption. Review repair options before changing Windows.",
            "nonrepairable" => "Windows reports component-store corruption that this servicing check considers non-repairable. Review recovery options and retain backups.",
            "warning" => "Windows reported a condition worth reviewing. The evidence does not establish the cause of your symptoms.",
            "critical" => "Windows reported a serious condition. Review the evidence and protect important data before making changes.",
            "registered" => "An antivirus provider is registered. Registration alone does not confirm that its protection is working.",
            "info" => "Context for investigation; this observation alone is not a fault.",
            "failed" => "The check failed to collect usable evidence. This does not mean Windows is corrupted.",
            _ => "This check could not establish the state. Review the individual Windows tool; no health conclusion is available."
        };
        string coverage = "Current local observation; no repair performed.";
        if (module == "storage" && v.Id == "capacity") {
            if (v.Value is not double free || v.Total is not double total || !double.IsFinite(free) || !double.IsFinite(total) || free < 0 || total <= 0 || free > total) {
                severity = FindingSeverity.Unknown; explanation = "System-drive capacity counters are unavailable.";
            } else {
                var fraction = free / total;
                severity = fraction < 0.05 ? FindingSeverity.Critical : fraction < 0.10 ? FindingSeverity.Warning : FindingSeverity.Healthy;
                explanation = $"The system drive has {fraction:P0} free space. Below 10% warrants review; below 5% is urgent capacity pressure. This is not a hardware-health assessment.";
            }
        }
        if (module == "performance" && v.Id == "memory") {
            if (v.Value is not double free || v.Total is not double total || !double.IsFinite(free) || !double.IsFinite(total) || total <= 0 || free < 0 || free > total) {
                severity = FindingSeverity.Unknown; explanation = "Memory counters are unavailable.";
            } else {
                severity = free / total < 0.10 ? FindingSeverity.Warning : FindingSeverity.Informational;
                explanation = "Available physical memory in one sample: " + (free / total).ToString("P0") + ". Less than 10% is a review signal, not proof of a leak or a reason to disable the pagefile. Repeat during a representative workload.";
            }
        }
        if (module == "sfc") coverage = "Verification only. Recognized English terminal messages are mapped; other locales and unrecognized output remain unknown. SFC can write its own Windows logs.";
        if (module == "dism") coverage = "DISM ScanHealth with no repair; Windows may write servicing logs. Component-store health is distinct from system-file integrity.";
        if (module == "events") coverage = "Newest 500 System events from 7 days; only repeated allowlisted provider/ID pairs are surfaced. Absence is not proof that no fault occurred.";
        if (module == "security" && v.Id == "antivirus") coverage = "Security Center provider registration does not establish product health. Review Windows Security or your antivirus console.";
        if (outcome != CollectionOutcome.Completed) severity = FindingSeverity.Unknown;
        var confidence = severity is FindingSeverity.Unknown ? FindingConfidence.Unknown : FindingConfidence.Confirmed;
        var metadata = new Dictionary<string, string> { ["state"] = v.State };
        if (v.Value.HasValue) metadata["value"] = v.Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new(module, v.Id, category, outcome, severity, Title(module, v.Id), explanation,
            start, end, v.Evidence ?? "", coverage, confidence, metadata: metadata);
    }
    internal static string SfcState(string? text, double? exitCode)
    {
        if (exitCode is null) return "unknown";
        if (exitCode != 0) return "failed";
        var messages = new Dictionary<string,string>(StringComparer.Ordinal) {
            ["Windows Resource Protection did not find any integrity violations."] = "healthy",
            ["Windows Resource Protection found integrity violations."] = "warning",
            ["Windows Resource Protection found corrupt files and successfully repaired them."] = "info",
            ["Windows Resource Protection found corrupt files but was unable to fix some of them."] = "warning"
        };
        var matches = messages.Where(m => (text ?? "").Replace("\0", "").Contains(m.Key, StringComparison.Ordinal)).Select(m => m.Value).Distinct().ToArray();
        return matches.Length == 1 ? matches[0] : "unknown";
    }
    internal static string Title(string module, string finding) => (module, finding) switch {
        ("dism", _) => "Windows component store", ("sfc", _) => "Protected system files",
        ("storage", "capacity") => "System-drive free space", ("storage", _) => "Storage health / reliability",
        ("security", "antivirus") => "Antivirus providers", ("security", "realtime") => "Defender protection",
        ("security", _) => "Windows firewall profiles", ("performance", "memory") => "Available memory",
        ("performance", _) => "Startup registrations and impact", ("devices", _) => "Windows device state",
        ("events", _) => "Repeated Windows event signals", ("network", "dns") => "DNS resolution",
        ("network", "gateway") => "Gateway reachability", ("network", "internet") => "External connectivity",
        ("network", "proxy") => "Current-user proxy configuration", ("network", _) => "Local network configuration",
        _ => "Windows diagnostic"
    };
}

internal static class WindowsDiagnosticCatalog
{
    // Each fixed read-only query emits small typed rows. Never interpolate reports or user input into scripts.
    private const string Prelude = "function row($id,$state,$evidence,$value=$null,$total=$null){[pscustomobject]@{Id=$id;State=$state;Evidence=[string]$evidence;Value=$value;Total=$total}}; $rows=@(& { ";
    private const string Suffix = " }); ConvertTo-Json -InputObject @($rows) -Depth 4 -Compress";
    // The network-probes script lists the same names; a check keeps them in step. Several names,
    // because routers and filters sometimes block one (a real router refused example.com).
    internal static readonly string ProbeDisclosure = $"DNS lookups for {string.Join(", ", NetworkDiagnostics.DnsTestNames)} and a TCP connection to port 443 of the first name that resolves disclose your source IP.";
    internal static IDiagnosticModule[] Create(IDiagnosticProbe? probe = null, bool includeExternal = false)
    {
        probe ??= new WindowsDiagnosticProbe();
        IDiagnosticModule Module(string id, string name, DiagnosticCategory c, string script, bool admin = false, int timeout = 60, bool external = false) =>
            new WindowsDiagnosticModule(id, name, c, new(admin, external, external ? ProbeDisclosure + " Gateway ICMP contacts your local network." : ""), Prelude + script + Suffix, probe, timeout);
        var modules = new List<IDiagnosticModule> {
            Module("dism", "Windows component store (can take several minutes)", DiagnosticCategory.Windows, """
                $h=Repair-WindowsImage -Online -ScanHealth -NoRestart;
                switch ([string]$h.ImageHealthState) {
                    'Healthy' {row 'health' 'healthy' 'DISM ImageHealthState: Healthy'}
                    'Repairable' {row 'health' 'repairable' 'DISM ImageHealthState: Repairable'}
                    'NonRepairable' {row 'health' 'nonrepairable' 'DISM ImageHealthState: NonRepairable'}
                    default {row 'health' 'unknown' ('Unrecognized ImageHealthState: '+$h.ImageHealthState)}
                }
                """, true, 900),
            Module("sfc", "Protected system files (verification only)", DiagnosticCategory.Windows, """
                $text=(& "$env:SystemRoot\System32\sfc.exe" /verifyonly 2>&1 | Out-String) -replace "`0",''; $code=$LASTEXITCODE;
                row 'integrity' 'sfc-output' $text $code
                """, true, 900),
            Module("storage", "Storage capacity and health", DiagnosticCategory.Storage, """
                try {$d=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$env:SystemDrive'";row 'capacity' 'info' 'System-drive free/total bytes' $d.FreeSpace $d.Size}catch{row 'capacity' 'unavailable' 'Capacity unavailable'}
                try {$disks=@(Get-PhysicalDisk); if($disks.Count -eq 0){row 'health' 'unavailable' 'No physical disks returned'}
                    else {$bad=@($disks|Where-Object {$_.HealthStatus -in 'Warning','Unhealthy'});$unknown=@($disks|Where-Object {$_.HealthStatus -notin 'Healthy','Warning','Unhealthy'});row 'health' $(if($bad.Count -gt 0){'warning'}elseif($unknown.Count -gt 0){'unknown'}else{'healthy'}) (($disks|Select-Object FriendlyName,HealthStatus,OperationalStatus|ConvertTo-Json -Depth 3)-join '')}
                    try {$reliability=@($disks|Get-StorageReliabilityCounter);$known=@($reliability|Where-Object {$null -ne $_.ReadErrorsTotal -or $null -ne $_.WriteErrorsTotal -or $null -ne $_.Wear});
                        if($known.Count -eq 0){row 'reliability' 'unavailable' 'No supported reliability counters'}else{row 'reliability' 'info' ($known|Select-Object DeviceId,ReadErrorsTotal,WriteErrorsTotal,Wear,Temperature|ConvertTo-Json -Depth 3)}
                    }catch{row 'reliability' 'unavailable' 'Reliability counters unsupported or access denied'}
                }catch{row 'health' 'unavailable' 'Storage-health provider unavailable'}
                """),
            Module("network", "Local network and proxy", DiagnosticCategory.Network, """
                try {$a=@(Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=True');row 'adapters' $(if($a.Count -gt 0){'info'}else{'warning'}) ($a|Select-Object Description,DHCPEnabled,IPAddress,DefaultIPGateway,DNSServerSearchOrder|ConvertTo-Json -Depth 4)}catch{row 'adapters' 'unavailable' 'Adapter configuration unavailable'}
                try {$p=Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings';row 'proxy' 'info' ($p|Select-Object ProxyEnable,ProxyServer,AutoConfigURL|ConvertTo-Json)}catch{row 'proxy' 'unavailable' 'Current-user proxy configuration unavailable'}
                """),
            Module("devices", "Devices and drivers", DiagnosticCategory.Devices, """
                $all=@(Get-CimInstance Win32_PnPEntity); $bad=@($all|Where-Object {$null -eq $_.ConfigManagerErrorCode -or $_.ConfigManagerErrorCode -ne 0});
                if($all.Count -eq 0){row 'devices' 'unavailable' 'No devices returned'}
                elseif($bad.Count -eq 0){row 'devices' 'healthy' ('No problem codes in '+$all.Count+' enumerated devices')}
                else {$drivers=@{};try{Get-CimInstance Win32_PnPSignedDriver|ForEach-Object{if($_.DeviceID){$drivers[$_.DeviceID]=$_}}}catch{}
                    foreach($d in $bad|Select-Object -First 100){$driver=$drivers[$d.DeviceID];$state=if($null -eq $d.ConfigManagerErrorCode){'unknown'}elseif($d.ConfigManagerErrorCode -eq 22){'info'}else{'warning'};
                        row $d.DeviceID $state ([pscustomobject]@{Name=$d.Name;Class=$d.PNPClass;DeviceID=$d.DeviceID;Status=$d.Status;ProblemCode=$d.ConfigManagerErrorCode;DriverVersion=$driver.DriverVersion;DriverProvider=$driver.DriverProviderName;DriverDate=$driver.DriverDate}|ConvertTo-Json)}
                    if($bad.Count -gt 100){row 'coverage' 'unknown' 'Only first 100 problem devices included'}}
                """),
            Module("events", "Repeated event signals", DiagnosticCategory.Windows, """
                $all=@(Get-WinEvent -FilterHashtable @{LogName='System';StartTime=(Get-Date).AddDays(-7)} -MaxEvents 500);
                $selected=@($all|Where-Object {($_.ProviderName -eq 'Microsoft-Windows-WHEA-Logger' -and $_.Id -in 17,18,19) -or ($_.ProviderName -eq 'disk' -and $_.Id -in 7,51,153) -or ($_.ProviderName -eq 'Microsoft-Windows-Kernel-Power' -and $_.Id -eq 41)});
                $groups=@($selected|Group-Object ProviderName,Id|Where-Object Count -ge 2);
                if($groups.Count -eq 0){row 'signals' 'info' 'No repeated allowlisted signals in the bounded query; other faults are not excluded.'}
                foreach($g in $groups){row $g.Name 'warning' ('Occurrences: '+$g.Count+'; latest: '+$g.Group[0].TimeCreated.ToString('o')+'. Repeated events are clues, not confirmed root causes.') $g.Count}
                """),
            Module("security", "Windows Security", DiagnosticCategory.Security, """
                $providers=@();try {$providers=@(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct);row 'antivirus' $(if($providers.Count -gt 0){'registered'}else{'unknown'}) ($providers|Select-Object displayName,productState|ConvertTo-Json)}catch{row 'antivirus' 'unavailable' 'Security Center unavailable'}
                try {$s=Get-MpComputerStatus; $third=@($providers|Where-Object {$_.displayName -notmatch '^(Microsoft|Windows) Defender'}).Count -gt 0;
                    $state=if($s.RealTimeProtectionEnabled -eq $true){'healthy'}elseif($third -or $s.AMRunningMode -ne 'Normal'){'unknown'}elseif($s.RealTimeProtectionEnabled -eq $false){'warning'}else{'unknown'};
                    row 'realtime' $state ('Defender mode: '+$s.AMRunningMode+'; realtime: '+$s.RealTimeProtectionEnabled+'. Another registered provider or policy can explain inactive Defender.')
                }catch{row 'realtime' 'unavailable' 'Defender state unavailable'}
                foreach($name in @('wscsvc','MpsSvc','WinDefend')){try{$svc=Get-CimInstance Win32_Service -Filter "Name='$name'";if($null -eq $svc){row ('service-'+$name) 'unknown' 'Security service not returned'}else{row ('service-'+$name) 'info' ($name+': '+$svc.State+' / '+$svc.StartMode+'. Third-party security products and policy may alter service state.')}}catch{row ('service-'+$name) 'unavailable' 'Security service unavailable'}}
                try {$profiles=@(Get-NetFirewallProfile);if($profiles.Count -eq 0){row 'firewall' 'unknown' 'No firewall profiles returned'}else{row 'firewall' $(if(@($profiles|Where-Object {[string]$_.Enabled -eq 'False'}).Count -gt 0){'warning'}elseif(@($profiles|Where-Object {[string]$_.Enabled -ne 'True'}).Count -gt 0){'unknown'}else{'healthy'}) ($profiles|Select-Object Name,Enabled|ConvertTo-Json)}}catch{row 'firewall' 'unavailable' 'Firewall state unavailable'}
                """),
            Module("performance", "Memory and startup", DiagnosticCategory.Performance, """
                try {$m=Get-CimInstance Win32_OperatingSystem;row 'memory' 'info' 'Available/total physical memory (KiB), one sample' $m.FreePhysicalMemory $m.TotalVisibleMemorySize}catch{row 'memory' 'unavailable' 'Memory counters unavailable'}
                try {$items=@(Get-CimInstance Win32_StartupCommand);row 'startup' 'info' ('Registered startup commands: '+$items.Count+'. Count alone does not establish high impact. Windows Startup impact is not reliably available from this inventory; review Task Manager.') $items.Count}catch{row 'startup' 'unavailable' 'Startup inventory unavailable'}
                """)
        };
        if (includeExternal) modules.Add(Module("network-probes", "Approved network probes", DiagnosticCategory.Network, """
            try {$gateways=@(Get-NetRoute -DestinationPrefix '0.0.0.0/0'|Select-Object -ExpandProperty NextHop -Unique|Select-Object -First 4);
                if($gateways.Count -eq 0){row 'gateway' 'unknown' 'No IPv4 default gateway'}else {foreach($gateway in $gateways){$ping=New-Object System.Net.NetworkInformation.Ping;try{$r=$ping.Send($gateway,1500);row ('gateway-'+$gateway) $(if($r.Status -eq 'Success'){'healthy'}else{'unknown'}) ([string]$r.Status+'. ICMP can be blocked without a routing fault.')}finally{$ping.Dispose()}}}
            }catch{row 'gateway' 'unavailable' 'Gateway probe unavailable'}
            $names=@('www.microsoft.com','cloudflare.com','example.com');$resolved=@();$failed=@()
            foreach($n in $names){try{if(@(Resolve-DnsName $n -DnsOnly -QuickTimeout).Count -gt 0){$resolved+=$n}else{$failed+=$n}}catch{$failed+=$n}}
            if($resolved.Count -eq 0){row 'dns' 'warning' ('None of the test names resolved ('+($names -join ', ')+'). Check local DNS, VPN and policy before changing anything.')}
            elseif($failed.Count -eq 0){row 'dns' 'healthy' ('DNS resolved every test name: '+($names -join ', '))}
            else{row 'dns' 'info' ('DNS resolved '+($resolved -join ', ')+' but not '+($failed -join ', ')+'. One unresolved name usually means a filtered network (router, VPN or policy), not a DNS fault.')}
            if($resolved.Count -eq 0){row 'internet' 'unknown' 'TCP probe skipped: no test name resolved'}
            else{$target=$resolved[0];$tcp=New-Object System.Net.Sockets.TcpClient;try{$t=$tcp.ConnectAsync($target,443);if($t.Wait(5000) -and $tcp.Connected){row 'internet' 'healthy' ($target+':443 TCP reachable; not an HTTPS or whole-internet test')}else{row 'internet' 'unknown' ('TCP probe to '+$target+':443 timed out')}}catch{row 'internet' 'unknown' 'TCP probe failed; proxy, firewall or endpoint conditions may explain this'}finally{$tcp.Dispose()}}
            """, external: true));
        // Plain-language modules shared with their own pages (Diagnose → Windows Update, Diagnose → Battery & startup).
        modules.Insert(2, new WindowsUpdateDiagnostic(probe));
        modules.Add(new BatteryStartupDiagnostic(probe));
        modules.Add(new ActivationDiagnostic(probe));
        // Significant gaming configuration problems only; details and changes live in Performance → Gaming.
        modules.Add(new GamingDiagnostic());
        return modules.ToArray();
    }
}
