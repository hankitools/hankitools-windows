namespace IgezziGuard;

public sealed class DiagnosticPanel : UserControl
{
    private readonly TextBox output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        BackColor = Color.FromArgb(24, 36, 52), ForeColor = Color.WhiteSmoke };
    private CancellationTokenSource? pending;
    public bool IsBusy => pending is not null;
    public void Cancel() => pending?.Cancel();
    public event Action<string>? PrepareRequested;

    /// <summary>Text report, optionally summarized into cards by <paramref name="present"/>.</summary>
    public DiagnosticPanel(string label, string disclosure, Func<CancellationToken, Task<string>> collect, Func<string, ResultCard[]>? present = null, string introTitle = "Before you start")
        : this(label, disclosure, present is null ? null : introTitle, async token => {
            var text = await collect(token);
            return present is null ? new Diagnosis(text, CardStatus.Info, "", []) : Diagnosis.From(text, present(text));
        }) { }

    /// <summary>Structured result: plain-language summary first, technical report on request.</summary>
    public DiagnosticPanel(string label, string disclosure, string? introTitle, Func<CancellationToken, Task<Diagnosis>> diagnose)
    {
        Dock = DockStyle.Fill;
        output.Text = disclosure;
        ResultCardsView? results = introTitle is null ? null : new ResultCardsView(output);
        results?.ShowCards([new(introTitle!, disclosure)], false);
        // One row: the check and (while it runs) Cancel on the left, report options (⋯) on the right.
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var run = new HankiButton { Text = label, AutoSize = true, Primary = true };
        var stop = new HankiButton { Text = "Cancel", AutoSize = true, Appearance = HankiButtonStyle.Quiet, Visible = false };
        var options = new HankiButton { Text = "⋯", AutoSize = true, AccessibleName = "Report options", Margin = Padding.Empty, Font = new Font("Segoe UI Semibold", 12f), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        bool reportReady = false;
        var activity = new Label { Text = "Ready • Start the check to see results", Dock = DockStyle.Top, Height = 34, Padding = new Padding(2, 8, 2, 6), Tag = "intro", AccessibleRole = AccessibleRole.StatusBar };
        var progress = new ProgressLine { Dock = DockStyle.Top };
        output.BorderStyle = BorderStyle.None;
        NativeTheme.PadText(output); output.Select(0, 0);
        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 0, 0, 8), Margin = Padding.Empty };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.AddRange([run, stop]); top.Controls.Add(bar, 0, 0); top.Controls.Add(options, 1, 0);
        Controls.Add((Control?)results ?? output); Controls.Add(activity); Controls.Add(progress); Controls.Add(top);
        stop.Click += (_, _) => Cancel();
        options.Click += (_, _) => {
            var menu = HankiMenu.Create();
            menu.Items.Add(HankiMenu.Item("Review / export…", Export, reportReady));
            menu.Items.Add(HankiMenu.Item("Prepare for Assistant", () => PrepareRequested?.Invoke(output.Text), reportReady));
            menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
            menu.Show(options, new Point(options.Width - menu.GetPreferredSize(Size.Empty).Width, options.Height + 4));
        };
        run.Click += async (_, _) => {
            if (pending is not null) return;
            using var cts = new CancellationTokenSource(); pending = cts;
            run.Enabled = reportReady = false; stop.Visible = true;
            activity.Text = "Checking… You can cancel at any time"; progress.Busy = true;
            results?.ShowCards([new("Checking…", "This usually takes a few seconds. Nothing on your PC is changed.")], false);
            output.Text = "Collecting…\r\n" + disclosure;
            try {
                var result = await Task.Run(() => diagnose(cts.Token), cts.Token);
                output.Text = result.Report;
                results?.Show(result);
                reportReady = true; activity.Text = $"Finished at {DateTime.Now:t}" + (results is null ? "" : " • Summary below; the full report is under View technical details");
            }
            catch (OperationCanceledException) { output.Text = "Cancelled — incomplete report discarded."; activity.Text = "Cancelled • Run the check again when ready"; results?.ShowCards([new("Check cancelled", output.Text, CardStatus.Unknown)], false); }
            catch (Exception ex) { activity.Text = "Could not complete the check"; output.Text = "Collection failed: " + ex.Message + "\r\nNo settings changed."; results?.ShowCards([new("Check unavailable", output.Text, CardStatus.Unknown)]); }
            finally { progress.Busy = false; pending = null; run.Enabled = true; stop.Visible = false; }
        };
        void Export() {
            using var review = new Form { Text = "Review / redact before local export", Size = new Size(850, 600), StartPosition = FormStartPosition.CenterParent };
            var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Text = output.Text };
            var save = new HankiButton { Text = "Save reviewed text…", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK };
            review.Controls.Add(text); review.Controls.Add(save);
            HankiTheme.Apply(review);
            if (review.ShowDialog(this) != DialogResult.OK) return;
            using var dialog = new SaveFileDialog { Filter = "Text report|*.txt", FileName = "Hanki-report.txt", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dialog.FileName, text.Text); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export failed"); }
        }
    }
}
