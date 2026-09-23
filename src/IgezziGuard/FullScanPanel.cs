using System.Security.Principal;
using System.Text;

namespace IgezziGuard;

public sealed class FullScanPanel : ToolPage
{
    private readonly CheckBox external = new() { Text = "Include network probes", AutoSize = true, AccessibleName = "Include optional external network probes" };
    private readonly ListView findings = new() { Dock = DockStyle.Top, Height = 230, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, AccessibleName = "Diagnostic findings" };
    private DiagnosticScan? latest;
    private readonly IEntitlements entitlements = EntitlementComposition.Current();
    private readonly DiagnosticHistory history = new(Path.Combine(SecurityPaths.Root, "diagnostic-history.json"));
    public FullScanPanel() : base("A local, read-only review of Windows, storage, devices, security and performance. Some checks require administrator access and may take several minutes. Unavailable checks stay unknown. No repairs, uploads or automatic elevation. Existing tools remain available individually.")
    {
        Button("Start full scan", StartScan);
        Bar.Controls.Add(external);
        Button("Review automatic repairs", ReviewRepairs);
        Button("Show scan summary", () => { if (latest is not null) Output.Text = Summary(latest); });
        findings.Columns.Add("Finding", 285); findings.Columns.Add("Severity", 110); findings.Columns.Add("Collection", 110);
        findings.SelectedIndexChanged += (_, _) => {
            if (IsBusy || findings.SelectedItems.Count != 1 || findings.SelectedItems[0].Tag is not DiagnosticResult r) return;
            Output.Text = FindingAnalysis.Describe(r);
        };
        Controls.Add(findings); Controls.SetChildIndex(findings, 1);
    }
    private async void StartScan()
    {
        bool contact = external.Checked;
        if (contact && !Review("Include gateway ICMP, example.com DNS lookup and TCP connection to example.com:443? Installed KMS clients may also query your organization DNS and contact the Windows-configured KMS host. These endpoints and your DNS resolver can see your source IP. No report is uploaded. You can run without these checks by clearing Include network probes.")) return;
        latest = null; findings.Items.Clear();
        var context = Context(contact);
        var progress = new Progress<ScanProgressUpdate>(p => { if (IsBusy) Output.Text = $"{p.CompletedModules}/{p.TotalModules} checks finished\r\n{p.Activity}\r\n\r\nUnavailable checks remain unknown. Cancel preserves results from completed checks."; });
        await Run(async token => {
            latest = await new DiagnosticOrchestrator(WindowsDiagnosticCatalog.Create(includeExternal: contact)).ScanAsync(context, progress, token);
            string summary = Summary(latest);
            try { history.Add(latest); } catch { summary += "\r\nHistory could not be saved. Current results remain available; existing history was preserved."; }
            return summary;
        });
        if (latest is not null) foreach (var r in FindingAnalysis.Rank(FindingAnalysis.Normalize(latest.Results)).SelectMany(g => g.Group.Sources)) findings.Items.Add(new ListViewItem([r.Title, r.Severity.ToString(), r.Outcome.ToString()]) { Tag = r });
    }
    private async void ReviewRepairs()
    {
        if (latest is null) { Output.Text = "Run a full scan first. Manual tools remain available in each module."; return; }
        if (!entitlements.Allows(HankiCapability.AutomaticRepair)) {
            Output.Text = "Automatic repair is an additive Hanki Pro capability. Production licensing is not connected in this candidate.\r\n\r\nYour scan, findings, manual guidance and existing Community tools remain available without an account. Select a finding to review its manual next steps.";
            return;
        }
        var ids = latest.Results.Select(FindingAnalysis.Recommend).Where(r => r?.RepairActionId is not null).Select(r => r!.RepairActionId).ToHashSet();
        var actions = WindowsServicingRepair.Catalog().Where(a => ids.Contains(a.Definition.Id)).ToArray();
        if (actions.Length == 0) { Output.Text = "No supported automatic repairs follow from this scan. Review manual guidance; unknown states do not justify automatic changes."; return; }
        using var dialog = new Form { Text = "Review proposed changes", Size = new Size(850,650), MinimumSize = new Size(650,500), StartPosition = FormStartPosition.CenterParent, Padding = new Padding(16) };
        var choices = new CheckedListBox { Dock=DockStyle.Top, Height=100, CheckOnClick=true, AccessibleName="Select repairs to approve" };
        foreach(var action in actions) choices.Items.Add(action.Definition.Title, false);
        var description = new TextBox { Dock=DockStyle.Fill, Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, Text=string.Join("\r\n\r\n",actions.Select(a=>$"{a.Definition.Title} — {a.Definition.Risk} risk\r\n{a.Definition.ChangeDescription}\r\nUndo: {a.Definition.RollbackInformation}")) };
        var settings = new FlowLayoutPanel {Dock=DockStyle.Bottom,AutoSize=true,FlowDirection=FlowDirection.TopDown};
        var noRestore = new CheckBox {AutoSize=true,Text="I accept proceeding if an optional restore point cannot be created"};
        var network = new CheckBox {AutoSize=true,Text="Allow configured Windows repair sources to use the network"};
        var approve = new HankiButton {Text="Approve selected changes",AutoSize=true,DialogResult=DialogResult.OK};
        var cancel = new HankiButton {Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};
        settings.Controls.AddRange([noRestore,network,approve,cancel]);
        dialog.Controls.Add(description);dialog.Controls.Add(choices);dialog.Controls.Add(settings);dialog.CancelButton=cancel;
        HankiTheme.Apply(dialog);
        if(dialog.ShowDialog(this)!=DialogResult.OK || choices.CheckedIndices.Count==0)return;
        var scan=latest;
        var approved=new RepairApproval(scan.Id,choices.CheckedIndices.Cast<int>().Select(i=>actions[i].Definition.Id).ToHashSet(),noRestore.Checked,network.Checked);
        var audit=new RepairAudit(Path.Combine(SecurityPaths.Root,"repair-audit.json"));
        var workflow=new RepairWorkflow(WindowsServicingRepair.Catalog(),WindowsDiagnosticCatalog.Create(),new WindowsRepairEnvironment(),new WindowsRestoreProtection(),audit,entitlements);
        var progress=new Progress<string>(text=>{if(IsBusy)Output.Text=text;});
        await Run(async token=>RepairReportText.Format(await workflow.RunAsync(scan,approved,Context(false),progress,token)));
        // Repair evidence is historical; a new scan is required for another proposal.
        latest=null;
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
        text.AppendLine($"{scan.CompletedModules}/{scan.PlannedModules} checks finished · {scan.Started:g} — {scan.Ended:g}");
        foreach (var severity in Enum.GetValues<FindingSeverity>()) text.AppendLine($"{severity}: {scan.Results.Count(r => r.Severity == severity)}");
        text.AppendLine("\r\nSelect a finding for explanation and technical evidence. No changes made. These checks do not prove overall PC health.");
        foreach (var r in scan.Results) text.AppendLine($"\r\n{r.Title} · {r.Severity} · {r.Outcome}\r\n{r.Explanation}");
        return text.ToString();
    }
}
