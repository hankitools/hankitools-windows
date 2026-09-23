using System.Globalization;
using System.Text.Json;

namespace IgezziGuard;

/// <summary>Plain-language reading of Defender and scanner evidence. Clean results never prove a PC is malware-free.</summary>
public static class ShieldInsights
{
    public static Diagnosis Defender(JsonElement status, JsonElement findings, string report, DateTimeOffset now)
    {
        var cards = new List<ResultCard>();
        bool? Flag(string name) => status.ValueKind == JsonValueKind.Object && status.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        var flags = new (string Key, string Label)[] { ("AntivirusEnabled", "Antivirus"), ("RealTimeProtectionEnabled", "Real-time protection"), ("BehaviorMonitorEnabled", "Behavior monitoring"), ("IsTamperProtected", "Tamper protection") };
        var off = flags.Where(f => Flag(f.Key) == false).Select(f => f.Label).ToArray();
        var unknown = flags.Where(f => Flag(f.Key) is null).Select(f => f.Label).ToArray();
        string mode = Text(status, "AMRunningMode") ?? "unknown";
        cards.Add(off.Length > 0 ? new("Protection", $"Turned off: {string.Join(", ", off)}. Running mode: {mode}. Another antivirus or an organization policy can explain this; otherwise turn it back on in Windows Security.", CardStatus.Problem)
            : unknown.Length > 0 ? new("Protection", $"Not reported: {string.Join(", ", unknown)}. Running mode: {mode}.", CardStatus.Unknown)
            : new("Protection", $"Antivirus, real-time protection, behavior monitoring and tamper protection are on. Running mode: {mode}.", CardStatus.Good));

        int? age = status.ValueKind == JsonValueKind.Object && status.TryGetProperty("AntivirusSignatureAge", out var a) && a.TryGetInt32(out var days) ? days : null;
        cards.Add(age switch {
            null => new("Security intelligence", "Definition age was not reported.", CardStatus.Unknown),
            > 7 => new("Security intelligence", $"Definitions are {age} days old. Defender normally updates daily; use Update Defender definitions or Windows Update.", CardStatus.Review),
            _ => new("Security intelligence", age == 0 ? "Definitions were updated today." : $"Definitions were updated {Days(age.Value)}.", CardStatus.Good)
        });

        var quick = Date(status, "QuickScanEndTime"); var full = Date(status, "FullScanEndTime");
        string ScanLine(string label, DateTimeOffset? when) => when is null ? $"{label}: no record" : $"{label}: {Days((int)Math.Max(0, (now - when.Value).TotalDays))} ({when.Value.ToLocalTime():g})";
        cards.Add(new("Recent scans", ScanLine("Last quick scan", quick) + "\n" + ScanLine("Last full scan", full) + "\nDefender also scans files as they are opened while real-time protection is on." +
            (quick is null ? "\nNo quick scan is on record; you can start one with Defender quick scan above." : ""),
            quick is null ? CardStatus.Info : (now - quick.Value).TotalDays > 14 ? CardStatus.Review : CardStatus.Good));

        var names = new Dictionary<string, (string Name, bool Active)>();
        if (findings.ValueKind == JsonValueKind.Object && findings.TryGetProperty("ThreatNames", out var threats) && threats.ValueKind == JsonValueKind.Array)
            foreach (var t in threats.EnumerateArray())
                if (Text(t, "ThreatID") is { } id) names[id] = (Text(t, "ThreatName") ?? "Unnamed threat", t.TryGetProperty("IsActive", out var act) && act.ValueKind == JsonValueKind.True);
        var recent = new List<(string Name, bool Active, bool Handled)>();
        if (findings.ValueKind == JsonValueKind.Object && findings.TryGetProperty("Detections", out var detections) && detections.ValueKind == JsonValueKind.Array)
            foreach (var d in detections.EnumerateArray()) {
                if (DateValue(Text(d, "InitialDetectionTime")) is not { } found || (now - found).TotalDays > 30) continue;
                var info = Text(d, "ThreatID") is { } id && names.TryGetValue(id, out var n) ? n : ("Unnamed threat", false);
                recent.Add((info.Item1, info.Item2, d.TryGetProperty("ActionSuccess", out var ok) && ok.ValueKind == JsonValueKind.True));
            }
        int active = recent.Count(r => r.Active);
        cards.Add(recent.Count == 0 ? new("Detections in the last 30 days", "None reported. That's good news, but it does not prove the PC is free of threats.", CardStatus.Good)
            : new("Detections in the last 30 days", $"{recent.Count} detection(s): " + string.Join(", ", recent.Select(r => r.Name).Distinct().Take(5)) +
                (active > 0 ? $"\n{active} still reported as active. Open Windows Security → Protection history to act on it." : "\nAll are reported as handled. Check Windows Security → Protection history for details."),
                active > 0 ? CardStatus.Problem : CardStatus.Review));

        string headline = active > 0 ? "Defender reports an active threat"
            : off.Length > 0 ? "Some Defender protection is turned off"
            : recent.Count > 0 ? "Defender recently found and handled threats"
            : unknown.Length > 0 ? "Defender did not report every protection state"
            : "Defender is on and reports no recent detections";
        string next = active > 0 ? "Open Windows Security → Protection history and follow its recommended action. Then run a Defender full scan."
            : off.Length > 0 ? "Open Windows Security → Virus & threat protection and turn protection back on, unless another antivirus you trust is in charge."
            : age > 7 ? "Update Defender definitions, then refresh this page."
            : "No action needed. Run a quick scan if something seems wrong, and keep Windows updated.";
        cards.Add(new("What to do next", next));
        return Diagnosis.From(report, cards, headline);
    }

