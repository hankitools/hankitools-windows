using IgezziGuard;

// Pro and Technician: scheduled-check status, the customer report and the wording around automatic repairs.
internal static class ProChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    internal static void Run() { Schedule(); CustomerReport(); Repairs(); }

    private static DiagnosticResult Result(string module, string finding, FindingSeverity severity, string title, string explanation = "",
        CollectionOutcome outcome = CollectionOutcome.Completed, string evidence = "") =>
        new(module, finding, DiagnosticCategory.Windows, outcome, severity, title, explanation, Now.AddMinutes(-5), Now.AddMinutes(-4), evidence);
    private static DiagnosticScan Scan(params DiagnosticResult[] results) => new(Guid.NewGuid(), Now.AddMinutes(-6), Now.AddMinutes(-3), results.Length, results.Length, false, results);
    private static RepairAttempt Attempt(string id, RepairState state, VerificationState verification, string explanation = "Completed.") =>
        new(id, Now, Now, state, explanation, new(RestoreState.Created, "Restore point created."), verification, [], []);
    private static RepairDefinition Definition(string id, bool network, bool administrator = true) =>
        new(id, id, "Changes something.", RepairRisk.Moderate, administrator, network, true, false, [], id, "No undo.");

    private static void Schedule()
    {
        ScheduledHealthChecks.ScheduleStatus Status(string state = "Ready", DateTimeOffset? last = null, long? result = null) => new(true, state, last, result, Now.AddDays(1));
        Check(ScheduledHealthChecks.Describe(new(false, null, null, null, null)) == "No scheduled check.", "schedule status: no task");
        Check(ScheduledHealthChecks.Describe(Status("Disabled")).Contains("turned off"), "schedule status: disabled in Task Scheduler");
        Check(ScheduledHealthChecks.Describe(Status()).Contains("hasn't run yet"), "schedule status: not run yet");
        Check(ScheduledHealthChecks.Describe(Status(last: Now, result: 0)).EndsWith(" finished."), "schedule status: last run finished");
        Check(ScheduledHealthChecks.Describe(Status(last: Now, result: 1)).Contains("finished with gaps"), "schedule status: exit 1 means gaps, not failure");
        Check(ScheduledHealthChecks.Describe(Status("Running", Now, 0x41301)).Contains("is running"), "schedule status: running");
        Check(ScheduledHealthChecks.Describe(Status(last: Now, result: 2147942405)).Contains("0x80070005"), "schedule status: failure shows the Windows code");
    }

    private static void CustomerReport()
    {
        var technician = new TechnicianSessions(new EditionEntitlements(HankiEdition.Technician));
        var scan = Scan(
            Result("sfc", "integrity", FindingSeverity.Warning, "Protected Windows files", "SFC found files it could not verify."),
            Result("services", "service-a", FindingSeverity.Warning, "Automatic service stopped", "A service set to start automatically isn't running."),
            Result("services", "service-b", FindingSeverity.Warning, "Automatic service stopped", "A service set to start automatically isn't running."),
            Result("storage", "space", FindingSeverity.Healthy, "Disk <script>alert(1)</script> space", "Plenty of free space.", evidence: @"C:\Users\Matti\Documents password=hunter2"),
            Result("dism", "store", FindingSeverity.Unknown, "Windows component store", "Needs administrator rights.", CollectionOutcome.Unavailable));
        var session = technician.Begin("JOB-1042", "Lenovo laptop", scan, new(scan.Id, [Attempt("sfc-repair", RepairState.Executed, VerificationState.Fixed)]));
        var html = technician.ExportHtml(session, new("Korjaamo Oy", "040 123 4567"), false, "0.18.0");
        Check(html.Contains("1 thing is worth reviewing") && html.Contains("We checked 3 areas"), "customer report headline counts grouped open findings and the areas actually checked");
        Check(html.Contains("Automatic service stopped <span class=\"times\">×2</span>"), "identical observations are listed once with a count");
        Check(html.Contains("Protected Windows files repair (SFC)") && html.Contains("Done, and the check now passes.") && html.Contains("1 fixed"), "repairs are described in plain words");
        Check(html.Contains("Found:</span> SFC found files it could not verify.") && !html.Contains("<b>Protected Windows files</b>"), "a repaired finding moves under its repair instead of staying open");
        var unchanged = technician.Begin("", "", scan, new(scan.Id, [Attempt("sfc-repair", RepairState.Executed, VerificationState.Unchanged)]));
        var unchangedHtml = technician.ExportHtml(unchanged, new("", ""), false, "0.18.0");
        Check(unchangedHtml.Contains("2 things are worth reviewing") && unchangedHtml.Contains("Done, but the check shows no change.") && !unchangedHtml.Contains("Found:"), "a repair that didn't fix the problem leaves the finding open");
        Check(unchangedHtml.Contains("Next step:") && unchangedHtml.Contains("Review Windows servicing evidence"), "items worth reviewing carry the manual next step");
        Check(html.Contains("<h3>Not checked</h3>") && html.Contains("Needs administrator rights."), "unavailable checks are listed as not checked, with the reason");
        Check(!html.Contains("<script>alert") && html.Contains("&lt;script&gt;"), "customer report escapes text from the PC");
        Check(html.Contains("Korjaamo Oy") && html.Contains("JOB-1042") && html.Contains("Hanki Tools 0.18.0"), "customer report shows business, job and version");
        Check(!html.Contains("Technical details") && !html.Contains("Matti"), "technical details are left out unless asked for");
        var technical = technician.ExportHtml(session, new("Korjaamo Oy", ""), true, "0.18.0");
        Check(technical.Contains("Technical details") && !technical.Contains("Matti") && !technical.Contains("hunter2") && technical.Contains("[USER]"), "technical details mask user names and secrets");
        Check(!technical.Contains("class=\"contact\""), "an empty contact line is left out");

        var clean = technician.Begin("", "", Scan(Result("storage", "space", FindingSeverity.Healthy, "Disk space", "Plenty of free space.")));
        var cleanHtml = technician.ExportHtml(clean, new("", ""), false, "0.18.0");
        Check(cleanHtml.Contains("Nothing needs attention") && cleanHtml.Contains("<h1>PC check report</h1>") && !cleanHtml.Contains("Unlabelled"), "unlabelled report reads naturally");
        var critical = technician.Begin("", "", Scan(Result("storage", "smart", FindingSeverity.Critical, "Drive health", "The drive reports failure.")));
        Check(technician.ExportHtml(critical, new("", ""), false, "0.18.0").Contains("1 thing needs attention"), "critical findings lead the summary");

        var saved = technician.Begin("", "", DiagnosticPrivacy.Minimize(scan));
        var savedHtml = technician.ExportHtml(saved, new("", ""), false, "0.18.0");
        Check(savedHtml.Contains("saved history") && !savedHtml.Contains("Local result retained"), "report from saved history explains the limited detail");

        bool refused = false;
        try { new TechnicianSessions(new EditionEntitlements(HankiEdition.Pro)).ExportHtml(session, new("", ""), false, "0.18.0"); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "customer report needs the Technician edition");
        bool mismatch = false;
        try { technician.ExportHtml(session with { Repairs = new(Guid.NewGuid(), []) }, new("", ""), false, "0.18.0"); } catch (ArgumentException) { mismatch = true; }
        Check(mismatch, "customer report refuses repairs from another scan");

        Check(TechnicianReport.FileName("JOB:10/42?", Now) == "PC check JOB1042 2026-09-23.html", "report file name drops characters Windows doesn't allow");
        Check(TechnicianReport.FileName("Unlabelled", Now) == "PC check 2026-09-23.html", "unlabelled report file name");
    }

    private static void Repairs()
    {
        var notChecked = RepairGuidance.NoProposals(Scan(
            Result("dism", "store", FindingSeverity.Unknown, "Windows component store", "", CollectionOutcome.Unavailable),
            Result("storage", "space", FindingSeverity.Healthy, "Disk space")));
        Check(notChecked.Contains("run it as administrator") && notChecked.Contains("Include network probes"), "no repairs: explains what wasn't checked and how to include it");
        var checkedAll = RepairGuidance.NoProposals(Scan(Result("dism", "store", FindingSeverity.Healthy, "Windows component store"),
            Result("sfc", "integrity", FindingSeverity.Healthy, "Protected Windows files"), Result("network-probes", "dns", FindingSeverity.Healthy, "DNS")));
        Check(checkedAll.Contains("None of these showed up") && !checkedAll.Contains("administrator"), "no repairs: a full check says nothing was found");

        var sfcScan = Scan(Result("sfc", "integrity", FindingSeverity.Warning, "Protected Windows files"));
        Check(RepairGuidance.Reason(sfcScan, "sfc-repair").StartsWith("Protected Windows files: "), "proposal names the finding behind it");

        var dism = Definition("dism-restore", network: true); var sfc = Definition("sfc-repair", network: false);
        Check(RepairGuidance.ApprovalProblem([], true) is { } none && none.Contains("Tick"), "approval needs a selection");
        Check(RepairGuidance.ApprovalProblem([dism, sfc], false) is { } net && net.Contains("Allow network use") && net.Contains("component store"), "approval names the repair that needs the network");
        Check(RepairGuidance.ApprovalProblem([dism, sfc], true) is null && RepairGuidance.ApprovalProblem([sfc], false) is null, "approval passes when the network isn't needed or is allowed");
        Check(RepairGuidance.Blocker(sfc, false) is not null && RepairGuidance.Blocker(sfc, true) is null && RepairGuidance.Blocker(Definition("x", false, false), false) is null, "administrator-only repairs are marked when Hanki isn't elevated");

        Check(RepairGuidance.NoneAvailable([Definition("dns-cache-flush", true)], false) is { } admin && admin.Contains("Windows DNS cache refresh") && admin.Contains("Run as administrator")
            && RepairGuidance.NoneAvailable([Definition("dns-cache-flush", true)], true) is null, "without administrator rights the page explains instead of offering greyed-out repairs");
        Check(RepairGuidance.Stale(sfcScan, true, Now) is { } ran && ran.Contains("already ran"), "one set of repairs per scan");
        Check(RepairGuidance.Stale(sfcScan, false, Now.AddMinutes(40)) is { } old && old.Contains("30 minutes"), "repairs need a recent scan");
        Check(RepairGuidance.Stale(sfcScan, false, Now) is null, "a fresh scan can lead to repairs");

        var restart = RepairGuidance.Results(new(sfcScan.Id, [Attempt("dism-restore", RepairState.Executed, VerificationState.RequiresRestart)]));
        Check(restart.StartsWith("Repair results") && restart.Contains("Restart the PC to finish") && restart.Contains("Details"), "results say when a restart is needed and keep the details");
        var failed = RepairGuidance.Results(new(sfcScan.Id, [Attempt("sfc-repair", RepairState.Failed, VerificationState.NotRun)]));
        Check(failed.Contains("Tried, but it didn't complete.") && failed.Contains("partly changed"), "a failed repair warns about partial changes");
        Check(RepairGuidance.Outcome(Attempt("dns-cache-flush", RepairState.Blocked, VerificationState.NotRun, @"Blocked for C:\Users\Matti")) is { } blocked
            && blocked.StartsWith("Not run: ") && !blocked.Contains("Matti"), "a blocked repair gives the reason with user names masked");
    }
}
