using System.Security.Principal;
using System.Text;

namespace IgezziGuard;

public sealed class FullScanPanel : ToolPage
{
    private readonly CheckBox external = new() { Text = "Include network probes", AutoSize = true, AccessibleName = "Include optional external network probes", Margin = new Padding(6, 10, 14, 0) };
    private readonly ListBox findings = new() { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false, BorderStyle = BorderStyle.None, AccessibleName = "Diagnostic findings" };
    private readonly RoundedPanel results = new() { Dock = DockStyle.Top, Height = 320, Padding = new Padding(6, 4, 6, 8), Visible = false };
    private readonly Panel counts = new() { Dock = DockStyle.Top, Height = 40, Tag = "card" };
    private readonly Panel resultsGap = new() { Dock = DockStyle.Top, Height = 14, Visible = false };
    private readonly Font pillFont = new("Segoe UI Semibold", 8.25f), metaFont = new("Segoe UI", 9f);
    private DiagnosticScan? latest;
    private readonly IEntitlements entitlements = EntitlementComposition.Current();
    private readonly DiagnosticHistory history = new(Path.Combine(SecurityPaths.Root, "diagnostic-history.json"));
    public FullScanPanel() : base("A local, read-only review of Windows, storage, devices, security and performance. Some checks require administrator access and may take several minutes. Unavailable checks stay unknown. No repairs, uploads or automatic elevation. Existing tools remain available individually.")
    {
        Button("Start full scan", StartScan);
        Bar.Controls.Add(external);
        Button("Review automatic repairs", ReviewRepairs);
        Button("Show scan summary", () => { if (latest is not null) Output.Text = Summary(latest); });
        findings.SelectedIndexChanged += (_, _) => {
            if (IsBusy || findings.SelectedItem is not DiagnosticResult r) return;
            Output.Text = FindingAnalysis.Describe(r);
        };
        findings.DrawItem += DrawFinding;
        findings.HandleCreated += (_, _) => findings.ItemHeight = (int)(42 * findings.DeviceDpi / 96f);
        counts.Paint += (_, e) => {
            if (latest is null) return;
            float s = counts.DeviceDpi / 96f;
            string total = $"{latest.Results.Count} results";
            TextRenderer.DrawText(e.Graphics, total, pillFont, new Rectangle((int)(12 * s), 0, counts.Width, counts.Height), SystemInformation.HighContrast ? SystemColors.ControlText : HankiTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            int left = (int)(12 * s) + TextRenderer.MeasureText(total, pillFont).Width + (int)(18 * s);
            StatusChips.Draw(e.Graphics, new Rectangle(left, 0, counts.Width - left, counts.Height), StatusChips.Count(latest.Results), metaFont, s);
        };
        results.Controls.Add(findings); results.Controls.Add(counts);
        var administrator = Context(false).IsAdministrator;
        var adminHint = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 30, Tag = "intro", Visible = !administrator,
            Text = "DISM and SFC checks need administrator rights. To include them, close Hanki and run it as administrator." };
        Controls.Add(results); Controls.SetChildIndex(results, 1);
        Controls.Add(resultsGap); Controls.SetChildIndex(resultsGap, 1);
        Controls.Add(adminHint); Controls.SetChildIndex(adminHint, 3);
    }
    protected override void Dispose(bool disposing) { if (disposing) { pillFont.Dispose(); metaFont.Dispose(); } base.Dispose(disposing); }
    internal void Start() { if (!IsBusy) StartScan(); }
    private void DrawFinding(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || findings.Items[e.Index] is not DiagnosticResult r) return;
        var g = e.Graphics; float s = findings.DeviceDpi / 96f;
        bool hc = SystemInformation.HighContrast, selected = (e.State & DrawItemState.Selected) != 0;
        var text = hc ? (selected ? SystemColors.HighlightText : SystemColors.WindowText) : HankiTheme.Text;
        var muted = hc ? text : HankiTheme.Muted;
        using (var bg = new SolidBrush(hc ? (selected ? SystemColors.Highlight : SystemColors.Window) : selected ? HankiTheme.Raised : HankiTheme.Surface)) g.FillRectangle(bg, e.Bounds);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (selected && !hc) { using var bar = new SolidBrush(HankiTheme.Accent); g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y + 8 * s, 3 * s, e.Bounds.Height - 16 * s); }
        var color = HankiTheme.SeverityColor(r.Severity, r.Outcome);
        var pill = new RectangleF(e.Bounds.X + 14 * s, e.Bounds.Y + (e.Bounds.Height - 22 * s) / 2, 92 * s, 22 * s);
        using (var path = HankiButton.Rounded(pill, 11 * s)) {
            using var fill = new SolidBrush(hc ? SystemColors.Window : Color.FromArgb(38, color)); g.FillPath(fill, path);
            if (hc) { using var edge = new Pen(text); g.DrawPath(edge, path); }
        }
        TextRenderer.DrawText(g, HankiTheme.SeverityLabel(r.Severity, r.Outcome), pillFont, Rectangle.Round(pill), hc ? text : color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        string category = r.Category.ToString();
        int categoryWidth = TextRenderer.MeasureText(category, metaFont).Width;
        int titleLeft = (int)(pill.Right + 14 * s), right = e.Bounds.Right - (int)(14 * s);
        TextRenderer.DrawText(g, category, metaFont, new Rectangle(right - categoryWidth, e.Bounds.Y, categoryWidth, e.Bounds.Height), muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, DisplayTitle(r), findings.Font, new Rectangle(titleLeft, e.Bounds.Y, right - categoryWidth - titleLeft - (int)(12 * s), e.Bounds.Height), text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (!hc && e.Index < findings.Items.Count - 1) { using var line = new Pen(HankiTheme.Border); g.DrawLine(line, e.Bounds.X + 14 * s, e.Bounds.Bottom - 1, e.Bounds.Right - 14 * s, e.Bounds.Bottom - 1); }
        if ((e.State & DrawItemState.Focus) != 0 && findings.Focused && ShowFocusCues) {
            using var ring = new Pen(hc ? text : HankiTheme.Accent, 2 * s); g.DrawRectangle(ring, e.Bounds.X + s, e.Bounds.Y + s, e.Bounds.Width - 2 * s, e.Bounds.Height - 2 * s);
        }
    }
    // Several findings share a module title (for example two services); the finding id tells them apart.
    private string DisplayTitle(DiagnosticResult r) =>
        latest is not null && latest.Results.Count(x => x.Title == r.Title) > 1 && r.FindingId != "collection"
            ? r.Title + " · " + r.FindingId.Replace("service-", "", StringComparison.Ordinal) : r.Title;
    private void ShowFindings()
    {
        findings.BeginUpdate(); findings.Items.Clear();
        if (latest is not null) foreach (var r in FindingAnalysis.Rank(FindingAnalysis.Normalize(latest.Results)).SelectMany(g => g.Group.Sources)) findings.Items.Add(r);
        findings.EndUpdate();
        bool any = findings.Items.Count > 0;
        results.Visible = resultsGap.Visible = any;
        // The strip is painted, so screen readers get the same counts as text.
        counts.AccessibleName = latest is null ? "" : $"{latest.Results.Count} results: " + string.Join(", ", StatusChips.Count(latest.Results).Select(c => c.Text));
        FitResults(); counts.Invalidate();
    }
    // Findings get at most half the page so the selected result's explanation stays readable.
    private void FitResults()
    {
        if (!results.Visible) return;
        int wanted = counts.Height + findings.ItemHeight * findings.Items.Count + results.Padding.Vertical + 4;
        results.Height = Math.Max(counts.Height + findings.ItemHeight * 2, Math.Min(wanted, ClientSize.Height / 2));
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); FitResults(); }
    private async void StartScan()
    {
        bool contact = external.Checked;
        if (contact && !Review("Include gateway ICMP, example.com DNS lookup and TCP connection to example.com:443? Installed KMS clients may also query your organization DNS and contact the Windows-configured KMS host. These endpoints and your DNS resolver can see your source IP. No report is uploaded. You can run without these checks by clearing Include network probes.")) return;
        latest = null; ShowFindings();
        var context = Context(contact);
        var progress = new Progress<ScanProgressUpdate>(p => { if (IsBusy) Output.Text = $"{p.CompletedModules}/{p.TotalModules} checks finished\r\n{p.Activity}\r\n\r\nUnavailable checks remain unknown. Cancel preserves results from completed checks."; });
        await Run(async token => {
            latest = await new DiagnosticOrchestrator(WindowsDiagnosticCatalog.Create(includeExternal: contact)).ScanAsync(context, progress, token);
            string summary = Summary(latest);
            try { history.Add(latest); } catch { summary += "\r\nHistory could not be saved. Current results remain available; existing history was preserved."; }
            return summary;
        });
        ShowFindings();
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
        var description = new TextBox { Dock=DockStyle.Fill, Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, Text=string.Join("\r\n\r\n",actions.Select(a=>$"{a.Definition.Title} — {a.Definition.Risk} risk · Restart: {a.Definition.Restart}\r\n{a.Definition.ChangeDescription}\r\nUndo: {a.Definition.RollbackInformation}")) };
        var settings = new FlowLayoutPanel {Dock=DockStyle.Bottom,AutoSize=true,FlowDirection=FlowDirection.TopDown};
        var noRestore = new CheckBox {AutoSize=true,Text="I accept proceeding if an optional restore point cannot be created"};
        var network = new CheckBox {AutoSize=true,Text="Allow disclosed verification probes and configured Windows repair sources to use the network"};
        var approve = new HankiButton {Text="Approve selected changes",AutoSize=true,DialogResult=DialogResult.OK};
        var cancel = new HankiButton {Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};
        settings.Controls.AddRange([noRestore,network,approve,cancel]);
        dialog.Controls.Add(description);dialog.Controls.Add(choices);dialog.Controls.Add(settings);dialog.CancelButton=cancel;
        HankiTheme.Apply(dialog);
        if(dialog.ShowDialog(this)!=DialogResult.OK || choices.CheckedIndices.Count==0)return;
        var scan=latest;
        var approved=new RepairApproval(scan.Id,choices.CheckedIndices.Cast<int>().Select(i=>actions[i].Definition.Id).ToHashSet(),noRestore.Checked,network.Checked);
        var audit=new RepairAudit(Path.Combine(SecurityPaths.Root,"repair-audit.json"));
        var workflow=new RepairWorkflow(WindowsServicingRepair.Catalog(),WindowsDiagnosticCatalog.Create(includeExternal: true),new WindowsRepairEnvironment(),new WindowsRestoreProtection(),audit,entitlements);
        var progress=new Progress<string>(text=>{if(IsBusy)Output.Text=text;});
        await Run(async token=>RepairReportText.Format(await workflow.RunAsync(scan,approved,Context(approved.NetworkApproved),progress,token)));
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
        text.AppendLine($"{scan.CompletedModules}/{scan.PlannedModules} checks finished · {scan.Started.ToLocalTime():g} — {scan.Ended.ToLocalTime():g}");
        foreach (var severity in Enum.GetValues<FindingSeverity>()) text.AppendLine($"{severity}: {scan.Results.Count(r => r.Severity == severity)}");
        text.AppendLine("\r\nSelect a finding for explanation and technical evidence. No changes made. These checks do not prove overall PC health.");
        foreach (var r in scan.Results) text.AppendLine($"\r\n{r.Title} · {r.Severity} · {r.Outcome}\r\n{r.Explanation}");
        return text.ToString();
    }
}
