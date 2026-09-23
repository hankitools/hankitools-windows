using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace IgezziGuard;

/// <summary>One plain-language result: a summary card on its own page and a finding in the Full System Scan.</summary>
internal sealed record HealthItem(string Id, string Title, CardStatus Status, string Body, string Evidence);

internal static class HealthItems
{
    internal static FindingSeverity Severity(CardStatus status) => status switch {
        CardStatus.Good => FindingSeverity.Healthy, CardStatus.Review => FindingSeverity.Warning, CardStatus.Problem => FindingSeverity.Critical,
        CardStatus.Info => FindingSeverity.Informational, _ => FindingSeverity.Unknown };
    internal static IReadOnlyList<DiagnosticResult> Findings(string module, DiagnosticCategory category, IEnumerable<HealthItem> items, DateTimeOffset start, DateTimeOffset end) =>
        items.Select(i => new DiagnosticResult(module, i.Id, category,
            i.Status == CardStatus.Unknown ? CollectionOutcome.Unavailable : CollectionOutcome.Completed, Severity(i.Status), i.Title, i.Body,
            start, end, i.Evidence, "Current local observation; nothing was changed.",
            i.Status == CardStatus.Unknown ? FindingConfidence.Unknown : FindingConfidence.Confirmed)).ToArray();
    internal static Diagnosis Diagnose(IReadOnlyList<HealthItem> items, string headline, string report) =>
        Diagnosis.From(report, items.Select(i => new ResultCard(i.Title, i.Body, i.Status)).ToList(), headline);
    internal static string Report(string heading, IEnumerable<HealthItem> items, IEnumerable<string> extra, string? notes)
    {
        var text = new StringBuilder(heading).Append(" • ").AppendLine(DateTimeOffset.Now.ToString("g", CultureInfo.CurrentCulture)).AppendLine("Read-only; nothing was changed.").AppendLine();
        foreach (var item in items) text.AppendLine($"{item.Title} · {item.Status}").AppendLine(item.Body).AppendLine("Evidence: " + item.Evidence).AppendLine();
        foreach (var line in extra) text.AppendLine(line);
        if (!string.IsNullOrWhiteSpace(notes)) text.AppendLine().AppendLine("Not available: " + notes);
        return text.ToString().TrimEnd();
    }
    internal static string Days(double days) => days < 1 ? "today" : days < 2 ? "yesterday" : $"{Math.Floor(days):0} days ago";
}

internal sealed record UpdateHistoryItem(DateTimeOffset Date, int Result, long HResult, string? Title, string? Client);
internal sealed record UpdateFacts(string? ServiceStartMode, bool RestartPending, DateTimeOffset? PausedUntil,
    UpdateHistoryItem[]? History, int HistoryCount, string? Notes);

internal static partial class UpdateHealth
{
    internal const string Script = """
        $notes=@();$start=$null;$restart=$false;$paused=$null;$items=@();$count=0
        try{$start=[string](Get-CimInstance -Query "SELECT StartMode FROM Win32_Service WHERE Name='wuauserv'").StartMode}catch{$notes+='Windows Update service state'}
        try{$restart=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending')}catch{$notes+='restart markers'}
        try{$p=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings';if($p.PauseUpdatesExpiryTime){$paused=([datetime]$p.PauseUpdatesExpiryTime).ToUniversalTime().ToString('o')}}catch{}
        try{$searcher=(New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher();$count=[int]$searcher.GetTotalHistoryCount()
            if($count -gt 0){$items=@($searcher.QueryHistory(0,[Math]::Min(200,$count))|Where-Object{$_.Operation -eq 1}|ForEach-Object{
                [pscustomobject]@{Date=[datetime]::SpecifyKind($_.Date,'Utc').ToString('o');Result=[int]$_.ResultCode;HResult=[long]$_.HResult;Title=[string]$_.Title;Client=[string]$_.ClientApplicationID}})}
        }catch{$notes+='update history'}
        [pscustomobject]@{ServiceStartMode=$start;RestartPending=[bool]$restart;PausedUntil=$paused;History=$items;HistoryCount=$count;Notes=($notes -join ', ')}|ConvertTo-Json -Depth 4 -Compress
        """;
    internal static UpdateFacts Parse(string json) =>
        JsonSerializer.Deserialize<UpdateFacts>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("Windows Update data unavailable.");

    // Defender definitions and platform updates install daily through Windows Update; they say nothing
    // about Windows' own monthly updates. KB numbers work in every display language.
    private static readonly string[] DefenderKbs = ["KB2267602", "KB4052623", "KB915597"];
    [GeneratedRegex(@"KB\d{6,8}", RegexOptions.IgnoreCase)] private static partial Regex KbPattern();
    internal static string? Kb(string? title) => KbPattern().Match(title ?? "") is { Success: true } m ? m.Value.ToUpperInvariant() : null;
    internal static bool IsDefender(UpdateHistoryItem item) =>
        string.Equals(item.Client, "Windows Defender", StringComparison.OrdinalIgnoreCase) || DefenderKbs.Contains(Kb(item.Title));
    /// <summary>Windows, .NET and other Microsoft updates with a KB number, excluding Defender definitions.</summary>
    internal static bool IsWindowsUpdate(UpdateHistoryItem item) => Kb(item.Title) is not null && !IsDefender(item);
    private static bool Succeeded(UpdateHistoryItem item) => item.Result is 2 or 3;
    private static bool Failed(UpdateHistoryItem item) => item.Result is 4 or 5;

