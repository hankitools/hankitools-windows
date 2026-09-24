using IgezziGuard;

// HANKI-ARCH-200: Hanki System and Hanki Performance, and their separate histories.
internal static class ArchitectureChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    internal static void Run(string root) { NavigationModel(); Sessions(root); Timeline(); }

    private static void NavigationModel()
    {
        var items = Navigation.Items;
        Check(items.Select(i => i.Page).Distinct().Count() == items.Count, "navigation: every page id is unique");
        string[] Pages(ProductArea area) => items.Where(i => i.Area == area).Select(i => i.Label).ToArray();
        Check(Pages(ProductArea.System).SequenceEqual(["Fix my PC", "Full scan", "Diagnose", "Maintain", "Shield", "Connect", "Recovery"]), "navigation: Hanki System holds diagnose, repair, maintain, protect and recover");
        Check(Pages(ProductArea.Performance).SequenceEqual(["Tune my PC", "Gaming", "GPU", "CPU", "Memory", "Storage", "Performance Lab"]), "navigation: Hanki Performance has its own destinations");
        Check(Navigation.LabTools.SequenceEqual(["Monitor", "Comparisons", "Bottleneck Analyzer", "Stutter Diagnostics", "Benchmarks", "Advanced Tuning"]), "navigation: Performance Lab has a place for every lab tool");
        Check(Pages(ProductArea.History).SequenceEqual(["History", "System actions", "Performance sessions"]), "navigation: history keeps system actions and performance sessions apart");
        // HANKI-UX-300: five sidebar items, one landing page per area; every other page opens from its landing page.
        Check(Navigation.Sidebar.Count == 5 && Navigation.Sidebar.Select(Navigation.AreaOf).Distinct().Count() == 5
            && Enum.GetValues<ProductArea>().All(a => Navigation.Landing(a) == items.First(i => i.Area == a).Page && Navigation.IsLanding(Navigation.Landing(a))),
            "navigation: the sidebar has one landing page per area, first in its area");
        Check(Enum.GetValues<ProductArea>().Where(a => a != ProductArea.Home).All(a => Navigation.Tools(a).Count >= 2 && Navigation.Tools(a).All(t => !Navigation.IsLanding(t.Page)))
            && items.Where(i => !Navigation.IsLanding(i.Page)).All(i => Navigation.Tools(i.Area).Contains(i)), "navigation: every other page is a tile on its area's landing page");
        Check(Navigation.Find("System overview")!.Label == "Fix my PC" && Navigation.Find("Performance overview")!.Label == "Tune my PC" && Navigation.Title(Navigation.Find("Fix My PC")!) == "Full scan",
            "navigation: the areas are called Fix my PC and Tune my PC, and the scan page Full scan");
        Check(items.All(i => i.Introduction.Length <= 110), "navigation: page introductions are one short sentence");
        Check(items[0].Page == "Home" && items.Skip(1).All(i => i.Area != ProductArea.Home), "navigation: Home comes first and leads into both areas");
        Check(items.Where(i => i.Area == ProductArea.Performance).All(i => !Navigation.UsesFaultLanguage(i.Introduction)), "navigation: performance pages don't describe optimization as a fault or repair");
        Check(items.All(i => i.Icon.Split(' ').Length == 1 && i.Introduction.Length > 20), "navigation: every destination has an icon kind and an introduction");
        // Backward compatibility: every 0.17 destination still exists somewhere.
        var pages = items.Select(i => i.Page).ToHashSet();
        Check(new[] { "Home", "Diagnose", "Maintain", "Connect", "Recovery", "Assistant", "Help & community", "Hanki Pro", "Shield" }.All(pages.Contains), "navigation: existing areas keep their pages");
        Check(Navigation.Moved.Values.All(v => pages.Contains(v.Split("  /  ")[0])), "navigation: every moved tool has a destination page");
        Check(Navigation.Moved["Performance  /  Battery & startup"].StartsWith("Diagnose") && Navigation.Moved["Performance  /  Power tuning"].StartsWith("CPU"), "navigation: health checks move to System, tuning to Performance");
        Check(Navigation.AreaName(ProductArea.System) == "HANKI SYSTEM" && Navigation.AreaName(ProductArea.Performance) == "HANKI PERFORMANCE", "navigation: the current area is named above each page");
    }

    private static PerformanceMeasurement Measurement(double seconds, string source = "Performance Lab monitor", double? fps = null, double? low = null)
    {
        var metrics = new Dictionary<string, double> { [PerformanceMetrics.CpuAverage] = 40, [PerformanceMetrics.GpuAverage] = 70 };
        if (fps is not null) metrics[PerformanceMetrics.FpsAverage] = fps.Value;
        if (low is not null) metrics[PerformanceMetrics.FpsLow1] = low.Value;
        return new(Now, Now.AddSeconds(seconds), metrics, source);
    }

    private static void Sessions(string root)
    {
        Check(PerformanceComparison.Outcome(Measurement(60), null) == SessionOutcome.Measured, "sessions: a single measurement is only Measured");
        Check(PerformanceComparison.Outcome(Measurement(60), Measurement(60)) == SessionOutcome.Measured, "sessions: without frame rates, utilization changes aren't called improvements");
        Check(PerformanceComparison.Outcome(Measurement(60, fps: 100), Measurement(300, fps: 130)) == SessionOutcome.NotComparable, "sessions: runs of different length aren't compared");
        Check(PerformanceComparison.Outcome(Measurement(60, fps: 100), Measurement(60, "Other", fps: 130)) == SessionOutcome.NotComparable, "sessions: runs from different sources aren't compared");
        Check(PerformanceComparison.Outcome(Measurement(60, fps: 126, low: 87), Measurement(60, fps: 139, low: 96)) == SessionOutcome.Improved, "sessions: a clear frame-rate gain is an improvement");
        Check(PerformanceComparison.Outcome(Measurement(60, fps: 126, low: 87), Measurement(60, fps: 127, low: 88)) == SessionOutcome.NoClearChange, "sessions: a change within normal variation isn't claimed");
        Check(PerformanceComparison.Outcome(Measurement(60, fps: 126, low: 87), Measurement(60, fps: 139, low: 70)) == SessionOutcome.Worse, "sessions: a higher average with much worse 1% lows isn't an improvement");
        var table = PerformanceComparison.Table(Measurement(60, fps: 126), Measurement(60, fps: 139));
        Check(table.Contains("Average FPS") && table.Contains("126") && table.Contains("139") && table.Contains("Before"), "sessions: raw before and after values stay visible");

        var run = new SavedSession(1, "PC", new PerformanceRun(Now, Now.AddSeconds(60), 60, 30, 80, 40, 55, 4UL * 1024 * 1024 * 1024),
            [new(Now.AddSeconds(5), 1048576, 2097152, 50, null), new(Now.AddSeconds(10), null, null, 90, "Disk counter unavailable")]);
        var measured = PerformanceMetrics.FromMonitoring(run);
        Check(measured.Metrics[PerformanceMetrics.CpuPeak] == 80 && measured.Metrics[PerformanceMetrics.AvailableRamMinimum] == 4 && measured.Metrics[PerformanceMetrics.DiskRead] == 1
            && measured.Metrics[PerformanceMetrics.GpuAverage] == 70 && measured.Metrics[PerformanceMetrics.GpuPeak] == 90, "sessions: a monitoring run becomes a measurement, averaging only reported samples");

        var store = new PerformanceSessionStore(Path.Combine(root, "performance-sessions.json"));
        PerformanceSession Session(int minutes, string name = "WoW test") => new(Guid.NewGuid(), name, Now.AddMinutes(minutes), Measurement(60, fps: 118), [Guid.NewGuid()], Measurement(60, fps: 126), SessionOutcome.Improved, "");
        var first = Session(0); store.Add(first); store.Add(Session(5, "Cyberpunk benchmark"));
        var read = store.Read();
        Check(read.Count == 2 && read[0].Name == "Cyberpunk benchmark" && read[1].ChangesTested.Count == 1 && read[1].After!.Metrics[PerformanceMetrics.FpsAverage] == 126, "sessions: stored newest first with baseline, changes tested and result");
        store.Remove(first.Id);
        Check(store.Read().Count == 1, "sessions: a session can be removed");
        for (int i = 0; i < PerformanceSessionStore.Limit + 5; i++) store.Add(Session(10 + i));
        Check(store.Read().Count == PerformanceSessionStore.Limit, "sessions: history keeps the newest 100");
        bool refused = false;
        try { store.Add(Session(0, "two\nlines")); } catch (ArgumentException) { refused = true; }
        Check(refused, "sessions: names are single-line");
        File.WriteAllText(Path.Combine(root, "damaged-sessions.json"), """{"Version":9,"Sessions":[]}""");
        bool damaged = false;
        try { new PerformanceSessionStore(Path.Combine(root, "damaged-sessions.json")).Read(); } catch (IOException) { damaged = true; }
        Check(damaged, "sessions: an unknown history version is preserved, not overwritten");
    }

    private static void Timeline()
    {
        var result = new DiagnosticResult("sfc", "integrity", DiagnosticCategory.Windows, CollectionOutcome.Completed, FindingSeverity.Warning, "Protected files", "", Now, Now);
        var scan = new DiagnosticScan(Guid.NewGuid(), Now.AddMinutes(-10), Now.AddMinutes(-9), 1, 1, false, [result]);
        var started = Now.AddMinutes(-5);
        RepairAttempt Attempt(RepairState state, VerificationState verification) =>
            new("sfc-repair", started, Now.AddMinutes(state == RepairState.Pending ? -5 : -2), state, "", new(RestoreState.Created, ""), verification, [], []);
        var repairs = new[] { new RepairAuditEntry(scan.Id, Attempt(RepairState.Pending, VerificationState.NotRun)), new RepairAuditEntry(scan.Id, Attempt(RepairState.Executed, VerificationState.Fixed)) };
        var changes = new[] {
            new SettingChange(Guid.NewGuid(), Now.AddMinutes(-1), "IPv4 DNS", "adapter", "", "1.1.1.1", "Applied"),
            new SettingChange(Guid.NewGuid(), Now, "Display mode", "Display 1", "1920x1080@60", "1920x1080@165", "Applied"),
            new SettingChange(Guid.NewGuid(), Now, "Power plan", "Active", "balanced", "high", "Applied")
        };
        var timeline = SystemActions.Timeline([scan], repairs, changes);
        Check(timeline.Count == 3 && timeline[0].Kind == "Change" && timeline[1].Kind == "Repair" && timeline[2].Kind == "Scan", "system actions: scans, repairs and changes, newest first");
        Check(timeline.Count(e => e.Kind == "Repair") == 1 && timeline.Single(e => e.Kind == "Repair").Detail.Contains("now passes"), "system actions: a repair's pending record is replaced by its result");
        Check(timeline.All(e => e.Title is not ("Display mode" or "Power plan")), "system actions: performance changes stay out of system history");
        Check(timeline.Single(e => e.Kind == "Scan").Detail.Contains("1 worth reviewing"), "system actions: scans are summarised");
        Check(SystemActions.Format([]).Contains("No system actions yet"), "system actions: an empty history explains what will appear");
    }
}
