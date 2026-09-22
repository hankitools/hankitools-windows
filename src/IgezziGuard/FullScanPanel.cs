using System.Security.Principal;
using System.Text;

namespace IgezziGuard;

public sealed class FullScanPanel : ToolPage
{
    private readonly CheckBox external = new() { Text = "Include network probes", AutoSize = true, AccessibleName = "Include optional external network probes" };
    private readonly ListView findings = new() { Dock = DockStyle.Top, Height = 230, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, AccessibleName = "Diagnostic findings" };
    private DiagnosticScan? latest;
    public FullScanPanel() : base("A local, read-only review of Windows, storage, devices, security and performance. Some checks require administrator access and may take several minutes. Unavailable checks stay unknown. No repairs, uploads or automatic elevation. Existing tools remain available individually.")
    {
        Button("Start full scan", StartScan);
        Bar.Controls.Add(external);
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
        if (contact && !Review("Include gateway ICMP, example.com DNS lookup and TCP connection to example.com:443? These endpoints and your DNS resolver can see your source IP. No report is uploaded. You can run without these checks by clearing Include network probes.")) return;
        latest = null; findings.Items.Clear();
        var context = Context(contact);
        var progress = new Progress<ScanProgressUpdate>(p => { if (IsBusy) Output.Text = $"{p.CompletedModules}/{p.TotalModules} checks finished\r\n{p.Activity}\r\n\r\nUnavailable checks remain unknown. Cancel preserves results from completed checks."; });
        await Run(async token => {
            latest = await new DiagnosticOrchestrator(WindowsDiagnosticCatalog.Create(includeExternal: contact)).ScanAsync(context, progress, token);
            return Summary(latest);
        });
        if (latest is not null) foreach (var r in FindingAnalysis.Rank(FindingAnalysis.Normalize(latest.Results)).SelectMany(g => g.Group.Sources)) findings.Items.Add(new ListViewItem([r.Title, r.Severity.ToString(), r.Outcome.ToString()]) { Tag = r });
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