    /// <summary>Well-known Windows Update error codes in plain language. Unknown codes are shown as-is.</summary>
    internal static string Explain(long hresult) => unchecked((uint)hresult) switch {
        0x80070070 => "not enough free disk space",
        0x80070002 or 0x80070003 => "Windows couldn't find files it needed; a later retry often works",
        0x800F081F => "repair files were missing; Windows' component store may need a DISM repair",
        0x80073712 => "Windows' component store is damaged; a DISM repair usually fixes this",
        0x800F0922 => "the installation couldn't finish, often because of a VPN, low space on the system-reserved partition or a connection problem",
        0x80070643 => "the installer reported a general failure",
        0x8024402C or 0x80072EE7 or 0x80072EFD => "Windows couldn't reach the update servers (check the internet connection, proxy or VPN)",
        0x80240034 => "the download didn't complete",
        0x80240016 => "another installation was running or a restart was pending",
        0x8024200D => "the update needs to be downloaded again",
        0x80070005 => "access was denied, often by security software or policy",
        _ => "not a code Hanki knows; search for it on Microsoft's support site"
    };
    internal static string Code(long hresult) => "0x" + unchecked((uint)hresult).ToString("X8", CultureInfo.InvariantCulture);
    private static string Short(string? title)
    {
        var text = string.IsNullOrWhiteSpace(title) ? "Update" : title.Trim();
        return text.Length > 90 ? text[..87] + "..." : text;
    }

