namespace IgezziGuard;

public class ToolPage : UserControl
{
    /// <summary>The page's actions on one row: the first is primary; what doesn't fit moves into "More".</summary>
    protected readonly FlowLayoutPanel Bar = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
    protected readonly TextBox Output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Both, HideSelection = false };
    private readonly HankiButton stop = new() { Text = "Cancel", AutoSize = true, Enabled = false, Visible = false, Appearance = HankiButtonStyle.Quiet, Margin = new Padding(0, 0, 0, 0) };
    private readonly Label state = new() { Text = "Ready when you are", AutoSize = true, Tag = "intro", Margin = new Padding(0, 8, 12, 0), Font = new Font("Segoe UI Semibold", 10f) };
    private readonly SearchField find = new("Find in report  (Ctrl+F)", 240) { Margin = new Padding(8, 0, 0, 0) };
    private readonly Label matches = new() { AutoSize = true, Tag = "intro", Margin = new Padding(8, 8, 0, 0) };
    private readonly SummaryView summary = new() { Visible = false };
    private readonly HankiButton details = new() { Text = "Technical details", Appearance = HankiButtonStyle.Quiet, AutoSize = true, Visible = false, Margin = new Padding(0, 1, 4, 0), AccessibleName = "View technical details" };
    private readonly HankiButton options = new() { Text = "⋯", Appearance = HankiButtonStyle.Icon, AccessibleName = "Report options", Margin = new Padding(0, 1, 0, 0), Font = new Font("Segoe UI Semibold", 13f) };
    private readonly HankiButton barMore;
    private readonly ProgressLine progress = new() { Dock = DockStyle.Top };
    private readonly ToolTip tips = new();
    private RoundedPanel report = null!;
    // Report actions live in the ⋯ menu; they're off while a task runs.
    private bool reportActions = true, wrapLines = true;
    private string? previousReport;
    private string previousState = "";
    private CancellationTokenSource? pending;
    public bool IsBusy => pending is not null;
    public void Cancel() => pending?.Cancel();
    /// <summary>True for tools that only read; their cancel message then says nothing was changed.</summary>
    protected virtual bool ReadOnlyTool => false;
    public event Action<string>? PrepareRequested;

    public ToolPage(string disclosure)
    {
        Dock = DockStyle.Fill; Padding = new Padding(0, 4, 0, 0); Output.Text = disclosure;
        // Top row: actions on the left; the summary/details switch and report options on the right.
        var side = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        side.Controls.AddRange([details, options]);
        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 0, 0, 14), Margin = Padding.Empty };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(Bar, 0, 0); top.Controls.Add(side, 1, 0);
        barMore = Overflow.Attach(Bar, b => !b.Primary, label: "More", style: HankiButtonStyle.Secondary, maxRows: 2, keepVisible: 2);
        tips.SetToolTip(options, "Share, send to the Assistant, previous report");
        Disposed += (_, _) => tips.Dispose();
        // Report card: status and cancel on the left, search on the right, a progress line, then the report.
        var statusBar = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Tag = "card", Margin = Padding.Empty, Anchor = AnchorStyles.Left | AnchorStyles.Top };
        statusBar.Controls.AddRange([state, stop]);
        var searchBar = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Tag = "card", Margin = Padding.Empty, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        searchBar.Controls.AddRange([matches, find]);
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Tag = "card", Padding = new Padding(0, 0, 0, 12) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(statusBar, 0, 0); header.Controls.Add(searchBar, 1, 0);
        var spacer = new Panel { Dock = DockStyle.Top, Height = 12, Tag = "card" };
        report = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 14, 12, 12) };
        report.Controls.Add(Output); report.Controls.Add(spacer); report.Controls.Add(progress); report.Controls.Add(header);
        // The body holds either the plain-language summary or the technical report; subclasses insert above it at index 1.
        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(report); body.Controls.Add(summary);
        Controls.Add(body); Controls.Add(top);
        details.Click += (_, _) => SetSummaryVisible(!summary.Visible);
        options.Click += (_, _) => ShowOptions();
        // TextBox selects everything when focus arrives (for example when action buttons are disabled); a read-only report should not look selected.
        Output.Select(0, 0);
        Output.GotFocus += (_, _) => BeginInvoke(() => { if (Output.TextLength > 0 && Output.SelectionLength == Output.TextLength) Output.Select(0, 0); });
        stop.Click += (_, _) => { Cancel(); state.Text = "Cancelling…"; stop.Enabled = false; };
        find.Box.TextChanged += (_, _) => { Output.Select(0, 0); FindNext(); };
        find.Box.KeyDown += (_, e) => {
            if (e.KeyCode == Keys.Enter) { FindNext(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { find.Box.Clear(); Output.Focus(); e.SuppressKeyPress = true; }
        };
        Output.TextChanged += (_, _) => {
            matches.Text = "";
            if (IsBusy) return;
            state.Text = "Report updated · " + DateTime.Now.ToString("t");
            // A direct message (outside a run) replaces the previous result, so its summary no longer applies.
            hasSummary = details.Visible = false; SetSummaryVisible(false);
        };
    }

    /// <summary>Share, Assistant, previous report and line wrapping, in one menu instead of a row of links.</summary>
    private void ShowOptions()
    {
        var menu = HankiMenu.Create(checks: true);
        menu.Items.Add(HankiMenu.Item("Review / share report…", ReviewReport, reportActions));
        menu.Items.Add(HankiMenu.Item("Prepare for Assistant", () => PrepareRequested?.Invoke(Output.Text), reportActions));
        menu.Items.Add(HankiMenu.Item(showingPrevious ? "Back to the latest report" : "Show the previous report", SwapPrevious, reportActions && previousReport is not null));
        menu.Items.Add(new ToolStripSeparator());
        var wrap = HankiMenu.Item("Wrap long lines", () => { wrapLines = !wrapLines; Output.WordWrap = wrapLines; });
        wrap.Checked = wrapLines; menu.Items.Add(wrap);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(options, new Point(options.Width - menu.GetPreferredSize(Size.Empty).Width, options.Height + 4));
    }
    private bool showingPrevious;
    private void SwapPrevious()
    {
        if (IsBusy || previousReport is null) return;
        SetSummaryVisible(false);
        string currentState = state.Text;
        (previousReport, Output.Text) = (Output.Text, previousReport);
        (previousState, state.Text) = (currentState, previousState);
        showingPrevious = !showingPrevious;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F)) { SetSummaryVisible(false); find.Box.Focus(); find.Box.SelectAll(); return true; }
        if (keyData == Keys.F3) { FindNext(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void FindNext()
    {
        string text = find.Box.Text;
        if (string.IsNullOrEmpty(text)) { matches.Text = ""; Output.SelectionLength = 0; return; }
        int start = Output.SelectionStart + Output.SelectionLength;
        int index = Output.Text.IndexOf(text, start, StringComparison.OrdinalIgnoreCase);
        bool wrapped = index < 0 && start > 0;
        if (wrapped) index = Output.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase);
        if (index < 0) { matches.Text = "No matches"; return; }
        Output.Select(index, text.Length); Output.ScrollToCaret();
        matches.Text = wrapped ? "Back to first match" : "Match found";
    }

    private void ReviewReport()
    {
        using var review = new Form { Text = "Review before sharing", Size = new Size(900, 650), MinimumSize = new Size(500, 350), StartPosition = FormStartPosition.CenterParent };
        var hint = new Label { Text = "Remove names, paths, keys and other private details before copying or saving.", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), Tag = "intro" };
        var body = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Text = Output.Text, AccessibleName = "Editable report for review" };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8) };
        var save = new HankiButton { Text = "Save reviewed text", AutoSize = true, Primary = true };
        var copy = new HankiButton { Text = "Copy reviewed text", AutoSize = true };
        var close = new HankiButton { Text = "Close", AutoSize = true, Appearance = HankiButtonStyle.Quiet, DialogResult = DialogResult.Cancel };
        var feedback = new Label { AutoSize = true, Margin = new Padding(8, 12, 0, 0) };
        actions.Controls.AddRange([save, copy, close, feedback]);
        review.SizeChanged += (_, _) => hint.MaximumSize = new Size(review.ClientSize.Width, 0);
        hint.MaximumSize = new Size(review.ClientSize.Width, 0);
        review.Controls.Add(body); review.Controls.Add(hint); review.Controls.Add(actions); review.CancelButton = close;
        copy.Click += (_, _) => {
            try { if (body.Text.Length == 0) { feedback.Text = "Nothing to copy"; return; } Clipboard.SetText(body.Text); feedback.Text = "Reviewed text copied"; }
            catch (Exception ex) { MessageBox.Show(review, ex.Message, "Could not copy report"); }
        };
        save.Click += (_, _) => {
            using var picker = new SaveFileDialog { Filter = "Text report|*.txt", FileName = "Hanki-report-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt", OverwritePrompt = true };
            if (picker.ShowDialog(review) != DialogResult.OK) return;
            try { File.WriteAllText(picker.FileName, body.Text); feedback.Text = "Report saved"; }
            catch (Exception ex) { MessageBox.Show(review, ex.Message, "Could not save report"); }
        };
        HankiTheme.Apply(review); review.ShowDialog(this);
    }

    protected HankiButton Button(string text, Action action)
    {
        var b = new HankiButton { Text = text, AutoSize = true, Primary = !Bar.Controls.OfType<HankiButton>().Any(x => x != barMore) };
        b.Click += (_, _) => { if (!IsBusy) action(); }; Bar.Controls.Add(b); return b;
    }
    protected bool Review(string text) => MessageBox.Show(this, text, "Review action", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.OK;

    /// <summary>Show a plain-language summary in place of the report; the report stays one click away.</summary>
    protected void ShowSummary(Diagnosis diagnosis)
    {
        summary.Show(diagnosis.Status, diagnosis.Headline, diagnosis.Cards);
        hasSummary = details.Visible = true; SetSummaryVisible(true);
    }
    private bool hasSummary;
    private void SetSummaryVisible(bool visible)
    {
        // A field, not details.Visible: Visible reads false whenever this page's tab is not shown.
        visible &= hasSummary;
        summary.Visible = visible; report.Visible = !visible;
        details.Text = visible ? "Technical details" : "Back to summary";
        details.Invalidate();
    }
    /// <summary>Runs structured work: the report fills the technical view and the summary is shown first.</summary>
    protected async Task Run(Func<CancellationToken, Task<Diagnosis>> work)
    {
        Diagnosis? result = null;
        await Run(async token => { result = await work(token); return result.Report; });
        if (result is not null && !IsDisposed) ShowSummary(result);
    }

    protected async Task Run(Func<CancellationToken, Task<string>> work)
    {
        if (IsBusy) return;
        hasSummary = details.Visible = false; SetSummaryVisible(false);
        previousReport = Output.Text; previousState = state.Text; showingPrevious = false;
        using var cts = new CancellationTokenSource(); pending = cts;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) => state.Text = (cts.IsCancellationRequested ? "Cancelling" : "Working") + $" · {elapsed.Elapsed:mm\\:ss}";
        timer.Start();
        Bar.Enabled = reportActions = false; progress.Busy = true;
        stop.Visible = stop.Enabled = true; state.Text = "Working…"; Output.Text = "Collecting results. Your previous report remains available after this operation.";
        string outcome = "Report ready";
        try {
            string result = await Task.Run(() => work(cts.Token), cts.Token);
            if (!IsDisposed) Output.Text = result;
        }
        catch (OperationCanceledException) {
            outcome = "Cancelled";
            if (!IsDisposed) Output.Text = ReadOnlyTool ? "Cancelled. Nothing on your PC was changed; incomplete results were discarded. Run it again when you're ready."
                : "Cancelled. Any completed actions remain in effect. Check Recovery and the relevant Windows status before retrying.";
        }
        catch (Exception ex) {
            outcome = "Could not finish";
            if (!IsDisposed) Output.Text = "Operation stopped: " + ex.Message + "\r\nFor a setting change, inspect Recovery before retrying. Access denied may require running Hanki as administrator; Hanki does not auto-elevate.";
        }
        finally {
            timer.Stop(); pending = null;
            if (!IsDisposed) {
                Bar.Enabled = reportActions = true; progress.Busy = false;
                stop.Enabled = stop.Visible = false;
                state.Text = outcome + $" · {elapsed.Elapsed:mm\\:ss} · " + DateTime.Now.ToString("t");
            }
        }
    }
}
