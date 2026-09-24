namespace IgezziGuard;

/// <summary>
/// Hanki is one app with two product areas (HANKI-ARCH-200, docs/SYSTEM-AND-PERFORMANCE.md):
/// Hanki System diagnoses, repairs, maintains, protects and recovers Windows; Hanki Performance measures,
/// analyzes, optimizes and verifies performance. History keeps system actions and performance sessions apart.
/// </summary>
public enum ProductArea { Home, System, Performance, History, Support }

/// <param name="Page">Unique page id: the workspace tab's text and the start of its "Find a tool" route.</param>
/// <param name="Label">Sidebar label; several areas have an "Overview".</param>
/// <param name="Icon">A single-word ToolIcon kind.</param>
public sealed record NavigationItem(string Page, string Label, ProductArea Area, string Icon, string Introduction);

public static class Navigation
{
    public static readonly IReadOnlyList<NavigationItem> Items = [
        new("Home", "Home", ProductArea.Home, "Home",
            "Diagnose and repair Windows in Hanki System, or measure and optimize performance in Hanki Performance."),

        new("System overview", "Overview", ProductArea.System, "Overview",
            "Find what's wrong and fix it safely. Every tool here checks, maintains or protects Windows."),
        new("Fix My PC", "Fix My PC", ProductArea.System, "Fix",
            "One read-only scan across Windows, storage, devices, security and performance. Review each finding, approve any repair, and Hanki checks afterwards whether it worked."),
        new("Diagnose", "Diagnose", ProductArea.System, "Diagnose",
            "Explore crash events, inspect dumps, check Windows Update, activation, battery and startup, and follow guided checks to narrow down a problem."),
        new("Maintain", "Maintain", ProductArea.System, "Maintain",
            "Find large or duplicate files, review installed apps and manage startup entries to reclaim storage and reduce startup activity."),
        new("Shield", "Shield", ProductArea.System, "Shield",
            "Review Microsoft Defender protection, run scans and inspect findings. Hanki's separate file scanner is experimental."),
        new("Connect", "Connect", ProductArea.System, "Connect",
            "Check your connection, compare DNS and trace network routes to investigate slow or unreliable access and review repair options."),
        new("Recovery", "Recovery", ProductArea.System, "Recovery",
            "Review recorded changes and undo supported actions, from both Hanki System and Hanki Performance."),

        new("Performance overview", "Overview", ProductArea.Performance, "Performance",
            "Understand what limits performance and optimize it. Measure first, change one thing with your approval, measure again, then keep or revert."),
        new("Gaming", "Gaming", ProductArea.Performance, "Gaming",
            "Check your gaming setup: display refresh rate, which GPU games use, Windows gaming settings and graphics-driver settings."),
        new("GPU", "GPU", ProductArea.Performance, "GPU",
            "See your graphics hardware, driver and displays, and what Hanki can read or adjust on this GPU."),
        new("CPU", "CPU", ProductArea.Performance, "CPU",
            "See how the processor is configured and powered, and choose a Windows power plan to test."),
        new("Memory", "Memory", ProductArea.Performance, "Memory",
            "See how much memory is free, what uses the most, and whether virtual memory (the pagefile) is configured sensibly."),
        new("Storage", "Storage", ProductArea.Performance, "Storage",
            "See which drives are SSDs or hard disks, where your games are installed, and whether Windows drive optimization is running."),
        new("Performance Lab", "Performance Lab", ProductArea.Performance, "Lab",
            "Measure while you work or play: monitor the PC, compare runs, look for bottlenecks and stutter."),

        new("System actions", "System actions", ProductArea.History, "Diagnostic",
            "Saved scans, repairs and recorded Windows changes, newest first. History stays on this PC."),
        new("Performance sessions", "Performance sessions", ProductArea.History, "Sessions",
            "Measurements and optimization tests, each with its baseline, the changes tested and the result."),

        new("Assistant", "Assistant", ProductArea.Support, "Assistant",
            "Prepare and redact diagnostic reports, then use optional AI chat to help explain the evidence and explore next steps."),
        new("Help & community", "Help & community", ProductArea.Support, "Help",
            "Find guides, join the community, get remote help from someone you trust, prepare a bug report and check your version."),
        new("Hanki Pro", "Hanki Pro", ProductArea.Support, "Hanki",
            "Add scheduled checks, automatic repairs and customer reports with a licence key. Every free tool stays free."),
    ];

    /// <summary>
    /// Where each tool from the 0.17 layout went. Nothing was removed or put behind Pro; the smoke test checks every
    /// destination exists.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Moved = new Dictionary<string, string> {
        ["Full system scan"] = "Fix My PC",
        ["Diagnostic history"] = "System actions  /  Saved scans",
        ["Scan history"] = "Shield  /  File scan history",
        ["Shield · experimental"] = "Shield",
        ["Performance  /  Snapshot / pagefile"] = "Memory  /  Memory & pagefile",
        ["Performance  /  30-second sample"] = "Performance Lab  /  Comparisons",
        ["Performance  /  Long monitoring / saved runs"] = "Performance Lab  /  Monitor",
        ["Performance  /  Power tuning"] = "CPU  /  Power plans",
        ["Performance  /  Battery & startup"] = "Diagnose  /  Battery & startup",
    };

    /// <summary>Performance Lab tools, in their tab order.</summary>
    public static readonly IReadOnlyList<string> LabTools = ["Monitor", "Comparisons", "Bottleneck Analyzer", "Stutter Diagnostics", "Benchmarks", "Advanced Tuning"];

    public static NavigationItem? Find(string page) => Items.FirstOrDefault(i => i.Page == page);
    public static ProductArea AreaOf(string page) => Find(page)?.Area ?? ProductArea.Home;
    public static string GroupLabel(ProductArea area) => area switch {
        ProductArea.System => "SYSTEM", ProductArea.Performance => "PERFORMANCE", ProductArea.History => "HISTORY", ProductArea.Support => "SUPPORT", _ => ""
    };
    /// <summary>Shown above the page title so the current area is always obvious.</summary>
    public static string AreaName(ProductArea area) => area switch {
        ProductArea.System => "HANKI SYSTEM", ProductArea.Performance => "HANKI PERFORMANCE", ProductArea.History => "HISTORY", ProductArea.Support => "SUPPORT", _ => "HANKI TOOLS"
    };
    public static string Title(NavigationItem item) => item.Page switch { "Home" => "Welcome to Hanki Tools", _ => item.Page };

    /// <summary>
    /// Recovery journal kinds that belong to Hanki Performance: they appear in Performance sessions, not in System actions.
    /// Recovery itself stays shared and lists every kind.
    /// </summary>
    public static readonly IReadOnlySet<string> PerformanceChangeKinds = new HashSet<string>(StringComparer.Ordinal) {
        "Power plan", "Display mode", "GPU preference", "NVIDIA setting", "Processor power"
    };
    public static bool IsPerformanceChange(string kind) => PerformanceChangeKinds.Contains(kind);

    /// <summary>
    /// Words for a system fault. Performance text uses observation, bottleneck, recommendation, optimization, test,
    /// baseline and comparison instead, so a healthy PC with room to optimize never reads as broken.
    /// </summary>
    public static readonly IReadOnlyList<string> FaultWords = ["repair", "broken", "fix my", "problem", "unhealthy"];
    public static bool UsesFaultLanguage(string text) => FaultWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
}
