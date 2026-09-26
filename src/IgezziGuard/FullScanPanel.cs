using System.Security.Principal;
using System.Text;

namespace IgezziGuard;

public sealed class FullScanPanel : ToolPage
{
    private readonly CheckBox external = new() { Text = "Include network probes", AutoSize = true, AccessibleName = "Include optional external network probes", Margin = new Padding(6, 10, 14, 0) };
    private readonly HankiButton repairButton;
    private readonly HankiButton summaryButton;
    public event Action<string>? OpenRequested;
    private DiagnosticScan? latest;
    private string scanReport = "";
    private bool historySaved = true;
    // Repairs run from the latest scan: they stop further proposals from it and go into the customer report.
    private RepairReport? repairs;
    private readonly IEntitlements entitlements = EntitlementComposition.Current();
    private readonly DiagnosticHistory history = new(Path.Combine(SecurityPaths.Root, "diagnostic-history.json"));
    public FullScanPanel() : base("A local, read-only review of Windows, storage, devices, security and performance. Some checks require administrator access and may take several minutes. Unavailable checks stay unknown. No repairs, uploads or automatic elevation. Existing tools remain available individually.")
    {
        Button("Start full scan", StartScan);
        Bar.Controls.Add(external);
        repairButton = Button("Review repairs", ReviewRepairs);
        repairButton.Visible = false;
        summaryButton = Button("Back to scan results", ShowFindings);
        summaryButton.Visible = false;
        // Technician only; the licence can change while Hanki is open, so visibility follows it.
        var customerReport = Button("Customer report", () => {
            if (latest is null) { Output.Text = "Run a full scan first. To report on an earlier scan, open System actions → Saved scans."; return; }
            if (CustomerReportFlow.Create(this, latest, repairs) is { } status) Output.Text = status;
        });
        customerReport.Visible = entitlements.Allows(HankiCapability.CustomerReports);
        VisibleChanged += (_, _) => { if (Visible) customerReport.Visible = entitlements.Allows(HankiCapability.CustomerReports); };
        ShowSummary(new(Output.Text, CardStatus.Info, Localizer.T("Check your PC without changing settings"), [
            new(Localizer.T("Start with a full scan"), Localizer.T("Checks Windows, storage, devices and security. You review the findings before choosing any action.")),
            new(Localizer.T("Before you start"), Localizer.T("Some checks take several minutes or need administrator access. Unavailable checks are reported separately."))
        ]));
    }
    internal void Start() { if (!IsBusy) StartScan(); }
    internal void Preview(DiagnosticScan scan) { latest = scan; scanReport = Summary(scan); ShowFindings(); }
    private void ShowFindings() { if (latest is not null) Output.Text = scanReport; ShowFindings(false, false); }
    private void ShowFindings(bool showOther, bool showGaps)
    {
        summaryButton.Visible = false;
        repairButton.Visible = latest is not null && repairs is null && entitlements.Allows(HankiCapability.AutomaticRepair) && Context(false).IsAdministrator
            && RepairGuidance.Stale(latest, false, DateTimeOffset.UtcNow) is null
            && latest.Results.Any(r => FindingAnalysis.Recommend(r)?.RepairActionId is not null);
        if (latest is null) return;
        var scan = latest;
        var cards = new List<ResultCard>();
        if (!historySaved) cards.Add(new(Localizer.T("History could not be saved"), Localizer.T("Results remain available in this window. Your existing history was preserved."), CardStatus.Unknown));
        var ordered = FindingAnalysis.Rank(FindingAnalysis.Normalize(scan.Results)).SelectMany(g => g.Group.Sources).ToArray();
        ResultCard Card(DiagnosticResult r) => new(r.Title, r.Explanation, ScanPresentation.Status(r), Localizer.T("See next step"), () => ShowFinding(r));
        cards.AddRange(ordered.Where(ScanPresentation.NeedsAttention).Select(Card));
        if (scan.Cancelled || scan.CompletedModules < scan.PlannedModules) cards.Add(new(Localizer.T("Some checks are incomplete"),
            Localizer.Format("Checks processed: {0}/{1}. Run a new scan to check the remaining items.", scan.CompletedModules, scan.PlannedModules), CardStatus.Unknown));
        var gaps = ordered.Where(ScanPresentation.HasGap).ToArray();
        if (gaps.Length > 0) {
            cards.Add(new(Localizer.Format("Checks with missing evidence: {0}", gaps.Length), Localizer.T("These checks cannot establish that everything is OK."), CardStatus.Unknown,
                Localizer.T(showGaps ? "Hide incomplete checks" : "Review incomplete checks"), () => ShowFindings(showOther, !showGaps)));
            if (showGaps) cards.AddRange(gaps.Select(Card));
        }
        var other = ordered.Where(r => !ScanPresentation.NeedsAttention(r) && !ScanPresentation.HasGap(r)).ToArray();
        if (other.Length > 0) {
            cards.Add(new(Localizer.Format("Other completed checks: {0}", other.Length), Localizer.T("Healthy checks and information are kept here. No action is required just because a result is listed."), CardStatus.Info,
                Localizer.T(showOther ? "Hide other checks" : "Show other checks"), () => ShowFindings(!showOther, showGaps)));
            if (showOther) cards.AddRange(other.Select(Card));
        }
        if (cards.Count == 0) cards.Add(new(Localizer.T("No results yet"), Localizer.T("Run the scan again when you are ready."), CardStatus.Unknown));
        ShowSummary(new(Output.Text, ordered.Any(r => r.Severity == FindingSeverity.Critical) ? CardStatus.Problem : ordered.Any(ScanPresentation.NeedsAttention) ? CardStatus.Review : scan.Complete && ordered.Length > 0 ? CardStatus.Good : CardStatus.Unknown,
            ScanPresentation.Headline(scan), cards));
    }
    private void ShowFinding(DiagnosticResult finding)
    {
        summaryButton.Visible = true;
        Output.Text = FindingAnalysis.Describe(finding);
        var recommendation = FindingAnalysis.Recommend(finding);
        var route = ScanPresentation.Route(finding);
        ShowSummary(new(Output.Text, ScanPresentation.Status(finding), finding.Title, [
            new(Localizer.T("What we found"), finding.Explanation, ScanPresentation.Status(finding)),
            new(Localizer.T("Try this next"), recommendation?.ManualAction ?? Localizer.T("Review the coverage below. A missing result is not a diagnosis."), CardStatus.Info,
                route is null ? null : Localizer.T("Open recommended tool"), route is null ? null : () => OpenRequested?.Invoke(route)),
            new(Localizer.T("What this check covers"), finding.Coverage + (ScanPresentation.HasGap(finding) ? "\n" + Localizer.T("This check is incomplete.") : ""), ScanPresentation.HasGap(finding) ? CardStatus.Unknown : CardStatus.Info)
        ]));
    }
    private async void StartScan()
    {
        bool contact = external.Checked;
        if (contact && !Review("Include network probes? Gateway ICMP contacts your local network. " + WindowsDiagnosticCatalog.ProbeDisclosure + " Installed KMS clients may also query your organization DNS and contact the Windows-configured KMS host. These endpoints and your DNS resolver can see your source IP. No report is uploaded. You can run without these checks by clearing Include network probes.")) return;
        latest = null; repairs = null; historySaved = true; ShowFindings();
        var context = Context(contact);
        var progress = new Progress<ScanProgressUpdate>(p => { if (IsBusy) Output.Text = $"{p.CompletedModules}/{p.TotalModules} checks finished\r\n{p.Activity}\r\n\r\nUnavailable checks remain unknown. Cancel preserves results from completed checks."; });
        await Run(async token => {
            latest = await new DiagnosticOrchestrator(WindowsDiagnosticCatalog.Create(includeExternal: contact)).ScanAsync(context, progress, token);
            string summary = Summary(latest);
            try { history.Add(latest); } catch { historySaved = false; summary += "\r\nHistory could not be saved. Current results remain available; existing history was preserved."; }
            scanReport = summary;
            return summary;
        });
        ShowFindings();
    }
    private async void ReviewRepairs()
    {
        if (latest is null) { Output.Text = "Run a full scan first. Manual tools remain available in each module."; return; }
        if (!entitlements.Allows(HankiCapability.AutomaticRepair)) {
            Output.Text = "Automatic repair is part of Hanki Pro. See Help → Hanki Pro.\r\n\r\nYour scan, findings and manual guidance stay free. Select a finding to see its manual next steps.";
            return;
        }
        if (RepairGuidance.Stale(latest, repairs is not null, DateTimeOffset.UtcNow) is { } stale) { Output.Text = stale; return; }
        var ids = latest.Results.Select(FindingAnalysis.Recommend).Where(r => r?.RepairActionId is not null).Select(r => r!.RepairActionId).ToHashSet();
        var actions = WindowsServicingRepair.Catalog().Where(a => ids.Contains(a.Definition.Id)).ToArray();
        if (actions.Length == 0) { Output.Text = RepairGuidance.NoProposals(latest); return; }
        var scan = latest; bool administrator = Context(false).IsAdministrator;
        if (RepairGuidance.NoneAvailable(actions.Select(a => a.Definition).ToList(), administrator) is { } blocked) { Output.Text = blocked; return; }
        if (RepairReviewDialog.Show(this, scan, actions, administrator) is not { } approved) return;
        var audit = new RepairAudit(Path.Combine(SecurityPaths.Root, "repair-audit.json"));
        var workflow = new RepairWorkflow(WindowsServicingRepair.Catalog(), WindowsDiagnosticCatalog.Create(includeExternal: true), new WindowsRepairEnvironment(), new WindowsRestoreProtection(), audit, entitlements);
        var progress = new Progress<string>(text => { if (IsBusy) Output.Text = text; });
        RepairReport? report = null;
        await Run(async token => RepairGuidance.Results(report = await workflow.RunAsync(scan, approved, Context(approved.NetworkApproved), progress, token)));
        // Repair evidence is historical; a new scan is required for another proposal.
        if (report is not null && latest == scan) { repairs = report; repairButton.Visible = false; summaryButton.Visible = true; }
    }
    internal static DiagnosticContext Context(bool external)
    {
        bool administrator = false;
        if (OperatingSystem.IsWindows()) {
            using var identity = WindowsIdentity.GetCurrent(); administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        return new(OperatingSystem.IsWindows(), administrator, external);
    }
    internal static string Summary(DiagnosticScan scan)
    {
        var text = new StringBuilder(scan.Cancelled ? "Scan cancelled — completed evidence retained.\r\n" : scan.Complete ? "Scan finished.\r\n" : "Scan finished with gaps — review unavailable or failed checks.\r\n");
        text.AppendLine($"{scan.CompletedModules}/{scan.PlannedModules} checks finished · {scan.Started.ToLocalTime():g} — {scan.Ended.ToLocalTime():g}");
        foreach (var severity in Enum.GetValues<FindingSeverity>()) text.AppendLine($"{severity}: {scan.Results.Count(r => r.Severity == severity)}");
        text.AppendLine("\r\nSelect a finding for explanation and technical evidence. No changes made. These checks do not prove overall PC health.");
        foreach (var r in scan.Results) text.AppendLine($"\r\n{r.Title} · {r.Severity} · {r.Outcome}\r\n{r.Explanation}");
        return text.ToString();
    }
}
