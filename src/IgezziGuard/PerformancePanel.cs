namespace IgezziGuard;

public sealed class PerformancePanel : UserControl
{
    private readonly HankiButton refresh = new() { Text = "Take / refresh snapshot", Primary = true, AutoSize = true };
    private readonly TextBox report = TextArea();
    private CancellationTokenSource? pending;
    public bool IsBusy => pending is not null;
    public event Action<string>? PrepareRequested;
    public void Cancel() => pending?.Cancel();
    public PerformancePanel()
    {
        Dock = DockStyle.Fill;
        var overview = new Panel { Dock = DockStyle.Fill };
        var results = new ResultCardsView(report); overview.Controls.Add(results);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        bar.Controls.Add(refresh);
        var prepare = new HankiButton { Text = "Prepare for ChatGPT…", Appearance = HankiButtonStyle.Quiet, AutoSize = true, Enabled = false };
        prepare.Click += (_, _) => { if (!IsBusy) PrepareRequested?.Invoke(report.Text); };
        bar.Controls.Add(prepare); overview.Controls.Add(bar);
        var settings = new HankiButton { Text = "Windows pagefile settings…", Appearance = HankiButtonStyle.Quiet, AutoSize = true };
        settings.Click += (_, _) => {
            if (MessageBox.Show(this, "Open Windows Performance Options?\nChoose Advanced → Virtual memory → Change to review pagefile settings.\n\nChanges made there are outside Hanki's undo history and may require a restart. Hanki will not change a setting for you.", "Review Windows settings", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.OK)
                DesktopShortcuts.Open(this, "pagefile");
        };
        bar.Controls.Add(settings);
        var guideText = "WHAT IS THE PAGEFILE?\r\n\r\n" +
            "Windows uses pagefiles to back some committed memory and to move less-used modified memory pages out of RAM. They can also support crash dumps. A pagefile is not a substitute for fast physical RAM.\r\n\r\n" +
            "HOW MUCH DO I NEED?\r\n\r\n" +
            "There is no universal RAM multiplier. Sizing depends on peak committed-memory demand and crash-dump requirements. One snapshot cannot determine the right custom size.\r\n\r\n" +
            "System-managed sizing is Windows' default and is the usual starting point unless a workload or administrator requires a specific configuration. Leave disk headroom for growth. Do not disable the pagefile just to chase a performance gain.\r\n\r\n" +
            "HOW TO READ THE DASHBOARD\r\n\r\n" +
            "RAM available: physical memory available for allocation.\r\nCommit / limit: promised memory versus its backing limit, not disk paging activity.\r\nPeak commit: the highest system commit since boot; it may not include your heaviest future workload.\r\nActive pagefiles: Windows' current allocated size and usage.\r\nConfigured entries: requested settings; they can differ from active state before a restart.\r\n\r\n" +
            "NEXT STEP\r\n\r\n" +
            "Take snapshots while your normal heavy workload is running. If commit repeatedly approaches its limit, investigate workload demand, system-managed configuration and disk headroom. Increasing the pagefile is not an automatic FPS improvement.\r\n\r\n" +
            "Crash-dump support also depends on dump mode, storage and configuration; Hanki does not certify it.\r\n\r\n" +
            "ADVISOR AND SAMPLING\r\n\r\nThe snapshot includes guidance based on current commit headroom and peak demand since boot. Use Sampling / comparison for 30-second sessions. Windows pagefile settings can be opened for manual review; changes there are outside Hanki's undo history. Hanki does not apply pagefile changes.\r\n\r\n" +
            "Microsoft reference:\r\nhttps://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/how-to-determine-the-appropriate-page-file-size-for-64-bit-versions-of-windows";
        var help = new HankiButton { Text = "Pagefile explained", AutoSize = true, Appearance = HankiButtonStyle.Quiet };
        help.Click += (_, _) => {
            using var dialog = new Form { Text = "Pagefile explained", Size = new Size(800, 620), MinimumSize = new Size(500, 400),
                StartPosition = FormStartPosition.CenterParent, Font = Font, Padding = new Padding(20) };
            var guide = TextArea(); guide.Text = guideText;
            var close = new HankiButton { Text = "Close", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.Cancel };
            dialog.Controls.Add(guide); dialog.Controls.Add(close); dialog.CancelButton = close;
            HankiTheme.Apply(dialog); dialog.ShowDialog(this);
        };
        bar.Controls.Add(help); Controls.Add(overview);
        report.Text = "No performance data collected yet. Click Take / refresh snapshot.\r\n\r\nReads memory counters, active pagefiles, configured pagefile/dump settings, fixed-drive free space and process working sets.\r\nNo report is uploaded and no settings or processes are changed.";
        results.ShowCards([new("Understand memory use", report.Text)], false);
        refresh.Click += async (_, _) => {
            if (pending is not null) return;
            using var cts = new CancellationTokenSource(); pending = cts; refresh.Enabled = prepare.Enabled = false;
            results.ShowCards([new("Collecting your snapshot…", "Reading memory, pagefiles and process working sets. No settings are changed.")], false);
            report.Text = "Collecting read-only snapshot…";
            try { report.Text = await Task.Run(() => PerformanceSnapshot.Collect(cts.Token), cts.Token); results.ShowCards(ResultPresentation.Performance(report.Text)); prepare.Enabled = true; }
            catch (OperationCanceledException) { report.Text = "Snapshot cancelled; incomplete data discarded."; results.ShowCards([new("Snapshot cancelled", report.Text)], false); }
            catch (Exception ex) { report.Text = "Snapshot unavailable: " + ex.Message + "\r\nNo settings changed."; results.ShowCards([new("Snapshot unavailable", report.Text)]); }
            finally { pending = null; refresh.Enabled = true; }
        };
    }
    private static TextBox TextArea() => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BackColor = Color.FromArgb(24, 36, 52), ForeColor = Color.WhiteSmoke, BorderStyle = BorderStyle.None };
}
