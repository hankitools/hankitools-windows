namespace IgezziGuard;

public class ToolPage : UserControl
{
    protected readonly FlowLayoutPanel Bar = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 12) };
    protected readonly TextBox Output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Both, HideSelection = false };
    private readonly HankiButton stop = new() { Text = "Cancel", AutoSize = true, Enabled = false, Visible = false };
    private readonly HankiButton export = new() { Text = "Review / share report", Appearance = HankiButtonStyle.Quiet, AutoSize = true };
    private readonly HankiButton assistant = new() { Text = "Prepare for Assistant", Appearance = HankiButtonStyle.Quiet, AutoSize = true };
    private readonly HankiButton previous = new() { Text = "Previous report", Appearance = HankiButtonStyle.Quiet, AutoSize = true, Enabled = false };
    private readonly Label state = new() { Text = "Ready when you are", AutoSize = true, Tag = "intro", Margin = new Padding(0, 9, 20, 8) };
    private readonly TextBox find = new() { Width = 190, PlaceholderText = "Find in report…", AccessibleName = "Find in report", Margin = new Padding(0, 6, 8, 6) };
    private readonly Label matches = new() { AutoSize = true, Tag = "intro", Margin = new Padding(8, 9, 0, 6) };
    private string? previousReport;
    private string previousState = "";
    private CancellationTokenSource? pending;
    public bool IsBusy => pending is not null;
    public void Cancel() => pending?.Cancel();
    public event Action<string>? PrepareRequested;

    public ToolPage(string disclosure)
    {
        Dock = DockStyle.Fill; Padding = new Padding(8); Output.Text = disclosure;
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        footer.Controls.AddRange([export, assistant, previous]);
        var statusBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        statusBar.Controls.AddRange([state, stop]);
        var searchBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
        var next = new HankiButton { Text = "Next match", AutoSize = true, Appearance = HankiButtonStyle.Quiet };
        var wrap = new CheckBox { Text = "Wrap lines", Checked = true, AutoSize = true, Margin = new Padding(14, 9, 0, 6) };
        searchBar.Controls.AddRange([find, next, wrap, matches]);
        var report = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), Tag = "card" };
        report.Controls.Add(Output);
        Controls.Add(report); Controls.Add(searchBar); Controls.Add(statusBar); Controls.Add(Bar); Controls.Add(footer);
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
            if (!IsBusy) state.Text = "Report updated · " + DateTime.Now.ToString("t");
        };
        previous.Click += (_, _) => {
            if (IsBusy || previousReport is null) return;
            string currentState = state.Text;
            (previousReport, Output.Text) = (Output.Text, previousReport);
            (previousState, state.Text) = (currentState, previousState);
            previous.Text = previous.Text == "Previous report" ? "Latest report" : "Previous report";
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F)) { find.Focus(); find.SelectAll(); return true; }
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

    protected async Task Run(Func<CancellationToken, Task<string>> work)
    {
        if (IsBusy) return;
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
            if (!IsDisposed) Output.Text = "Cancelled. Any completed actions remain in effect. Check Recovery and the relevant Windows status before retrying.";
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
