namespace IgezziGuard;

public sealed record TimelineEntry(DateTimeOffset At, string Kind, string Title, string Detail);

/// <summary>
/// Hanki System history: scans, repairs and recorded Windows changes, newest first. Changes made by Hanki Performance
/// share the Recovery journal but belong to Performance sessions, so they are left out here.
/// </summary>
public static class SystemActions
{
    public static IReadOnlyList<TimelineEntry> Timeline(IEnumerable<DiagnosticScan> scans, IEnumerable<RepairAuditEntry> repairs, IEnumerable<SettingChange> changes)
    {
        var entries = new List<TimelineEntry>();
        foreach (var scan in scans) {
            int attention = scan.Results.Count(r => r.Severity == FindingSeverity.Critical), review = scan.Results.Count(r => r.Severity == FindingSeverity.Warning);
            string state = scan.Cancelled ? " Cancelled before it finished." : scan.Complete ? "" : " Some checks were unavailable.";
            entries.Add(new(scan.Ended, "Scan", "Full System Scan",
                attention + review == 0 ? $"{scan.Results.Count} results, nothing needs attention.{state}" : $"{attention} need attention, {review} worth reviewing.{state}"));
        }
        // The audit records each attempt as Pending before it runs, then again with its result; show the latest record of each.
        foreach (var attempt in repairs.GroupBy(e => (e.ScanId, e.Attempt.ActionId, e.Attempt.Started)).Select(g => g.Last().Attempt))
            entries.Add(new(attempt.Ended, "Repair", RepairGuidance.Name(attempt.ActionId),
                attempt.State == RepairState.Pending ? "Started but no result was recorded. Check Windows before trying again." : RepairGuidance.Outcome(attempt)));
        foreach (var change in changes.Where(c => !Navigation.IsPerformanceChange(c.Kind)))
            entries.Add(new(change.At, "Change", change.Kind, $"{change.Target}: {change.Before} → {change.After} ({change.Status})"));
        return entries.OrderByDescending(e => e.At).ToArray();
    }

    public static string Format(IReadOnlyList<TimelineEntry> entries) => entries.Count == 0
        ? "No system actions yet. Scans from Fix My PC, repairs and changes you make in Hanki System will appear here."
        : string.Join("\r\n\r\n", entries.Select(e => $"{e.At.ToLocalTime():g}  ·  {e.Kind}: {e.Title}\r\n{e.Detail}"));
}