    internal static IReadOnlyList<HealthItem> Evaluate(UpdateFacts facts, DateTimeOffset now)
    {
        var items = new List<HealthItem>();
        var history = facts.History ?? [];
        bool historyRead = facts.History is not null && !(facts.Notes ?? "").Contains("update history", StringComparison.Ordinal);
        if (string.Equals(facts.ServiceStartMode, "Disabled", StringComparison.OrdinalIgnoreCase))
            items.Add(new("service", "Windows Update service", CardStatus.Problem,
                "The Windows Update service is disabled, so Windows can't install updates. It is usually turned off by a tweak tool or a policy. Set it back to Manual in Services, or ask your IT department on a work PC.",
                "wuauserv start mode: Disabled"));

        var windows = history.Where(IsWindowsUpdate).ToArray();
        var last = windows.Where(Succeeded).OrderByDescending(i => i.Date).FirstOrDefault();
        bool paused = facts.PausedUntil is { } until && until > now;
        string pauseNote = paused ? $" Updates are paused until {facts.PausedUntil!.Value.ToLocalTime():d}." : "";
        if (!historyRead) items.Add(new("last-success", "Last Windows update", CardStatus.Unknown,
            "Windows Update history couldn't be read, so Hanki can't tell when updates last installed. Open Windows Update settings to check.", "Update history unavailable"));
        else if (last is null) items.Add(new("last-success", "Last Windows update", CardStatus.Review,
            "Windows hasn't recorded a successful Windows update in its update history." + pauseNote + " Open Windows Update and select Check for updates.",
            $"History entries: {facts.HistoryCount}; Windows updates found: {windows.Length}"));
        else {
            double days = (now - last.Date).TotalDays;
            var status = days <= 40 ? CardStatus.Good : days <= 70 ? CardStatus.Review : CardStatus.Problem;
            string body = $"The last Windows update installed {last.Date.ToLocalTime():d} ({HealthItems.Days(days)}): {Short(last.Title)}." + (status switch {
                CardStatus.Good => " Windows is keeping up with its monthly updates.",
                CardStatus.Review => " Windows normally installs security updates every month, so this PC may have missed one." + pauseNote + " Open Windows Update and select Check for updates.",
                _ => " This PC has missed at least two months of security updates." + pauseNote + " Open Windows Update, check for updates and review any errors below."
            });
            items.Add(new("last-success", "Last Windows update", status, body, $"{last.Date:o} {Kb(last.Title)} result {last.Result}"));
        }

        if (historyRead) {
            // A failed update that later installed is resolved; keep only the latest failure per update.
            static string Key(UpdateHistoryItem i) => Kb(i.Title) ?? i.Title ?? "";
            var recent = history.Where(i => !IsDefender(i) && i.Date > now.AddDays(-30)).ToArray();
            UpdateHistoryItem[] Unresolved(IEnumerable<UpdateHistoryItem> failed) => failed.GroupBy(Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(i => i.Date).First())
                .Where(f => !recent.Any(s => Succeeded(s) && s.Date > f.Date && string.Equals(Key(s), Key(f), StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(i => i.Date).ToArray();
            // Windows, .NET and security updates carry KB numbers. Driver and app updates don't; Windows retries
            // those by itself, and a new PC's first driver batch often logs "another install was running".
            var failures = recent.Where(i => Failed(i) && Kb(i.Title) is not null).ToArray();
            var unresolved = Unresolved(failures);
            var stuck = unresolved.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int resolved = failures.Count(f => !stuck.Contains(Key(f)));
            var drivers = Unresolved(recent.Where(i => Failed(i) && Kb(i.Title) is null));
            if (unresolved.Length == 0)
                items.Add(new("failures", "Failed updates", CardStatus.Good,
                    resolved > 0 ? $"No update is stuck. {resolved} earlier attempt{(resolved == 1 ? "" : "s")} failed in the last 30 days but installed on a later try." : "No updates failed in the last 30 days.",
                    $"Failed attempts in 30 days: {failures.Length}; unresolved: 0"));
            else {
                var lines = unresolved.Take(3).Select(f => $"{Kb(f.Title) ?? Short(f.Title)} failed on {f.Date.ToLocalTime():d} with error {Code(f.HResult)}: {Explain(f.HResult)}.");
                string more = unresolved.Length > 3 ? $" {unresolved.Length - 3} more in the technical details." : "";
                items.Add(new("failures", "Failed updates", CardStatus.Review,
                    $"{unresolved.Length} update{(unresolved.Length == 1 ? " hasn't" : "s haven't")} installed after failing in the last 30 days.\n" + string.Join("\n", lines) + more +
                    "\nRestart, then try Check for updates again. If the same update keeps failing, run Windows' own Windows Update troubleshooter.",
                    string.Join("; ", unresolved.Select(f => $"{Kb(f.Title) ?? "no KB"} {Code(f.HResult)} {f.Date:o}"))));
            }
            if (drivers.Length > 0) {
                var latest = drivers[0];
                items.Add(new("driver-updates", "Driver and app updates", CardStatus.Info,
                    $"{drivers.Length} driver or app update{(drivers.Length == 1 ? "" : "s")} didn't install in the last 30 days, most recently {Short(latest.Title)} on {latest.Date.ToLocalTime():d} (error {Code(latest.HResult)}: {Explain(latest.HResult)}). " +
                    "Windows retries these by itself; they only matter if a device or app isn't working.",
                    string.Join("; ", drivers.Select(f => $"{Short(f.Title)} {Code(f.HResult)} {f.Date:o}"))));
            }
        }
        if (facts.RestartPending)
            items.Add(new("reboot", "Restart needed", CardStatus.Review,
                "Windows is waiting for a restart to finish installing updates. Restart when it suits you; choose Restart, not Shut down.", "Windows Update or servicing restart marker present"));
        if (paused)
            items.Add(new("paused", "Updates paused", CardStatus.Info,
                $"Updates are paused until {facts.PausedUntil!.Value.ToLocalTime():d}. Windows resumes them automatically after that.", $"PauseUpdatesExpiryTime {facts.PausedUntil:o}"));
        return items;
    }
    internal static string Headline(IReadOnlyList<HealthItem> items) => Diagnosis.Worst(items.Select(i => i.Status)) switch {
        CardStatus.Problem => "Windows Update isn't keeping this PC up to date",
        CardStatus.Review => "Windows Update needs a look",
        CardStatus.Unknown => "Windows Update status could not be fully read",
        _ => "Windows Update is working"
    };
    internal static IEnumerable<string> HistoryLines(UpdateFacts facts) =>
        new[] { "Recent update history (newest first, Defender definitions omitted):" }.Concat((facts.History ?? []).Where(i => !IsDefender(i))
            .OrderByDescending(i => i.Date).Take(15).Select(i => $"{i.Date.ToLocalTime():g} · {(i.Result switch { 2 => "Succeeded", 3 => "Succeeded with errors", 4 => "Failed", 5 => "Cancelled", _ => "Result " + i.Result })}{(Failed(i) ? " " + Code(i.HResult) : "")} · {Short(i.Title)}"));
}

/// <summary>Full System Scan module; the Diagnose → Windows Update page uses the same collector and rules.</summary>
internal sealed class WindowsUpdateDiagnostic(IDiagnosticProbe? source = null) : IDiagnosticModule
{
    private readonly IDiagnosticProbe source = source ?? new WindowsDiagnosticProbe();
    public string Id => "update";
    public string DisplayName => "Windows Update";
    public DiagnosticCategory Category => DiagnosticCategory.Windows;
    public DiagnosticRequirements Requirements => new();
    internal async Task<UpdateFacts> ReadAsync(CancellationToken token) => UpdateHealth.Parse(await source.ReadAsync(UpdateHealth.Script, 90, token));
    public async Task<IReadOnlyList<DiagnosticResult>> CollectAsync(DiagnosticContext context, IProgress<DiagnosticProgress>? progress, CancellationToken token)
    {
        var start = DateTimeOffset.UtcNow; progress?.Report(new(Id, "Reading Windows Update history"));
        var facts = await ReadAsync(token);
        return HealthItems.Findings(Id, Category, UpdateHealth.Evaluate(facts, DateTimeOffset.UtcNow), start, DateTimeOffset.UtcNow);
    }
}
