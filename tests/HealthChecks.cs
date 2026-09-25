using IgezziGuard;

// Windows Update and Battery & startup: honest, plain-language reading of Windows' own records.
internal static class HealthChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    internal static void Run() { Updates(); BatteryAndStartup(); WindowsRecords(); Collectors(); }

    private static UpdateHistoryItem Item(double daysAgo, int result, string title, long hresult = 0, string client = "MoUpdateOrchestrator") =>
        new(Now.AddDays(-daysAgo), result, hresult, title, client);
    // Titles as a Finnish Windows reports them; KB numbers are what the rules rely on.
    private const string Cumulative = "2026-09 suojauspäivitys (KB5129195)  (26200.9457)";
    private const string Defender = "Suojaustietojen päivitys tuotteelle Microsoft Defender Antivirus – KB2267602 (versio 1.459.343.0)";
    private static UpdateFacts Facts(params UpdateHistoryItem[] history) => new("Manual", false, null, history, history.Length, "");
    private static HealthItem Find(IReadOnlyList<HealthItem> items, string id) => items.Single(i => i.Id == id);

    private static void Updates()
    {
        var healthy = UpdateHealth.Evaluate(Facts(Item(0.2, 2, Defender, client: "Windows Defender"), Item(2, 2, Cumulative)), Now);
        Check(Find(healthy, "last-success") is { Status: CardStatus.Good } last && last.Body.Contains("KB5129195") && !last.Body.Contains("KB2267602"),
            "last Windows update ignores Defender's daily definitions and works with Finnish titles");
        Check(Find(healthy, "failures").Status == CardStatus.Good && UpdateHealth.Headline(healthy) == "Windows Update is working", "healthy history reads as working");

        var defenderOnly = UpdateHealth.Evaluate(Facts(Item(0.1, 2, Defender, client: "Windows Defender"), Item(50, 2, Cumulative)), Now);
        Check(Find(defenderOnly, "last-success").Status == CardStatus.Review, "daily Defender updates cannot hide 50 days without a Windows update");
        Check(Find(UpdateHealth.Evaluate(Facts(Item(80, 2, Cumulative)), Now), "last-success").Status == CardStatus.Problem, "80 days without a Windows update is a problem");
        Check(Find(UpdateHealth.Evaluate(Facts(Item(0.1, 2, Defender, client: "Windows Defender")), Now), "last-success").Status == CardStatus.Review, "no Windows update recorded at all is worth a look");

        var retried = UpdateHealth.Evaluate(Facts(Item(5, 4, Cumulative, unchecked((int)0x800F0922)), Item(3, 2, Cumulative)), Now);
        Check(Find(retried, "failures") is { Status: CardStatus.Good } ok && ok.Body.Contains("later try"), "a failure that later installed is not reported as stuck");

        var stuck = UpdateHealth.Evaluate(Facts(Item(10, 2, Cumulative), Item(4, 4, "2026-09 .NET Framework Tietoturvapäivitys (KB5126052)", unchecked((int)0x800F0922))), Now);
        Check(Find(stuck, "failures") is { Status: CardStatus.Review } failed && failed.Body.Contains("KB5126052") && failed.Body.Contains("0x800F0922") && failed.Body.Contains("VPN"),
            "stuck update names the KB, the error code and a plain-language hint");
        Check(UpdateHealth.Headline(stuck) == "Windows Update needs a look", "stuck update headline");

        // Real case: a new PC's first driver batch logged 0x80240016 ("another install was running") for several drivers.
        var driverBatch = UpdateHealth.Evaluate(Facts(Item(2, 2, Cumulative), Item(2, 4, "Realtek - AudioProcessingObject - 13.0.6000.1645", unchecked((int)0x80240016), "Device Driver Retrieval Client"),
            Item(2, 4, "Intel - System - 2.2.10000.20", unchecked((int)0x80240016), "Device Driver Retrieval Client")), Now);
        Check(Find(driverBatch, "failures").Status == CardStatus.Good && Find(driverBatch, "driver-updates") is { Status: CardStatus.Info } drivers &&
              drivers.Body.Contains("2 driver or app updates") && drivers.Body.Contains("retries"), "driver install hiccups are information, not stuck Windows updates");
        Check(UpdateHealth.Headline(driverBatch) == "Windows Update is working", "a driver batch alone doesn't turn the headline amber");
        var defenderFailure =UpdateHealth.Evaluate(Facts(Item(2, 2, Cumulative), Item(1, 4, Defender, unchecked((int)0x80240034), "Windows Defender")), Now);
        Check(Find(defenderFailure, "failures").Status == CardStatus.Good, "Defender definition hiccups are not Windows Update failures");

        Check(UpdateHealth.Code(unchecked((int)0x8024402C)) == "0x8024402C" && UpdateHealth.Explain(unchecked((int)0x8024402C)).Contains("update servers"), "negative HRESULTs format as Windows error codes");
        Check(UpdateHealth.Explain(0x12345678).Contains("not a code Hanki knows"), "unknown codes are not guessed");

        var disabled = UpdateHealth.Evaluate(new UpdateFacts("Disabled", true, Now.AddDays(5), [Item(2, 2, Cumulative)], 1, ""), Now);
        Check(Find(disabled, "service").Status == CardStatus.Problem && Find(disabled, "reboot").Status == CardStatus.Review && Find(disabled, "paused").Status == CardStatus.Info,
            "disabled service, pending restart and pause each get their own card");
        Check(!UpdateHealth.Evaluate(new UpdateFacts("Manual", false, Now.AddDays(-1), [Item(2, 2, Cumulative)], 1, ""), Now).Any(i => i.Id == "paused"), "an expired pause is not reported");
        var unreadable = UpdateHealth.Evaluate(new UpdateFacts("Manual", false, null, [], 0, "update history"), Now);
        Check(Find(unreadable, "last-success").Status == CardStatus.Unknown && !unreadable.Any(i => i.Id == "failures"), "unreadable history stays unknown, never good");

        var findings = HealthItems.Findings("update", DiagnosticCategory.Windows, unreadable, Now, Now);
        Check(findings.Single().Outcome == CollectionOutcome.Unavailable && findings.Single().Severity == FindingSeverity.Unknown, "unknown card becomes an unavailable scan finding");
        Check(HealthItems.Findings("update", DiagnosticCategory.Windows, stuck, Now, Now).Single(f => f.FindingId == "failures").Severity == FindingSeverity.Warning, "review card becomes a warning finding");

        var parsed = UpdateHealth.Parse("""{"ServiceStartMode":"Manual","RestartPending":false,"PausedUntil":null,"History":[{"Date":"2026-09-21T10:00:00.0000000Z","Result":2,"HResult":0,"Title":"(KB5129195)","Client":"MoUpdateOrchestrator"}],"HistoryCount":1,"Notes":""}""");
        Check(parsed.History!.Single().Date == new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero), "collector JSON parses with UTC dates");
    }

    private static BatteryStartupFacts Startup(int batteries = 0, BatteryReading[]? readings = null, double fullRestartDaysAgo = 1, bool? fast = false,
        BootRecord[]? boots = null, DateTimeOffset? signIn = null, double? mainPathMs = null, string notes = "") =>
        new(batteries, readings ?? [], Now.AddDays(-fullRestartDaysAgo), fast, boots ?? [], signIn, mainPathMs, mainPathMs * 2, notes);

    private static void BatteryAndStartup()
    {
        var desktop = BatteryStartup.Evaluate(Startup(), Now);
        Check(Find(desktop, "battery") is { Status: CardStatus.Info } none && none.Body.Contains("desktop"), "no battery reads as a desktop, not a fault");
        CardStatus Health(double full) => Find(BatteryStartup.Evaluate(Startup(1, [new(50000, full, 300)]), Now), "battery").Status;
        Check(Health(45000) == CardStatus.Good && Health(35000) == CardStatus.Review && Health(20000) == CardStatus.Problem, "battery wear thresholds at 80% and 50%");
        Check(Find(BatteryStartup.Evaluate(Startup(1, [new(50000, 52000, 3)]), Now), "battery").Body.Contains("100 %") ||
              Find(BatteryStartup.Evaluate(Startup(1, [new(50000, 52000, 3)]), Now), "battery").Body.Contains("100%"), "a new battery above design capacity is capped at 100%");
        Check(Find(BatteryStartup.Evaluate(Startup(1, []), Now), "battery").Status == CardStatus.Unknown, "unreadable battery report stays unknown");
        Check(Find(BatteryStartup.Evaluate(Startup(1, [new(null, 30000, null)]), Now), "battery").Status == CardStatus.Unknown, "missing design capacity is not guessed");

        Check(Find(BatteryStartup.Evaluate(Startup(fullRestartDaysAgo: 3), Now), "restart").Status == CardStatus.Good, "recent full restart is good");
        var longUp = Find(BatteryStartup.Evaluate(Startup(fullRestartDaysAgo: 12, fast: true), Now), "restart");
        Check(longUp.Status == CardStatus.Review && longUp.Body.Contains("Fast Startup") && longUp.Body.Contains("Restart"), "12 days without a real restart explains Fast Startup");

        var boot = new BootRecord(Now.AddHours(-2), 1);
        var startup = Find(BatteryStartup.Evaluate(Startup(boots: [new(Now.AddDays(-1), 0), boot], signIn: boot.Time.AddSeconds(14)), Now), "last-startup");
        Check(startup.Status == CardStatus.Info && startup.Body.Contains("Fast Startup") && startup.Body.Contains("14 seconds"), "last startup names its type and time to sign-in");
        var instant = Find(BatteryStartup.Evaluate(Startup(boots: [boot], signIn: boot.Time.AddSeconds(1)), Now), "last-startup");
        Check(instant.Body.Contains("straight away") && !instant.Body.Contains("1 seconds"), "sign-in within a second reads naturally");
        Check(Find(BatteryStartup.Evaluate(Startup(mainPathMs: 25000), Now), "boot-time").Status == CardStatus.Good &&
              Find(BatteryStartup.Evaluate(Startup(mainPathMs: 75000), Now), "boot-time").Status == CardStatus.Review, "startup time thresholds");
        Check(!BatteryStartup.Evaluate(Startup(), Now).Any(i => i.Id == "boot-time"), "no startup-time card when Windows doesn't record it");
        Check(BatteryStartup.Headline(BatteryStartup.Evaluate(Startup(1, [new(50000, 20000, 900)]), Now)) == "The battery has worn down a lot", "worn battery headline");
    }

    // HANKI-FIX-120, 121, 122: app crashes, network link speed and clock sync.
    private static void WindowsRecords()
    {
        AppCrashEvent Crash(string app, string? module = null, bool hang = false, int daysAgo = 1) => new(app, hang ? null : module, hang, Now.AddDays(-daysAgo));
        AppCrashFacts Crashes(params AppCrashEvent[] events) => new(14, events, "");
        Check(AppCrashes.Evaluate(Crashes(), Now).Single().Status == CardStatus.Good, "app crashes: none in 14 days looks fine");
        var driver = AppCrashes.Evaluate(Crashes(Crash("Game.exe", "nvwgf2umx.dll"), Crash("Game.exe", "nvwgf2umx.dll"), Crash("Game.exe", "nvwgf2umx.dll", daysAgo: 3), Crash("Notepad.exe", "ntdll.dll")), Now);
        Check(Find(driver, "app:game.exe") is { Status: CardStatus.Review } game && game.Title == "Game.exe keeps crashing" && game.Body.Contains("crashed 3 times") && game.Body.Contains("graphics driver (nvwgf2umx.dll)"),
            "app crashes: an app crashing three times in the graphics driver points to the driver");
        Check(Find(driver, "apps-occasional") is { Status: CardStatus.Info } other && other.Title == "Other app crashes" && other.Body.Contains("Notepad.exe once"),
            "app crashes: a single crash is listed as information only");
        var own = AppCrashes.Evaluate(Crashes(Crash("Tool.exe", "Tool.exe"), Crash("Tool.exe", "Tool.exe"), Crash("Tool.exe", hang: true)), Now);
        Check(Find(own, "app:tool.exe").Body.Contains("crashed twice and stopped responding once") && Find(own, "app:tool.exe").Body.Contains("in the app itself"),
            "app crashes: crashes in the app's own code suggest updating or repairing it, and hangs are counted separately");
        Check(AppCrashes.Evaluate(new(14, null, "crash history"), Now).Single().Status == CardStatus.Unknown, "app crashes: an unreadable log stays unknown");
        Check(AppCrashes.Parse("""{"Days":14,"Events":[{"App":"a.exe","Module":null,"Hang":true,"Time":"2026-09-20T10:00:00.0000000Z"}],"Notes":""}""").Events!.Single().Hang,
            "app crashes: the collector's JSON is read");

        LinkAdapter Wired(double bps, bool? duplex = true) => new("Ethernet", "Realtek PCIe GbE", "802.3", bps, duplex);
        LinkAdapter Wifi(double bps) => new("WLAN", "Intel Wi-Fi 6E", "Native 802.11", bps, false);
        CardStatus Link(params LinkAdapter[] a) => NetworkLink.Evaluate(new(a, "")).Single().Status;
        Check(Link(Wired(1e9)) == CardStatus.Good && Link(Wired(2.5e9)) == CardStatus.Good, "network link: gigabit and faster wired links look fine");
        Check(Link(Wired(100e6)) == CardStatus.Review && NetworkLink.Evaluate(new([Wired(100e6)], "")).Single().Body.Contains("cable"), "network link: a wired link at 100 Mbps points to the cable or port");
        Check(Link(Wired(10e6)) == CardStatus.Problem && Link(Wired(1e9, false)) == CardStatus.Review, "network link: 10 Mbps is a problem and half duplex is worth reviewing");
        Check(Link(Wifi(1.4e9)) == CardStatus.Info && Link(Wifi(24e6)) == CardStatus.Review && NetworkLink.Rate(1.4e9) == "1.4 Gbps" && NetworkLink.Rate(866.7e6) == "867 Mbps",
            "network link: Wi-Fi link rates are information, and never judged on duplex");
        Check(NetworkLink.Evaluate(new([], "")).Single().Status == CardStatus.Info && NetworkLink.Evaluate(new(null, "adapters")).Single().Status == CardStatus.Unknown,
            "network link: nothing connected is information, an unreadable list stays unknown");

        TimeSyncFacts Time(int? syncedDaysAgo = 2, int failures = 0, string type = "NTP", string start = "Manual", string notes = "") =>
            new(syncedDaysAgo is { } d ? Now.AddDays(-d) : null, "time.windows.com,0x9 (ntp.m|0x9|0.0.0.0:123->104.40.149.189:123)", failures, failures > 0 ? Now.AddDays(-1) : null, type, start, notes);
        Check(TimeSync.Evaluate(Time(), Now).Single() is { Status: CardStatus.Good } synced && synced.Body.Contains("from time.windows.com 2 days ago") && !synced.Evidence.Contains("104.40"),
            "clock sync: a recent sync names the server and when, without the server's IP address");
        Check(TimeSync.Evaluate(Time(null, failures: 4), Now).Single() is { Status: CardStatus.Review } blocked && blocked.Title == "Windows can't reach its time server" && blocked.Body.Contains("UDP port 123"),
            "clock sync: repeated failed syncs point to blocked time requests");
        Check(TimeSync.Evaluate(Time(type: "NoSync"), Now).Single().Title == "Automatic time is off" && TimeSync.Evaluate(Time(start: "Disabled"), Now).Single().Status == CardStatus.Review,
            "clock sync: automatic time turned off, or the time service disabled, is worth reviewing");
        Check(TimeSync.Evaluate(Time(null), Now).Single().Status == CardStatus.Info && TimeSync.Evaluate(Time(40), Now).Single().Body.Contains("last successful sync was"),
            "clock sync: no recent sync without failures is information, not a fault");
        Check(TimeSync.Evaluate(Time(null, notes: "time events"), Now).Single().Status == CardStatus.Unknown, "clock sync: unreadable records stay unknown");

        // The real collectors on this PC: they run as a standard user and their output parses.
        var probe = new WindowsDiagnosticProbe();
        string Read(string script) => probe.ReadAsync(script, 60, CancellationToken.None).GetAwaiter().GetResult();
        Check(AppCrashes.Evaluate(AppCrashes.Parse(Read(AppCrashes.Script)), DateTimeOffset.UtcNow).All(i => i.Status != CardStatus.Unknown), "live: app crash history is read");
        Check(NetworkLink.Evaluate(NetworkLink.Parse(Read(NetworkLink.Script))).All(i => i.Status != CardStatus.Unknown), "live: network adapter speeds are read");
        Check(TimeSync.Evaluate(TimeSync.Parse(Read(TimeSync.Script)), DateTimeOffset.UtcNow).Single().Status != CardStatus.Unknown, "live: clock sync history is read");
    }

    private static void Collectors()
    {
        foreach (var (name, script) in new[] { ("update", UpdateHealth.Script), ("battery-startup", BatteryStartup.Script), ("app-crashes", AppCrashes.Script),
            ("network-link", NetworkLink.Script), ("time-sync", TimeSync.Script) })
            Check(!System.Text.RegularExpressions.Regex.IsMatch(script, @"Set-ItemProperty|New-ItemProperty|Set-Service|Stop-Service|Start-Service|-RestoreHealth|/scannow|Remove-Item(?! -LiteralPath \$file)"),
                "collector only reads Windows state: " + name);
    }
}
