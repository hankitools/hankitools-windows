using IgezziGuard;

// Windows Update and Battery & startup: honest, plain-language reading of Windows' own records.
internal static class HealthChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    internal static void Run() { Updates(); BatteryAndStartup(); Collectors(); }

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

    private static void Collectors()
    {
        foreach (var (name, script) in new[] { ("update", UpdateHealth.Script), ("battery-startup", BatteryStartup.Script) })
            Check(!System.Text.RegularExpressions.Regex.IsMatch(script, @"Set-ItemProperty|New-ItemProperty|Set-Service|Stop-Service|Start-Service|-RestoreHealth|/scannow|Remove-Item(?! -LiteralPath \$file)"),
                "collector only reads Windows state: " + name);
    }
}
