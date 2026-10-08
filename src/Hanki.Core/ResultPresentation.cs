namespace IgezziGuard;

/// <summary>How a single observation reads to the user. Describes evidence, never an overall PC-health score.</summary>
public enum CardStatus { Info, Good, Review, Problem, Unknown }

/// <summary>One observation. An optional action opens the tool or setting that helps with it.</summary>
public sealed record ResultCard(string Title, string Body, CardStatus Status = CardStatus.Info, string? ActionLabel = null, Action? Action = null);

/// <summary>A plain-language headline plus cards; the technical report stays available separately.</summary>
public sealed record Diagnosis(string Report, CardStatus Status, string Headline, IReadOnlyList<ResultCard> Cards)
{
    public static Diagnosis From(string report, IReadOnlyList<ResultCard> cards, string? headline = null)
    {
        var status = Worst(cards.Select(c => c.Status));
        return new(report, status, headline ?? DefaultHeadline(status), cards);
    }
    internal static CardStatus Worst(IEnumerable<CardStatus> statuses)
    {
        var all = statuses.ToArray();
        return all.Contains(CardStatus.Problem) ? CardStatus.Problem : all.Contains(CardStatus.Review) ? CardStatus.Review :
            all.Contains(CardStatus.Unknown) ? CardStatus.Unknown : all.Contains(CardStatus.Good) ? CardStatus.Good : CardStatus.Info;
    }
    private static string DefaultHeadline(CardStatus status) => status switch {
        CardStatus.Problem => "Something here needs your attention",
        CardStatus.Review => "A few things are worth reviewing",
        CardStatus.Unknown => "Some information could not be collected",
        CardStatus.Good => "Nothing concerning in this check",
        _ => "Check finished"
    };
}

