namespace IgezziGuard;

public sealed record ResultCard(string Title, string Body);

internal static class ResultPresentation
{
    public static ResultCard[] Defender(string report)
    {
        // These are Hanki's own labelled report fields, not inference from arbitrary log text.
        var lines = Lines(report.Split("RAW EVIDENCE • REVIEW BEFORE SHARING", StringSplitOptions.None)[0]);
        string Fields(params string[] prefixes) => string.Join("\n", lines.Where(l => prefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal))));
        return [
            new("Reported protection", OrUnknown(Fields("Antivirus:", "Real-time protection:", "Behavior monitoring:", "Downloaded-file protection:", "Network inspection:", "Tamper protection:", "Status could not be read:"))),
            new("Security intelligence", OrUnknown(Fields("Running mode:", "Signature version:", "Signatures updated:"))),
            new("Exclusions & access", OrUnknown(Fields("Exclusion", "Preferences could not be read:"))),
            new("What to do next", "Review any disabled or unknown protections. If exclusions require administrator access, reopen Hanki as administrator and repeat the audit. Enabled protection does not prove the PC is free of threats. View technical details for configured preferences and raw evidence.")
        ];
    }
    public static ResultCard[] Performance(string report)
    {
        var lines = Lines(report);
        string Fields(params string[] prefixes) => string.Join("\n", lines.Where(l => prefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal))));
        string advice = Fields("Review now:", "This snapshot does not justify", "Peak commit since boot", "Recommendation:", "PAGEFILE ADVISOR: counters unavailable");
        return [
            new("Memory at this moment", OrUnknown(Fields("Usable physical RAM:", "Available RAM:", "Committed memory:", "Peak system commit since boot:", "Memory counters unavailable:"))),
            new("Pagefile guidance", string.IsNullOrWhiteSpace(advice) ? "Memory guidance is unavailable. Review technical details and try another snapshot." : advice),
            new("What to do next", "Measure again during your usual heavy workload, or open monitoring to compare a longer session. Commit is promised memory, not pagefile disk activity. One snapshot cannot determine a custom pagefile size. View technical details for pagefiles, drive space and process working sets.")
        ];
    }
    private static string[] Lines(string text) => text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
    private static string OrUnknown(string text) => string.IsNullOrWhiteSpace(text) ? "Not available — review technical details." : text;
}
