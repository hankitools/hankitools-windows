using System.Globalization;
using System.Text.Json;
namespace IgezziGuard;

internal sealed record BatteryReading(double? DesignCapacity, double? FullChargeCapacity, int? CycleCount);
/// <summary>Kernel-Boot event 27 boot type: 0 full start, 1 Fast Startup (hybrid boot), 2 resume from hibernation.</summary>
internal sealed record BootRecord(DateTimeOffset Time, int Type);
internal sealed record BatteryStartupFacts(int BatteryCount, BatteryReading[]? Batteries, DateTimeOffset? LastFullBoot, bool? FastStartup,
    BootRecord[]? Boots, DateTimeOffset? SignIn, double? MainPathMs, double? BootMs, string? Notes);

internal static class BatteryStartup
{
    // Battery capacity comes from Windows' own battery report (standard user; the temporary XML file is
    // deleted). Startup facts come from the System log and, where Windows records it, boot performance.
    internal const string Script = """
        $notes=@();$count=0;$batteries=@();$lastBoot=$null;$fast=$null;$boots=@();$signin=$null;$main=$null;$total=$null
        try{$count=@(Get-CimInstance Win32_Battery).Count}catch{$notes+='battery inventory'}
        if($count -gt 0){$file=Join-Path $env:TEMP ('hanki-battery-'+[guid]::NewGuid().ToString('N')+'.xml')
            try{$null=(& "$env:SystemRoot\System32\powercfg.exe" /batteryreport /xml /output $file /duration 1 2>&1|Out-String)
                $x=[xml](Get-Content -LiteralPath $file -Raw)
                foreach($b in $x.SelectNodes("//*[local-name()='Batteries']/*[local-name()='Battery']")){
                    $d=$b.SelectSingleNode("*[local-name()='DesignCapacity']");$f=$b.SelectSingleNode("*[local-name()='FullChargeCapacity']");$c=$b.SelectSingleNode("*[local-name()='CycleCount']")
                    $batteries+=[pscustomobject]@{DesignCapacity=$(if($d -and $d.InnerText){[double]$d.InnerText});FullChargeCapacity=$(if($f -and $f.InnerText){[double]$f.InnerText});CycleCount=$(if($c -and $c.InnerText){[int]$c.InnerText})}}
            }catch{$notes+='battery report'}finally{if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file -Force}}}
        try{$lastBoot=(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToString('o')}catch{$notes+='last restart time'}
        try{$v=(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power').HiberbootEnabled;$h=(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Power').HibernateEnabled
            if($null -ne $v){$fast=[bool]($v -eq 1 -and $h -ne 0)}}catch{}
        try{$events=@(Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Microsoft-Windows-Kernel-Boot';Id=27} -MaxEvents 10)
            $boots=@($events|ForEach-Object{[pscustomobject]@{Time=$_.TimeCreated.ToUniversalTime().ToString('o');Type=[int]$_.Properties[0].Value}})
            try{$s=@(Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Microsoft-Windows-Winlogon';Id=7001;StartTime=$events[0].TimeCreated} -MaxEvents 20)|Sort-Object TimeCreated|Select-Object -First 1
                if($s){$signin=$s.TimeCreated.ToUniversalTime().ToString('o')}}catch{}
        }catch{$notes+='startup history'}
        try{$e=Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Diagnostics-Performance/Operational';Id=100} -MaxEvents 1;$data=@{}
            ([xml]$e.ToXml()).Event.EventData.Data|ForEach-Object{$data[$_.Name]=$_.'#text'};$main=[double]$data['MainPathBootTime'];$total=[double]$data['BootTime']}catch{}
        [pscustomobject]@{BatteryCount=$count;Batteries=$batteries;LastFullBoot=$lastBoot;FastStartup=$fast;Boots=$boots;SignIn=$signin;MainPathMs=$main;BootMs=$total;Notes=($notes -join ', ')}|ConvertTo-Json -Depth 4 -Compress
        """;
    internal static BatteryStartupFacts Parse(string json) =>
        JsonSerializer.Deserialize<BatteryStartupFacts>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("Battery and startup data unavailable.");
    private static bool Missing(BatteryStartupFacts facts, string part) => (facts.Notes ?? "").Contains(part, StringComparison.Ordinal);
    internal static string BootType(int type) => type switch { 0 => "a full start", 1 => "a Fast Startup (resumed after Shut down)", 2 => "a resume from hibernation", _ => "a start" };