internal static class ResultPresentation
{
    public static ResultCard[] Defender(string report)
    {
        // These are Hanki's own labelled report fields, not inference from arbitrary log text.
        var summary = report.Split("RAW EVIDENCE • REVIEW BEFORE SHARING", StringSplitOptions.None)[0];
        // Reported status sits above the configured preferences, which reuse some labels (for example "Behavior monitoring").
        var status = Lines(summary.Split("CONFIGURED PREFERENCES", StringSplitOptions.None)[0]);
        var lines = Lines(summary);
        string Fields(string[] source, params string[] prefixes) => string.Join("\n", source.Where(l => prefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal))));
        string protection = Fields(status, "Antivirus:", "Real-time protection:", "Behavior monitoring:", "Downloaded-file protection:", "Network inspection:", "Tamper protection:", "Status could not be read:");
        string intelligence = Fields(status, "Running mode:", "Signature version:", "Signatures updated:");
        string exclusions = Fields(lines, "Exclusion", "Preferences could not be read:");
        return [
            new("Reported protection", OrUnknown(protection), FlagStatus(protection)),
            new("Security intelligence", OrUnknown(intelligence), intelligence.Contains("Not available", StringComparison.Ordinal) || intelligence.Length == 0 ? CardStatus.Unknown : CardStatus.Info),
            new("Exclusions & access", OrUnknown(exclusions), exclusions.Contains("unknown", StringComparison.OrdinalIgnoreCase) || exclusions.Length == 0 ? CardStatus.Unknown : exclusions.Split('\n').Any(l => !l.Contains("No entries", StringComparison.Ordinal)) ? CardStatus.Review : CardStatus.Good),
            new("What to do next", "Review any disabled or unknown protections. If exclusions require administrator access, reopen Hanki as administrator and repeat the audit. Enabled protection does not prove the PC is free of threats. See Technical details for configured preferences and raw evidence.")
        ];
    }
    /// <summary>The headline and its color follow reported protection; admin-only exclusions don't turn "on" into "unknown".</summary>
    public static Diagnosis DefenderDiagnosis(string report)
    {
        var cards = Defender(report);
        var (protection, exclusions) = (cards[0].Status, cards[2].Status);
        return protection switch {
            CardStatus.Good => new(report, exclusions == CardStatus.Review ? CardStatus.Review : CardStatus.Good,
                "Microsoft Defender reports its protections as on" + (exclusions == CardStatus.Unknown ? " (exclusions need administrator access)" : exclusions == CardStatus.Review ? ", with exclusions to review" : ""), cards),
            CardStatus.Problem => new(report, CardStatus.Problem, "One or more Defender protections are turned off", cards),
            _ => new(report, CardStatus.Unknown, "Defender did not report every protection state", cards)
        };
    }
    // Enabled everywhere is Good; any explicit Disabled is a Problem; missing values stay Unknown.
    private static CardStatus FlagStatus(string fields) =>
        fields.Length == 0 ? CardStatus.Unknown : fields.Contains("Disabled", StringComparison.Ordinal) ? CardStatus.Problem :
        fields.Contains("Unknown", StringComparison.Ordinal) || fields.Contains("could not be read", StringComparison.Ordinal) ? CardStatus.Unknown : CardStatus.Good;

    public static ResultCard[] Performance(string report)
    {
        var lines = Lines(report);
        string Fields(params string[] prefixes) => string.Join("\n", lines.Where(l => prefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal))));
        string advice = Fields("Review now:", "This snapshot does not justify", "Peak commit since boot", "Recommendation:", "PAGEFILE ADVISOR: counters unavailable");
        string memory = Fields("Usable physical RAM:", "Available RAM:", "Committed memory:", "Peak system commit since boot:", "Memory counters unavailable:");
        var cards = new List<ResultCard> {
            new("Memory at this moment", OrUnknown(memory), memory.Length == 0 || memory.Contains("unavailable", StringComparison.Ordinal) ? CardStatus.Unknown : MemoryStatus(report)),
            new("Pagefile guidance", string.IsNullOrWhiteSpace(advice) ? "Memory guidance is unavailable. Review technical details and try another snapshot." : advice,
                advice.Contains("Review now:", StringComparison.Ordinal) ? CardStatus.Review : string.IsNullOrWhiteSpace(advice) ? CardStatus.Unknown : CardStatus.Info),
            new("What to do next", "Measure again during your usual heavy workload, or open monitoring to compare a longer session. Commit is promised memory, not pagefile disk activity. One snapshot cannot determine a custom pagefile size. See Technical details for pagefiles, drive space and process working sets.")
        };
        var drives = Section(report, "LOCAL FIXED-DRIVE FREE SPACE");
        if (drives.Length > 0) cards.Add(new("Drive space", string.Join("\n", drives), DriveStatus(drives)));
        var processes = Section(report, "LARGEST PROCESS WORKING SETS").Where(l => l.Contains("(PID", StringComparison.Ordinal)).Take(5).ToArray();
        if (processes.Length > 0) cards.Add(new("Biggest memory users right now", string.Join("\n", processes) + "\nShared memory is counted in more than one app, so these do not add up to your total.", CardStatus.Info));
        return [.. cards];
    }
    /// <summary>Memory leads; a nearly full drive is called out when memory itself is fine.</summary>
    public static Diagnosis PerformanceDiagnosis(string report)
    {
        var cards = Performance(report);
        var memory = cards[0].Status;
        var drive = cards.FirstOrDefault(c => c.Title == "Drive space")?.Status ?? CardStatus.Info;
        if (memory == CardStatus.Good && drive is CardStatus.Review or CardStatus.Problem)
            return new(report, drive, "Memory is fine, but a drive is running out of space", cards);
        return new(report, memory, memory switch {
            CardStatus.Good => "Memory has comfortable headroom right now",
            CardStatus.Review => "Memory is getting tight at the moment",
            CardStatus.Problem => "Memory is nearly exhausted at the moment",
            _ => "Memory counters were not available"
        }, cards);
    }

    // Share of RAM still available, and commit against its limit, from Hanki's labelled lines.
    internal static CardStatus MemoryStatus(string report)
    {
        var total = Gib(report, "Usable physical RAM:"); var available = Gib(report, "Available RAM:");
        var commit = CommitRatio(report);
        if (total is not > 0 || available is null) return commit is null ? CardStatus.Unknown : commit >= 0.9 ? CardStatus.Problem : commit >= 0.8 ? CardStatus.Review : CardStatus.Good;
        double free = available.Value / total.Value;
        if (free < 0.05 || commit >= 0.95) return CardStatus.Problem;
        if (free < 0.15 || commit >= 0.85) return CardStatus.Review;
        return CardStatus.Good;
    }
    internal static CardStatus DriveStatus(IEnumerable<string> drives)
    {
        var status = CardStatus.Good;
        foreach (var line in drives) {
            var match = System.Text.RegularExpressions.Regex.Match(line, @"([\d.,]+) GiB free / ([\d.,]+) GiB total");
            if (!match.Success || !TryNumber(match.Groups[1].Value, out var free) || !TryNumber(match.Groups[2].Value, out var size) || size <= 0) { if (status == CardStatus.Good) status = CardStatus.Unknown; continue; }
            if (free / size < 0.05) return CardStatus.Problem;
            if (free / size < 0.10) status = CardStatus.Review;
        }
        return status;
    }
    private static double? Gib(string report, string label)
    {
        var line = Lines(report).FirstOrDefault(l => l.StartsWith(label, StringComparison.Ordinal));
        var match = line is null ? null : System.Text.RegularExpressions.Regex.Match(line, @"([\d.,]+) GiB");
        return match is { Success: true } && TryNumber(match.Groups[1].Value, out var value) ? value : null;
    }
    private static double? CommitRatio(string report)
    {
        var line = Lines(report).FirstOrDefault(l => l.StartsWith("Committed memory:", StringComparison.Ordinal));
        var match = line is null ? null : System.Text.RegularExpressions.Regex.Match(line, @"([\d.,]+) GiB / limit ([\d.,]+) GiB");
        return match is { Success: true } && TryNumber(match.Groups[1].Value, out var used) && TryNumber(match.Groups[2].Value, out var limit) && limit > 0 ? used / limit : null;
    }
    // Hanki formats with the current culture; accept either decimal separator.
    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    private static string[] Section(string report, string heading)
    {
        var lines = report.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, l => l.StartsWith(heading, StringComparison.Ordinal));
        if (start < 0) return [];
        return lines.Skip(start + 1).TakeWhile(l => l.Trim().Length > 0 && !l.All(c => char.IsUpper(c) || !char.IsLetter(c))).Select(l => l.Trim()).ToArray();
    }
    /// <summary>A diagnostic finding as a card: explanation plus the manual next step, when one applies.</summary>
    public static ResultCard FromFinding(DiagnosticResult r)
    {
        var status = r.Outcome is CollectionOutcome.Failed or CollectionOutcome.Unavailable or CollectionOutcome.Cancelled ? CardStatus.Unknown : r.Severity switch {
            FindingSeverity.Critical => CardStatus.Problem, FindingSeverity.Warning => CardStatus.Review, FindingSeverity.Healthy => CardStatus.Good,
            FindingSeverity.Informational => CardStatus.Info, _ => CardStatus.Unknown };
        var next = FindingAnalysis.Recommend(r)?.ManualAction;
        return new(r.Title, r.Explanation + (next is null ? "" : "\nWhat to do: " + next), status);
    }
    private static string[] Lines(string text) => text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
    private static string OrUnknown(string text) => string.IsNullOrWhiteSpace(text) ? "Not available — review technical details." : text;
}
