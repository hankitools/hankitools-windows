using System.Text.Json;
namespace IgezziGuard;

/// <param name="Source">Time server of the last successful sync, as the Time Service recorded it.</param>
/// <param name="Failures">Time Service warnings about unreachable or unsynchronized time in the last 14 days.</param>
/// <param name="Type">W32Time Parameters\Type: NTP (Set time automatically), NT5DS (domain), NoSync (off).</param>
internal sealed record TimeSyncFacts(DateTimeOffset? LastSync, string? Source, int Failures, DateTimeOffset? LastFailure, string? Type, string? StartType, string? Notes);

/// <summary>
/// Whether Windows keeps its clock right (HANKI-FIX-122). A wrong clock breaks sign-ins, secure websites and updates.
/// The Windows Time service only runs when needed, so its own event records are read instead of asking it. Read-only.
/// </summary>
internal static class TimeSync
{
    internal const string Script = """
        $notes=@();$last=$null;$source=$null;$fail=0;$lastFail=$null;$type=$null;$start=$null
        try{$e=@(Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Microsoft-Windows-Time-Service';StartTime=(Get-Date).AddDays(-30)} -MaxEvents 300 -ErrorAction Stop)
            $ok=@($e|Where-Object {$_.Id -in 35,37})|Select-Object -First 1
            if($ok){$last=$ok.TimeCreated.ToUniversalTime().ToString('o');$source=[string]$ok.Properties[0].Value}
            $bad=@($e|Where-Object {$_.Id -in 36,47,129,134,142 -and $_.TimeCreated -gt (Get-Date).AddDays(-14)});$fail=$bad.Count
            if($bad.Count -gt 0){$lastFail=$bad[0].TimeCreated.ToUniversalTime().ToString('o')}
        }catch{if($_.FullyQualifiedErrorId -notmatch 'NoMatchingEventsFound'){$notes+='time events'}}
        try{$type=[string](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\W32Time\Parameters' -ErrorAction Stop).Type}catch{$notes+='time settings'}
        try{$start=[string](Get-Service W32Time -ErrorAction Stop).StartType}catch{$notes+='time service'}
        [pscustomobject]@{LastSync=$last;Source=$source;Failures=$fail;LastFailure=$lastFail;Type=$type;StartType=$start;Notes=($notes -join ', ')}|ConvertTo-Json -Compress
        """;
    internal static TimeSyncFacts Parse(string json) =>
        JsonSerializer.Deserialize<TimeSyncFacts>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("Time sync data unavailable.");
    /// <summary>"time.windows.com,0x9 (ntp.m|…)" → "time.windows.com".</summary>
    internal static string Server(string? source) => string.IsNullOrWhiteSpace(source) ? "the time server" : source.Split(',', ' ')[0];
    private const string Settings = "Settings → Time & language → Date & time";

    internal static IReadOnlyList<HealthItem> Evaluate(TimeSyncFacts facts, DateTimeOffset now)
    {
        const string Id = "time", Title = "Clock sync";
        string evidence = $"Last sync {facts.LastSync?.ToString("o") ?? "none in 30 days"} ({facts.Source ?? "?"}); {facts.Failures} Time Service warnings in 14 days; Type {facts.Type ?? "?"}; W32Time {facts.StartType ?? "?"}";
        if (string.Equals(facts.Type, "NoSync", StringComparison.OrdinalIgnoreCase))
            return [new(Id, "Automatic time is off", CardStatus.Review,
                $"Set time automatically is turned off, so Windows never corrects its clock. A clock that's a few minutes off can break sign-ins, secure websites and updates. Turn it on in {Settings}.", evidence)];
        if (string.Equals(facts.StartType, "Disabled", StringComparison.OrdinalIgnoreCase))
            return [new(Id, "The Windows Time service is disabled", CardStatus.Review,
                $"Windows can't sync its clock while the Windows Time service is disabled. Set it back to Manual in Services, then use Sync now in {Settings}.", evidence)];
        if (facts.LastSync is { } last && (now - last).TotalDays <= 14)
            return [new(Id, Title, CardStatus.Good, $"Windows last set its clock from {Server(facts.Source)} {HealthItems.Days((now - last).TotalDays)} ({last.ToLocalTime():g}).", evidence)];
        if ((facts.Notes ?? "").Contains("time events", StringComparison.Ordinal))
            return [new(Id, Title, CardStatus.Unknown, "Windows' record of clock syncs couldn't be read.", evidence)];
        string since = facts.LastSync is { } old ? $"The last successful sync was {old.ToLocalTime():d}." : "No successful sync is recorded in the last 30 days.";
        if (facts.Failures > 0)
            return [new(Id, "Windows can't reach its time server", CardStatus.Review,
                $"Windows tried to sync its clock and failed {(facts.Failures == 1 ? "once" : $"{facts.Failures} times")} in the last 14 days. {since} A firewall, VPN or router that blocks time requests (UDP port 123) is the usual reason. Use Sync now in {Settings}, and check that the clock is right.", evidence)];
        return [new(Id, Title, CardStatus.Info,
            $"{since} Windows syncs about once a week, when the PC is on at the right moment. If the clock looks wrong, use Sync now in {Settings}.", evidence)];
    }
}

/// <summary>Full scan module: whether Windows keeps its clock in sync.</summary>
internal sealed class TimeSyncDiagnostic(IDiagnosticProbe? source = null) : IDiagnosticModule
{
    private readonly IDiagnosticProbe source = source ?? new WindowsDiagnosticProbe();
    public string Id => "time-sync";
    public string DisplayName => "Clock sync";
    public DiagnosticCategory Category => DiagnosticCategory.Windows;
    public DiagnosticRequirements Requirements => new();
    public async Task<IReadOnlyList<DiagnosticResult>> CollectAsync(DiagnosticContext context, IProgress<DiagnosticProgress>? progress, CancellationToken token)
    {
        var start = DateTimeOffset.UtcNow; progress?.Report(new(Id, "Reading clock sync history"));
        var facts = TimeSync.Parse(await source.ReadAsync(TimeSync.Script, 60, token));
        return HealthItems.Findings(Id, Category, TimeSync.Evaluate(facts, DateTimeOffset.UtcNow), start, DateTimeOffset.UtcNow);
    }
}