    public static Diagnosis Scanner(ScanSummary summary, string report)
    {
        var cards = new List<ResultCard>();
        int serious = summary.Findings.Count(f => f.Severity >= DetectionSeverity.High);
        string coverage = $"{summary.FilesScanned:N0} files checked in {Math.Max(1, (summary.FinishedAt - summary.StartedAt).TotalSeconds):0} s. {summary.Skipped:N0} skipped (over 512 MB or unreadable), {summary.Errors:N0} errors.";
        cards.Add(new("What was checked", coverage + $"\n{summary.Target}", summary.Errors > 0 || summary.Skipped > 0 ? CardStatus.Info : CardStatus.Good));
        cards.Add(summary.Findings.Count == 0
            ? new("Findings", "Nothing matched Hanki's test signature or simple heuristics. This experimental scanner is not antivirus; use Defender for real protection.", CardStatus.Good)
            : new("Findings", string.Join("\n", summary.Findings.Take(8).Select(f => $"{Label(f.Severity)}: {f.DetectionName} — {Path.GetFileName(f.FilePath)}")) +
                (summary.Findings.Count > 8 ? $"\n…and {summary.Findings.Count - 8} more in the technical details." : ""), serious > 0 ? CardStatus.Problem : CardStatus.Review));
        cards.Add(new("What to do next", summary.Findings.Count == 0
            ? "No action needed. For a thorough check, run a Defender quick or full scan in Scans & alerts."
            : "Don't open flagged files. Run a Defender scan on them (Scans & alerts), and check the technical details for the file path and SHA-256 before deleting anything. Heuristic matches can be false alarms."));
        string headline = summary.Findings.Count == 0 ? "No suspicious files found" : serious > 0 ? $"{summary.Findings.Count} file(s) need your attention" : $"{summary.Findings.Count} file(s) worth reviewing";
        return Diagnosis.From(report, cards, headline);
    }
    private static string Label(DetectionSeverity severity) => severity switch { DetectionSeverity.Malware => "Known test/malware signature", DetectionSeverity.High => "High-risk pattern", _ => "Suspicious pattern" };
    internal static string Days(int days) => days switch { 0 => "today", 1 => "yesterday", _ => $"{days} days ago" };
    private static string? Text(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? v.ToString() : null;
    private static DateTimeOffset? Date(JsonElement e, string name) => DateValue(Text(e, name));
    /// <summary>Windows PowerShell serializes dates as /Date(ms)/; ISO strings are accepted too.</summary>
    internal static DateTimeOffset? DateValue(string? value)
    {
        if (value is null) return null;
        var match = System.Text.RegularExpressions.Regex.Match(value, @"^/Date\((-?\d+)(?:[+-]\d{4})?\)/$");
        if (match.Success && long.TryParse(match.Groups[1].Value, out var ms)) { try { var d = DateTimeOffset.FromUnixTimeMilliseconds(ms); return d.Year < 2000 ? null : d; } catch (ArgumentOutOfRangeException) { return null; } }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) && parsed.Year >= 2000 ? parsed : null;
    }
}
