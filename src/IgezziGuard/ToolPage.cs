namespace IgezziGuard;

public class ToolPage : UserControl
{
    protected readonly FlowLayoutPanel Bar = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 14) };
    protected readonly TextBox Output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Both, HideSelection = false };
    private readonly HankiButton stop = new() { Text = "Cancel", AutoSize = true, Enabled = false, Visible = false, Appearance = HankiButtonStyle.Quiet, Margin = new Padding(0, 0, 0, 0) };
    private readonly HankiButton export = new() { Text = "Review / share report", Appearance = HankiButtonStyle.Quiet, AutoSize = true };
    private readonly HankiButton assistant = new() { Text = "Prepare for Assistant", Appearance = HankiButtonStyle.Quiet, AutoSize = true };
    private readonly HankiButton previous = new() { Text = "Previous report", Appearance = HankiButtonStyle.Quiet, AutoSize = true, Enabled = false };
    private readonly Label state = new() { Text = "Ready when you are", AutoSize = true, Tag = "intro", Margin = new Padding(0, 7, 12, 0), Font = new Font("Segoe UI Semibold", 9.75f) };
    private readonly TextBox find = new() { Width = 200, PlaceholderText = "Find in report  (Ctrl+F)", AccessibleName = "Find in report", Margin = new Padding(8, 3, 0, 0) };
    private readonly Label matches = new() { AutoSize = true, Tag = "intro", Margin = new Padding(8, 7, 0, 0) };
    private readonly SummaryView summary = new() { Visible = false };
    private readonly HankiButton details = new() { Text = "View technical details", Appearance = HankiButtonStyle.Quiet, AutoSize = true, Visible = false };
    private RoundedPanel report = null!;
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
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        footer.Controls.AddRange([details, export, assistant, previous]);
        // Report card: status and cancel on the left, search tools on the right, report below.
        var statusBar = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Tag = "card", Margin = Padding.Empty, Anchor = AnchorStyles.Left | AnchorStyles.Top };
        statusBar.Controls.AddRange([state, stop]);
        var next = new HankiButton { Text = "Next", AutoSize = true, Appearance = HankiButtonStyle.Quiet, Margin = new Padding(4, 0, 0, 0) };
        var wrap = new CheckBox { Text = "Wrap lines", Checked = true, AutoSize = true, Margin = new Padding(12, 7, 0, 0) };
        var searchBar = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Tag = "card", Margin = Padding.Empty, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        searchBar.Controls.AddRange([matches, find, next, wrap]);
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Tag = "card", Padding = new Padding(0, 0, 0, 10) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(statusBar, 0, 0); header.Controls.Add(searchBar, 1, 0);
        var divider = new Panel { Dock = DockStyle.Top, Height = 1, Tag = "divider" };
        divider.Paint += (_, e) => { if (!SystemInformation.HighContrast) e.Graphics.Clear(HankiTheme.Border); };
        var spacer = new Panel { Dock = DockStyle.Top, Height = 12, Tag = "card" };
        report = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 12, 12) };
        report.Controls.Add(Output); report.Controls.Add(spacer); report.Controls.Add(divider); report.Controls.Add(header);
        // The body holds either the plain-language summary or the technical report; subclasses insert above it at index 1.
        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(report); body.Controls.Add(summary);
        Controls.Add(body); Controls.Add(Bar); Controls.Add(footer);
        details.Click += (_, _) => SetSummaryVisible(!summary.Visible);
        // TextBox selects everything when focus arrives (for example when action buttons are disabled); a read-only report should not look selected.
        Output.Select(0, 0);
        Output.GotFocus += (_, _) => BeginInvoke(() => { if (Output.TextLength > 0 && Output.SelectionLength == Output.TextLength) Output.Select(0, 0); });
        stop.Click += (_, _) => { Cancel(); state.Text = "Cancelling…"; stop.Enabled = false; };
        assistant.Click += (_, _) => PrepareRequested?.Invoke(Output.Text);
        export.Click += (_, _) => ReviewReport();
        wrap.CheckedChanged += (_, _) => Output.WordWrap = wrap.Checked;
        next.Click += (_, _) => FindNext();
        find.TextChanged += (_, _) => { Output.Select(0, 0); FindNext(); };
        find.KeyDown += (_, e) => {
            if (e.KeyCode == Keys.Enter) { FindNext(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { find.Clear(); Output.Focus(); e.SuppressKeyPress = true; }
        };
        Output.TextChanged += (_, _) => {
            matches.Text = "";
            if (IsBusy) return;
            state.Text = "Report updated · " + DateTime.Now.ToString("t");
            // A direct message (outside a run) replaces the previous result, so its summary no longer applies.
            hasSummary = details.Visible = false; SetSummaryVisible(false);
        };
        previous.Click += (_, _) => {
            if (IsBusy || previousReport is null) return;
            SetSummaryVisible(false);
            string currentState = state.Text;
            (previousReport, Output.Text) = (Output.Text, previousReport);
            (previousState, state.Text) = (currentState, previousState);
            previous.Text = previous.Text == "Previous report" ? "Latest report" : "Previous report";
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F)) { SetSummaryVisible(false); find.Focus(); find.SelectAll(); return true; }
        if (keyData == Keys.F3) { FindNext(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void FindNext()
    {
        if (string.IsNullOrEmpty(find.Text)) { matches.Text = ""; Output.SelectionLength = 0; return; }
        int start = Output.SelectionStart + Output.SelectionLength;
        int index = Output.Text.IndexOf(find.Text, start, StringComparison.OrdinalIgnoreCase);
        bool wrapped = index < 0 && start > 0;
        if (wrapped) index = Output.Text.IndexOf(find.Text, StringComparison.OrdinalIgnoreCase);
        if (index < 0) { matches.Text = "No matches"; return; }
        Output.Select(index, find.Text.Length); Output.ScrollToCaret();
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
        var b = new HankiButton { Text = text, AutoSize = true, Primary = !Bar.Controls.OfType<HankiButton>().Any() };
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
        details.Text = visible ? "View technical details" : "Back to summary";
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
        previousReport = Output.Text; previousState = state.Text; previous.Text = "Previous report";
        using var cts = new CancellationTokenSource(); pending = cts;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) => state.Text = (cts.IsCancellationRequested ? "Cancelling" : "Working") + $" · {elapsed.Elapsed:mm\\:ss}";
        timer.Start();
        Bar.Enabled = export.Enabled = assistant.Enabled = previous.Enabled = false;
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
                Bar.Enabled = export.Enabled = assistant.Enabled = previous.Enabled = true;
                stop.Enabled = stop.Visible = false;
                state.Text = outcome + $" · {elapsed.Elapsed:mm\\:ss} · " + DateTime.Now.ToString("t");
            }
        }
    }
}
