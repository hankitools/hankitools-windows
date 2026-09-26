namespace IgezziGuard;

/// <summary>Keep actionable evidence, coverage gaps and ordinary observations distinct.</summary>
internal static class ScanPresentation
{
    internal static bool NeedsAttention(DiagnosticResult result) => result.Severity is FindingSeverity.Warning or FindingSeverity.Critical;
    internal static bool HasGap(DiagnosticResult result) => result.Outcome != CollectionOutcome.Completed || result.Severity == FindingSeverity.Unknown;
    internal static CardStatus Status(DiagnosticResult result) => result.Severity switch {
        FindingSeverity.Critical => CardStatus.Problem, FindingSeverity.Warning => CardStatus.Review,
        FindingSeverity.Healthy => CardStatus.Good, FindingSeverity.Unknown => CardStatus.Unknown, _ => CardStatus.Info
    };
    internal static string Headline(DiagnosticScan scan)
    {
        int count = scan.Results.Count(NeedsAttention);
        if (count > 0) return Localizer.Format("Findings to review: {0}", count);
        return Localizer.T(scan.Complete && scan.Results.Count > 0 ? "No issues found in the completed checks" : "No issues found so far — some checks are incomplete");
    }
    internal static string? Route(DiagnosticResult result) => result.ModuleId switch {
        "storage" => "Maintain  /  Files & storage", "update" => "Diagnose  /  Windows Update",
        "activation" => "Diagnose  /  Windows Activation", "devices" => "Windows / Settings",
        "events" or "app-crashes" => "Diagnose  /  Crash timeline", "network-link" or "network" => "Connect  /  Guided troubleshooting",
        "defender" or "security" => "Shield  /  Defender audit", "performance" => "Memory  /  Memory & pagefile",
        "battery-startup" => "Diagnose  /  Battery & startup", "gaming" => "Performance overview", _ => null
    };
}
