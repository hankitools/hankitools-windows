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
            "Fix a problem, or tune performance for what you do today."),

        new("System overview", "Fix my PC", ProductArea.System, "Fix",
            "Scan Windows in one pass, then open a tool for the details."),
        new("Fix My PC", "Full scan", ProductArea.System, "Full",
            "One read-only scan across Windows, storage, devices and security. You approve every repair."),
        new("Diagnose", "Diagnose", ProductArea.System, "Diagnose",
            "Crashes, event logs, dumps, Windows Update, activation, battery and guided checks."),
        new("Maintain", "Maintain", ProductArea.System, "Maintain",
            "Large and duplicate files, installed apps and startup entries."),
        new("Shield", "Shield", ProductArea.System, "Shield",
            "Microsoft Defender status and scans. Hanki's file scanner is experimental."),
        new("Connect", "Connect", ProductArea.System, "Connect",
            "Find where a connection slows down: adapter, router, DNS or internet."),
        new("Recovery", "Recovery", ProductArea.System, "Recovery",
            "Every change Hanki made, from both areas, with undo."),

        new("Performance overview", "Tune my PC", ProductArea.Performance, "Performance",
            "Say what you want today, review Hanki's plan, and apply only what you approve."),
        new("Gaming", "Gaming", ProductArea.Performance, "Gaming",
            "Refresh rate, GPU choice and Windows settings for games, your game list, and NVIDIA and AMD Radeon settings."),
        new("GPU", "GPU", ProductArea.Performance, "GPU",
            "Graphics hardware, drivers, displays and what Hanki can adjust."),
        new("CPU", "CPU", ProductArea.Performance, "CPU",
            "Processor configuration and Windows power plans."),
        new("Memory", "Memory", ProductArea.Performance, "Memory",
            "Free memory, what uses it, memory speed and the pagefile."),
        new("Storage", "Storage", ProductArea.Performance, "Storage",
            "SSDs and hard disks, where your games live, and drive optimization."),
        new("Performance Lab", "Performance Lab", ProductArea.Performance, "Lab",
            "Measure while you play: monitor, compare runs, find bottlenecks and stutter."),

        new("History", "History", ProductArea.History, "Sessions",
            "Scans, repairs, changes and performance tests, newest first. Kept on this PC."),
        new("System actions", "System actions", ProductArea.History, "Diagnostic",
            "Saved scans, repairs and recorded Windows changes, newest first."),
        new("Performance sessions", "Performance sessions", ProductArea.History, "Sessions",
            "Measurements and optimization tests, each with its baseline and result."),

        new("Help", "Help", ProductArea.Support, "Help",
            "Guides, community, remote help, the Assistant and Hanki Pro."),
        new("Assistant", "Assistant", ProductArea.Support, "Assistant",
            "Redact a report before sharing, then optionally ask AI to explain it."),
        new("Help & community", "Help & community", ProductArea.Support, "Help",
            "Guides, the community, remote help from someone you trust, and bug reports."),
        new("Hanki Pro", "Hanki Pro", ProductArea.Support, "Hanki",
            "Scheduled checks, automatic repairs and customer reports. Every free tool stays free."),
    ];

    /// <summary>
    /// The sidebar (HANKI-UX-300): one landing page per area. A landing page leads with its main action and opens every
    /// other page of its area as a tile; those pages show a way back.
    /// </summary>
    public static readonly IReadOnlyList<string> Sidebar = ["Home", "System overview", "Performance overview", "History", "Help"];
    public static string Landing(ProductArea area) => area switch {
        ProductArea.System => "System overview", ProductArea.Performance => "Performance overview", ProductArea.History => "History", ProductArea.Support => "Help", _ => "Home"
    };
    public static bool IsLanding(string page) => Sidebar.Contains(page);
    /// <summary>The pages a landing page opens as tiles, in sidebar order.</summary>
    public static IReadOnlyList<NavigationItem> Tools(ProductArea area) => Items.Where(i => i.Area == area && !IsLanding(i.Page)).ToArray();

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
    /// <summary>Shown above the page title so the current area is always obvious.</summary>
    public static string AreaName(ProductArea area) => area switch {
        ProductArea.System => "HANKI SYSTEM", ProductArea.Performance => "HANKI PERFORMANCE", ProductArea.History => "HISTORY", ProductArea.Support => "SUPPORT", _ => "HANKI TOOLS"
    };
    public static string Title(NavigationItem item) => item.Page switch { "Home" => "Welcome to Hanki Tools", "Fix My PC" => "Full scan", _ => item.Label };

    /// <summary>
    /// Recovery journal kinds that belong to Hanki Performance: they appear in Performance sessions, not in System actions.
    /// Recovery itself stays shared and lists every kind.
    /// </summary>
    public static readonly IReadOnlySet<string> PerformanceChangeKinds = new HashSet<string>(StringComparer.Ordinal) {
        "Power plan", "Display mode", "GPU preference", "NVIDIA setting", NvidiaPresets.ChangeKind, "Processor power", "Energy saver", "Windows gaming setting", AmdSettings.ChangeKind
    };
    public static bool IsPerformanceChange(string kind) => PerformanceChangeKinds.Contains(kind);

    /// <summary>
    /// Words for a system fault. Performance text uses observation, bottleneck, recommendation, optimization, test,
    /// baseline and comparison instead, so a healthy PC with room to optimize never reads as broken.
    /// </summary>
    public static readonly IReadOnlyList<string> FaultWords = ["repair", "broken", "fix my", "problem", "unhealthy"];
    public static bool UsesFaultLanguage(string text) => FaultWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
}
