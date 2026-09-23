using System.Text.Json;
using IgezziGuard;

// Plain-language summaries must read evidence honestly: failures are never "good", missing data stays unknown.
internal static class InsightChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    internal static void Run()
    {
        Connection(); Latency(); Events(); Performance(); Maintain(); Shield(); Summaries();
    }
    private static ConnectionFacts Online => new(1, "Wi-Fi (Wi-Fi)", true, true, false, true, 20, true, 15, true, 30);
    private static void Connection()
    {
        var ok = ConnectionVerdict.Evaluate(Online, "report");
        Check(ok.Status == CardStatus.Good && ok.Headline == "You're online" && ok.Report == "report", "healthy connection reads as online and keeps the report");
        var dns = ConnectionVerdict.Evaluate(Online with { DnsWorks = false, NameReachable = null }, "");
        Check(dns.Headline.Contains("names aren't resolving") && dns.Status == CardStatus.Problem, "working internet with failed DNS is identified as a DNS problem");
        var offline = ConnectionVerdict.Evaluate(Online with { DnsWorks = true, IpReachable = false, NameReachable = false }, "");
        Check(offline.Headline == "No internet access right now", "resolving names without reaching any server is not reported as online");
        Check(ConnectionVerdict.Evaluate(Online with { ActiveAdapters = 0, HasGateway = false }, "").Headline.Contains("not connected"), "no active adapter says not connected");
        Check(ConnectionVerdict.Evaluate(Online with { HasGateway = false }, "").Headline.Contains("not to a router"), "missing gateway is named");
        var filtered = ConnectionVerdict.Evaluate(Online with { IpReachable = false }, "");
        Check(filtered.Headline == "You're online" && filtered.Cards.Single(c => c.Title == "Internet reachability").Status == CardStatus.Review, "blocked 1.1.1.1 alone is a review note, not an outage");
        Check(ConnectionVerdict.Evaluate(Online with { DnsMs = 900 }, "").Headline.Contains("slow"), "slow DNS is surfaced");
        Check(ConnectionVerdict.Evaluate(Online with { VpnActive = true }, "").Cards[0].Body.Contains("VPN"), "active VPN is mentioned");
        // Found on a real router: example.com and example.org returned NXDOMAIN while other names resolved.
        var partial = ConnectionVerdict.Evaluate(Online with { DnsFailures = ["example.com"] }, "");
        Check(partial.Headline == "You're online" && partial.Cards.Single(c => c.Title == "Name lookups (DNS)") is { Status: CardStatus.Info } card && card.Body.Contains("example.com"),
            "one filtered test name is a note, not a DNS failure");
    }
    private static void Latency()
    {
        PingStats Probe(string target, bool gateway, params long[] replies) => new(target, gateway, 10, replies);
        long[] fast = [2, 3, 2, 3, 2, 3, 2, 3, 2, 3], internet = [20, 22, 21, 20, 23, 22, 20, 21, 22, 20];
        var healthy = ConnectionVerdict.Latency([Probe("192.168.1.1", true, fast), Probe("1.1.1.1", false, internet)], (85, "Home", "5 GHz"), "");
        Check(healthy.Headline == "Response times look healthy" && healthy.Status == CardStatus.Good, "fast router and internet read as healthy");
        var local = ConnectionVerdict.Latency([Probe("192.168.1.1", true, 80, 90, 120, 85), Probe("1.1.1.1", false, internet)], (35, null, null), "");
        Check(local.Headline.Contains("between this PC and your router"), "slow lossy router link is placed at home");
        var upstream = ConnectionVerdict.Latency([Probe("192.168.1.1", true, fast), Probe("1.1.1.1", false, 300, 310, 290)], (null, null, null), "");
        Check(upstream.Headline.Contains("further out"), "healthy router with slow internet points upstream");
        var silent = ConnectionVerdict.Latency([Probe("192.168.1.1", true), Probe("1.1.1.1", false, internet)], (null, null, null), "");
        Check(silent.Cards.Single(c => c.Title == "Your router").Status == CardStatus.Info && silent.Headline == "Response times look healthy", "router ignoring pings is not a fault");
        Check(ConnectionVerdict.Latency([Probe("1.1.1.1", false)], (null, null, null), "").Cards.Single(c => c.Title == "The internet").Status == CardStatus.Unknown, "no internet replies stay unknown");
        var wifi = NetworkDiagnostics.ParseWifi("    Name                   : Wi-Fi\r\n    SSID                   : Home\r\n    Band                   : 5 GHz\r\n    Signal                 : 86%\r\n");
        Check(wifi == (86, "Home", "5 GHz"), "English netsh signal, SSID and band parsed");
        Check(NetworkDiagnostics.ParseWifi("    Signalqualität : 86%").Signal is null, "non-English netsh output leaves signal unknown");
        Check(ConnectionVerdict.Speed(3).Status == CardStatus.Review && ConnectionVerdict.Speed(150).Status == CardStatus.Good, "speed meaning bands");
    }
    private static void Events()
    {
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        CrashEvent E(string provider, int id, string level, int hoursAgo = 1, string message = "") => new(now.AddHours(-hoursAgo), "System", provider, id, id, level, message);
        Check(EventKnowledge.Describe("Microsoft-Windows-Kernel-Power", 41).Impact == EventImpact.Serious, "unexpected restart is serious");
        Check(EventKnowledge.Describe("DistributedCOM", 10016).Impact == EventImpact.Noise, "DCOM 10016 is harmless noise");
        Check(EventKnowledge.Describe("Microsoft-Windows-DistributedCOM", 10029).Impact == EventImpact.Noise, "full DCOM provider name and timeout notice recognized");
        Check(EventKnowledge.Describe("Microsoft-Windows-Ntfs", 55).Impact == EventImpact.Serious, "full Ntfs provider name recognized");
        Check(EventKnowledge.Describe("Microsoft-Windows-WHEA-Logger", 17).Impact == EventImpact.Review && EventKnowledge.Describe("Microsoft-Windows-WHEA-Logger", 18).Impact == EventImpact.Serious, "corrected and fatal WHEA differ");
        Check(EventKnowledge.Describe("Unrelated provider", 41).Impact == EventImpact.Other, "event ID alone does not trigger a known meaning");
        Check(EventKnowledge.Subject("Application Error", 1000, "Faulting application name: game.exe, version: 1.0") == "game.exe", "crashed app name extracted");
        Check(EventKnowledge.Subject("Application Error", 1000, "Name der fehlerhaften Anwendung: game.exe") is null, "non-English message leaves app name unknown");
        var quiet = EventInsights.Summarize([E("DistributedCOM", 10016, "Error"), E("DistributedCOM", 10016, "Error", 3)], "coverage", now);
        Check(quiet.Headline == "No serious problems in the last 7 days" && quiet.Cards.Any(c => c.Title == "Usually harmless"), "only noise reads as no serious problems");
        var crash = EventInsights.Summarize([E("Microsoft-Windows-Kernel-Power", 41, "Critical"), E("EventLog", 6008, "Error"), E("Application Error", 1000, "Error", 2, "Faulting application name: game.exe, version")], "coverage", now);
        Check(crash.Status == CardStatus.Problem && crash.Headline.StartsWith("1 unexpected restart"), "restart markers in the same minute count once");
        Check(crash.Cards.Any(c => c.Body.Contains("game.exe")), "app crash card names the app");
        Check(crash.Report.Contains("coverage") && crash.Report.Contains("GROUPED BY SOURCE"), "event report keeps coverage and grouped evidence");
        Check(EventInsights.Summarize([], "System: unavailable", now).Report.Contains("System: unavailable"), "empty event summary keeps collection errors");
        Check(CrashTimeline.Summary([E("disk", 153, "Warning"), E("Microsoft-Windows-Kernel-Power", 41, "Critical")]).StartsWith("IN SHORT: Unexpected restart"), "timeline summary lists the most serious first");
        Check(CrashTimeline.Summary([]).Contains("No warnings"), "empty timeline summary says so");
    }
    private static void Performance()
    {
        var run = new PerformanceRun(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(30), 30, 20, 60, 50, 60, 8L << 30);
        Check(PerformanceInsights.Sample(run, null, "r").Status == CardStatus.Good, "idle sample reads as headroom");
        Check(PerformanceInsights.Sample(run with { PeakCommitPercent = 95 }, null, "r").Headline.Contains("nearly full"), "commit near limit flagged");
        Check(PerformanceInsights.Sample(run with { AverageCpu = 90 }, null, "r").Headline.Contains("very busy"), "sustained high CPU flagged");
        Check(PerformanceInsights.Sample(run, run with { AverageCpu = 40 }, "r").Cards.Any(c => c.Body.Contains("lower by 20.0")), "baseline comparison in plain words");
        Check(PerformanceInsights.Change("CPU", 1).Contains("about the same"), "tiny changes are not called improvements");
        var session = new SavedSession(1, "PC", run, [new(DateTimeOffset.UnixEpoch, null, null, null, "unavailable")]);
        var monitored = PerformanceInsights.Monitoring(session, null, "r");
        Check(monitored.Cards.Single(c => c.Title == "Disk activity").Status == CardStatus.Unknown && monitored.Cards.Single(c => c.Title == "Graphics (GPU)").Status == CardStatus.Unknown, "missing disk and GPU counters stay unknown");
        Check(ResultPresentation.MemoryStatus("Usable physical RAM: 16 GiB\nAvailable RAM: 0,5 GiB\nCommitted memory: 10 GiB / limit 20 GiB") == CardStatus.Problem, "under 5% free RAM is a problem (comma decimals)");
        Check(ResultPresentation.MemoryStatus("Usable physical RAM: 16 GiB\nAvailable RAM: 8 GiB\nCommitted memory: 10 GiB / limit 20 GiB") == CardStatus.Good, "half free RAM is comfortable");
        Check(ResultPresentation.MemoryStatus("Memory counters unavailable") == CardStatus.Unknown, "missing memory counters stay unknown");
        Check(ResultPresentation.DriveStatus(["C:\\: 4 GiB free / 500 GiB total"]) == CardStatus.Problem && ResultPresentation.DriveStatus(["D:\\: unavailable (denied)"]) == CardStatus.Unknown, "drive space thresholds and unreadable drives");
    }
    private static void Maintain()
    {
        Check(FileCategories.Of(".MP4") == "Videos" && FileCategories.Of(".xyz") == "Other", "file categories ignore case and default to Other");
        var files = new[] { new InventoryFile(@"C:\a.mp4", 300, DateTime.UtcNow, DateTime.UtcNow), new InventoryFile(@"C:\b.jpg", 100, DateTime.UtcNow, DateTime.UtcNow), new InventoryFile(@"C:\c.mkv", 200, DateTime.UtcNow, DateTime.UtcNow) };
        var totals = FileCategories.Totals(files);
        Check(totals[0] == ("Videos", 2, 500L) && totals[1].Name == "Pictures", "category totals largest first");
        Check(StartupAdvice.Describe("SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe").Kind == "Security", "Windows Security tray is kept");
        Check(StartupAdvice.Describe("Discord", @"C:\Users\x\AppData\Local\Discord\Update.exe --processStart Discord.exe").Kind == "Convenience", "an app launched through its updater reads as the app");
        Check(StartupAdvice.Describe("MicrosoftEdgeUpdate", @"C:\Program Files (x86)\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe").Kind == "Updater", "a plain updater stays an updater");
        Check(StartupAdvice.Describe("Spotify", @"C:\Users\x\AppData\Roaming\Spotify\Spotify.exe /minimized").Kind == "Convenience", "media launcher is optional");
        Check(StartupAdvice.Describe("Dismiss", @"C:\tools\dismiss.exe").Kind == "Unknown", "short tokens need word boundaries (msi inside dismiss)");
        var group = new DuplicateGroup("h", [new(@"C:\x\big.iso", 2L << 30, DateTime.UtcNow, DateTime.UtcNow), new(@"C:\y\big.iso", 2L << 30, DateTime.UtcNow, DateTime.UtcNow), new(@"C:\z\big.iso", 2L << 30, DateTime.UtcNow, DateTime.UtcNow)]);
        var dup = DuplicateInsights.Summarize(new([group], 0, "cov"), "r");
        Check(dup.Headline.StartsWith("4 GiB") && dup.Cards.Any(c => c.Body == "cov"), "reclaimable space keeps one copy per set");
        Check(DuplicateInsights.Summarize(new([], 0, "cov"), "r").Headline == "No duplicate files found", "no duplicates message");
    }
    private static void Shield()
    {
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        string day(int ago) => $"/Date({now.AddDays(-ago).ToUnixTimeMilliseconds()})/";
        using var on = JsonDocument.Parse($$"""{"AMRunningMode":"Normal","AntivirusEnabled":true,"RealTimeProtectionEnabled":true,"BehaviorMonitorEnabled":true,"IsTamperProtected":true,"AntivirusSignatureAge":0,"QuickScanEndTime":"{{day(2)}}"}""");
        using var none = JsonDocument.Parse("""{"Detections":[],"ThreatNames":[]}""");
        var clean = ShieldInsights.Defender(on.RootElement, none.RootElement, "r", now);
        Check(clean.Status == CardStatus.Good && clean.Cards.Any(c => c.Body.Contains("does not prove")), "clean Defender keeps the no-proof caveat");
        using var off = JsonDocument.Parse("""{"AntivirusEnabled":true,"RealTimeProtectionEnabled":false,"BehaviorMonitorEnabled":true,"IsTamperProtected":true}""");
        Check(ShieldInsights.Defender(off.RootElement, none.RootElement, "r", now).Headline.Contains("turned off"), "real-time protection off is the headline");
        using var threat = JsonDocument.Parse($$"""{"Detections":[{"ThreatID":7,"InitialDetectionTime":"{{day(3)}}","ActionSuccess":false}],"ThreatNames":[{"ThreatID":7,"ThreatName":"Trojan:Test","IsActive":true}]}""");
        var active = ShieldInsights.Defender(on.RootElement, threat.RootElement, "r", now);
        Check(active.Status == CardStatus.Problem && active.Cards.Any(c => c.Body.Contains("Trojan:Test")), "active threat is named and flagged");
        using var old = JsonDocument.Parse($$"""{"Detections":[{"ThreatID":7,"InitialDetectionTime":"{{day(60)}}"}],"ThreatNames":[]}""");
        Check(ShieldInsights.Defender(on.RootElement, old.RootElement, "r", now).Headline.Contains("no recent"), "detections older than 30 days are not recent");
        using var empty = JsonDocument.Parse("{}");
        Check(ShieldInsights.Defender(empty.RootElement, none.RootElement, "r", now).Cards[0].Status == CardStatus.Unknown, "missing Defender flags are unknown, not healthy");
        Check(ShieldInsights.DateValue("/Date(0)/") is null && ShieldInsights.DateValue("garbage") is null, "placeholder and invalid dates rejected");
        var scan = new ScanSummary(@"C:\x", now, now.AddSeconds(3), 10, 100, 0, 0, [new(@"C:\x\eicar.com", "hash", DetectionSeverity.Malware, "EICAR-Test-File", "d", 100, 68, now)]);
        Check(ShieldInsights.Scanner(scan, "r").Status == CardStatus.Problem && ShieldInsights.Scanner(scan with { Findings = [] }, "r").Headline == "No suspicious files found", "scanner findings and clean result");
    }
    private static void Summaries()
    {
        Check(Diagnosis.Worst([CardStatus.Good, CardStatus.Unknown]) == CardStatus.Unknown && Diagnosis.Worst([CardStatus.Review, CardStatus.Problem]) == CardStatus.Problem, "worst status ordering");
        var audit = ResultPresentation.DefenderDiagnosis("Antivirus: Enabled\nReal-time protection: Disabled — review recommended\nExclusionPath: No entries returned — absence is not verified.");
        Check(audit.Status == CardStatus.Problem && audit.Headline.Contains("turned off"), "disabled protection in audit is a problem");
        var adminOnly = ResultPresentation.DefenderDiagnosis("Antivirus: Enabled\nReal-time protection: Enabled\nExclusionPath: Administrator access required — exclusions are unknown.\r\nCONFIGURED PREFERENCES\nBehavior monitoring: Disabled — review recommended");
        Check(adminOnly.Status == CardStatus.Good && adminOnly.Headline.Contains("administrator"), "admin-only exclusions do not turn protection on into unknown");
        Check(!adminOnly.Cards[0].Body.Contains("Behavior monitoring"), "configured preferences are not mixed into reported protection");
        Check(ResultPresentation.PerformanceDiagnosis("Usable physical RAM: 16 GiB\nAvailable RAM: 8 GiB\nCommitted memory: 10 GiB / limit 20 GiB\nLOCAL FIXED-DRIVE FREE SPACE — x\nC:\\: 4 GiB free / 500 GiB total\n").Headline.Contains("drive is running out"), "full drive surfaces when memory is fine");
        var unknownAudit = ResultPresentation.Defender("Antivirus: Unknown — not reported");
        Check(unknownAudit[0].Status == CardStatus.Unknown, "unknown audit flags are not healthy");
        var failed = ResultPresentation.FromFinding(DiagnosticChecks.Result(CollectionOutcome.Failed, FindingSeverity.Unknown));
        Check(failed.Status == CardStatus.Unknown, "failed finding card is unknown, not good");
    }
}
