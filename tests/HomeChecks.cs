using IgezziGuard;

// HANKI-UX-301: Home search, "Your PC at a glance" and recent activity.
internal static class HomeChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    internal static void Run() { Search(); Glance(); ScanCompletion(); }

    private static void Search()
    {
        SearchEntry[] entries = [
            HomeSearch.Guide(0, "My PC restarted by itself or showed a blue screen", 5, ["Check what Windows recorded", "See what happened just before"]),
            HomeSearch.Guide(1, "My PC is slow", 4, ["See what is using the processor", "Check startup apps"]),
            HomeSearch.Guide(2, "The internet is slow or keeps dropping", 3, ["Check the connection"]),
            HomeSearch.Tool("Gaming", "game fps refresh rate hz game mode", "Refresh rate, GPU choice and Windows settings for games."),
            HomeSearch.Tool("Diagnose  /  Crash timeline", "crash blue screen timeline"),
            HomeSearch.Tool("Recovery", "undo restore revert"),
            HomeSearch.Tool("Performance Lab  /  Stutter Diagnostics", "slow speed optimize stutter"),
        ];
        string? First(string query) => HomeSearch.Find(query, entries).FirstOrDefault()?.Title;
        Check(First("laggy") == "My PC is slow" && First("bsod") == "My PC restarted by itself or showed a blue screen" && First("wifi") == "The internet is slow or keeps dropping",
            "home search: everyday words find the guided fix for a symptom");
        Check(First("fps") == "Gaming" && First("undo") == "Recovery" && First("crash timeline") == "Crash timeline", "home search: tools are found by name and by what they do");
        var slow = HomeSearch.Find("slow", entries);
        Check(slow.Take(2).All(e => e.GuidedFix) && slow.Any(e => e.Title == "Stutter Diagnostics"), "home search: guided fixes come before tools when both match");
        Check(HomeSearch.Find("  ", entries).Count == 0 && HomeSearch.Find("zzzz", entries).Count == 0 && HomeSearch.Find("slow internet", entries).Single().Title.StartsWith("The internet", StringComparison.Ordinal),
            "home search: every word must match; nothing is shown for an empty or unknown query");
        Check(entries[4] is { Title: "Crash timeline", Detail: "In Diagnose", GuidedFix: false, Target: "Diagnose  /  Crash timeline" } && entries[1] is { Detail: "Guided fix · 4 steps", Target: "guide:1" }
            && HomeSearch.Find("s", entries, limit: 3).Count == 3, "home search: results say where they lead and are limited");
    }

    private static void Glance()
    {
        Check(PcGlance.WindowsName("Windows 10 Pro", 22631) == "Windows 11 Pro" && PcGlance.WindowsName("Windows 10 Home", 19045) == "Windows 10 Home" && PcGlance.WindowsName(null, 0) == "Windows",
            "glance: Windows 11 is named from its build, since it still reports Windows 10");
        Check(PcGlance.Running(TimeSpan.FromMinutes(30)) == "Less than an hour" && PcGlance.Running(TimeSpan.FromHours(5)) == "5 hours" && PcGlance.Running(TimeSpan.FromHours(26)) == "1 day"
            && PcGlance.Running(TimeSpan.FromDays(3.5)) == "3 days" && PcGlance.Size(500L << 30) == "500 GB" && PcGlance.Size((long)(9.5 * (1L << 30))) == "9.5 GB",
            "glance: uptime and sizes read naturally");
        const long GB = 1L << 30;
        GlanceFacts Facts(long free = 200 * GB, long used = 8 * GB, double days = 2, SecurityHealth? av = SecurityHealth.Good, SecurityHealth? fw = SecurityHealth.Good) =>
            new("Windows 11 Pro 24H2", "26100.4652", TimeSpan.FromDays(days), "C:", free, 500 * GB, used, 16 * GB, "NVIDIA GeForce RTX 4070", "616.92", av, fw);
        GlanceTileModel Tile(GlanceFacts f, string label) => PcGlance.Tiles(f).Single(t => t.Label.StartsWith(label, StringComparison.Ordinal));
        var healthy = PcGlance.Tiles(Facts());
        Check(healthy.Count == 6 && healthy.All(t => t.Target.Length > 0) && Tile(Facts(), "Drive") is { Value: "200 GB free", Status: CardStatus.Good, Percent: 60 }
            && Tile(Facts(), "Memory") is { Value: "50%", Status: CardStatus.Good } && Tile(Facts(), "Protection") is { Value: "On", Status: CardStatus.Good },
            "glance: six tiles, each opening its page, with usage shown as a share");
        Check(Tile(Facts(free: 60 * GB), "Drive").Status == CardStatus.Review && Tile(Facts(free: 5 * GB), "Drive").Status == CardStatus.Problem
            && Tile(Facts(used: 15 * GB), "Memory").Status == CardStatus.Review && Tile(Facts(days: 20), "Running").Status == CardStatus.Review,
            "glance: a nearly full drive, busy memory and a long time since restart stand out");
        Check(Tile(Facts(av: SecurityHealth.Poor), "Protection").Status == CardStatus.Problem && Tile(Facts(fw: SecurityHealth.Poor), "Protection").Value == "Needs attention"
            && Tile(Facts(av: SecurityHealth.Snoozed), "Protection").Status == CardStatus.Review && Tile(Facts(av: null), "Protection") is { Value: "Not reported", Status: CardStatus.Unknown }
            && Tile(new GlanceFacts(null, null, null, "C:", null, null, null, null, null, null, null, null), "Drive").Value == "Not reported",
            "glance: protection follows Windows Security; anything Windows doesn't report says so");
    }

    private static void ScanCompletion()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        DiagnosticResult Finding() => new("fixture", "f", DiagnosticCategory.Performance, CollectionOutcome.Completed, FindingSeverity.Healthy, "Title", "Explanation.", now, now);
        var cancelled = new DiagnosticScan(Guid.NewGuid(), now, now, 15, 5, true, [Finding()]);
        var stopped = new DiagnosticScan(Guid.NewGuid(), now, now, 15, 14, false, [Finding()]);
        var adminOnlySkipped = new DiagnosticScan(Guid.NewGuid(), now, now, 15, 15, false,
            [Finding(), new("sfc", "integrity", DiagnosticCategory.Windows, CollectionOutcome.Unavailable, FindingSeverity.Unknown, "SFC", "Needs administrator rights.", now, now)]);
        Check(!HomeScanStatus.Finished(cancelled) && !HomeScanStatus.Finished(stopped) && HomeScanStatus.Finished(adminOnlySkipped),
            "home scan status: cancelled and incomplete scans remain distinguishable; checks skipped for administrator rights count as run");
    }
}