    internal static IReadOnlyList<HealthItem> Evaluate(BatteryStartupFacts facts, DateTimeOffset now)
    {
        var items = new List<HealthItem>();
        var batteries = facts.Batteries ?? [];
        if (Missing(facts, "battery inventory"))
            items.Add(new("battery", "Battery health", CardStatus.Unknown, "Windows didn't report whether this PC has a battery.", "Win32_Battery unavailable"));
        else if (facts.BatteryCount == 0)
            items.Add(new("battery", "Battery health", CardStatus.Info, "No battery found. This looks like a desktop PC, so battery health doesn't apply.", "Win32_Battery: none"));
        else if (batteries.Length == 0)
            items.Add(new("battery", "Battery health", CardStatus.Unknown, "This PC has a battery, but Windows' battery report couldn't be read. Try again, or run powercfg /batteryreport yourself.", "Battery report unavailable"));
        for (int index = 0; index < batteries.Length && facts.BatteryCount > 0; index++) {
            var b = batteries[index];
            string id = index == 0 ? "battery" : "battery-" + (index + 1), title = batteries.Length == 1 ? "Battery health" : $"Battery {index + 1} health";
            string evidence = $"Design {b.DesignCapacity?.ToString("0", CultureInfo.InvariantCulture) ?? "?"} mWh, full charge {b.FullChargeCapacity?.ToString("0", CultureInfo.InvariantCulture) ?? "?"} mWh, cycles {b.CycleCount?.ToString(CultureInfo.InvariantCulture) ?? "?"}";
            if (b.DesignCapacity is not > 0 || b.FullChargeCapacity is not >= 0) {
                items.Add(new(id, title, CardStatus.Unknown, "Windows didn't report this battery's original and current capacity.", evidence)); continue;
            }
            double health = Math.Min(1, b.FullChargeCapacity.Value / b.DesignCapacity.Value);
            string cycles = b.CycleCount is > 0 ? $", after {b.CycleCount} charge cycles" : "";
            var (status, body) = health >= 0.8
                ? (CardStatus.Good, $"The battery holds {health:P0} of its original capacity{cycles}. That's healthy.")
                : health >= 0.5
                    ? (CardStatus.Review, $"The battery holds {health:P0} of its original capacity{cycles}, so a full charge lasts noticeably less than when it was new. That's normal wear; consider a replacement if battery life matters to you.")
                    : (CardStatus.Problem, $"The battery holds only {health:P0} of its original capacity{cycles}. Expect much shorter battery life; replacing the battery is worth considering.");
            items.Add(new(id, title, status, body, evidence));
        }

        var boots = (facts.Boots ?? []).OrderByDescending(b => b.Time).ToArray();
        bool fastStartup = facts.FastStartup == true || boots.Any(b => b.Type == 1);
        if (facts.LastFullBoot is not { } lastFull)
            items.Add(new("restart", "Last full restart", CardStatus.Unknown, "Windows didn't report when it last restarted.", "LastBootUpTime unavailable"));
        else {
            double days = (now - lastFull).TotalDays;
            string evidence = $"LastBootUpTime {lastFull:o}; Fast Startup {(facts.FastStartup?.ToString() ?? "unknown")}";
            if (days <= 7)
                items.Add(new("restart", "Last full restart", CardStatus.Good, $"Windows last fully restarted {lastFull.ToLocalTime():g} ({HealthItems.Days(days)}).", evidence));
            else
                items.Add(new("restart", "Last full restart", CardStatus.Review,
                    $"Windows hasn't fully restarted for {Math.Floor(days):0} days (since {lastFull.ToLocalTime():d})." +
                    (fastStartup ? " Fast Startup is on, so Shut down followed by switching on doesn't restart Windows; only Restart does." : "") +
                    " Use Restart now and then: it finishes updates and clears memory, which often helps a slow PC.", evidence));
        }
        if (boots.Length > 0) {
            var latest = boots[0];
            string signIn = "";
            if (facts.SignIn is { } signed && signed >= latest.Time && (signed - latest.Time).TotalMinutes <= 60) {
                double seconds = (signed - latest.Time).TotalSeconds;
                signIn = seconds < 2 ? " Sign-in followed straight away."
                    : (seconds < 120 ? $" You signed in {seconds:0} seconds later" : $" You signed in {seconds / 60:0} minutes later") + " (this includes any time at the sign-in screen).";
            }
            items.Add(new("last-startup", "Last startup", CardStatus.Info,
                $"The last startup was {latest.Time.ToLocalTime():g}, {BootType(latest.Type)}." + signIn,
                $"Kernel-Boot 27 type {latest.Type} at {latest.Time:o}; sign-in {facts.SignIn?.ToString("o") ?? "not found"}"));
        } else if (Missing(facts, "startup history"))
            items.Add(new("last-startup", "Last startup", CardStatus.Unknown, "Windows' startup history couldn't be read.", "Kernel-Boot events unavailable"));
        if (facts.MainPathMs is > 0 and < 3_600_000) {
            double seconds = facts.MainPathMs.Value / 1000;
            var (status, body) = seconds <= 40
                ? (CardStatus.Good, $"At its last full start, Windows reached the desktop in {seconds:0} seconds.")
                : (CardStatus.Review, $"At its last full start, Windows took {seconds:0} seconds to reach the desktop. That's slower than typical; apps that start with Windows are the most common reason. Review them in Maintain → Startup / undo.");
            items.Add(new("boot-time", "Startup time", status, body, $"Diagnostics-Performance 100: MainPathBootTime {facts.MainPathMs:0} ms, BootTime {facts.BootMs?.ToString("0", CultureInfo.InvariantCulture) ?? "?"} ms"));
        }
        return items;
    }
    internal static string Headline(IReadOnlyList<HealthItem> items)
    {
        var battery = items.Where(i => i.Id.StartsWith("battery", StringComparison.Ordinal)).Select(i => i.Status).ToArray();
        return Diagnosis.Worst(items.Select(i => i.Status)) switch {
            CardStatus.Problem when battery.Contains(CardStatus.Problem) => "The battery has worn down a lot",
            CardStatus.Problem or CardStatus.Review => "A couple of things are worth a look",
            CardStatus.Unknown => "Some battery or startup information could not be read",
            _ => "Battery and startup look fine"
        };
    }
    internal static IEnumerable<string> BootLines(BatteryStartupFacts facts) =>
        new[] { "Recent startups (newest first):" }.Concat((facts.Boots ?? []).OrderByDescending(b => b.Time).Select(b => $"{b.Time.ToLocalTime():g} · {BootType(b.Type)}"));
}

/// <summary>Full System Scan module; Diagnose → Battery & startup uses the same collector and rules.</summary>
internal sealed class BatteryStartupDiagnostic(IDiagnosticProbe? source = null) : IDiagnosticModule
{
    private readonly IDiagnosticProbe source = source ?? new WindowsDiagnosticProbe();
    public string Id => "battery-startup";
    public string DisplayName => "Battery and startup";
    public DiagnosticCategory Category => DiagnosticCategory.Performance;
    public DiagnosticRequirements Requirements => new();
    internal async Task<BatteryStartupFacts> ReadAsync(CancellationToken token) => BatteryStartup.Parse(await source.ReadAsync(BatteryStartup.Script, 90, token));
    public async Task<IReadOnlyList<DiagnosticResult>> CollectAsync(DiagnosticContext context, IProgress<DiagnosticProgress>? progress, CancellationToken token)
    {
        var start = DateTimeOffset.UtcNow; progress?.Report(new(Id, "Reading battery and startup history"));
        var facts = await ReadAsync(token);
        return HealthItems.Findings(Id, Category, BatteryStartup.Evaluate(facts, DateTimeOffset.UtcNow), start, DateTimeOffset.UtcNow);
    }
}
