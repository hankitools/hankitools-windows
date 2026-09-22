namespace IgezziGuard;

public sealed class DiagnosticPanel : UserControl
{
    private readonly TextBox output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        BackColor = Color.FromArgb(24, 36, 52), ForeColor = Color.WhiteSmoke };
    private CancellationTokenSource? pending;
    public bool IsBusy => pending is not null;
    public void Cancel() => pending?.Cancel();
    public event Action<string>? PrepareRequested;
    public DiagnosticPanel(string label, string disclosure, Func<CancellationToken, Task<string>> collect, Func<string, ResultCard[]>? present = null)
    {
        Dock = DockStyle.Fill;
        output.Text = disclosure;
        ResultCardsView? results = present is null ? null : new ResultCardsView(output);
        results?.ShowCards([new("Start with a protection review", disclosure)], false);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var run = new HankiButton { Text = label, AutoSize = true, Primary = true }; var stop = new HankiButton { Text = "Cancel", Enabled = false };
        var export = new HankiButton { Text = "Review / export…", Appearance = HankiButtonStyle.Quiet, AutoSize = true, Enabled = false };
        var assistant = new HankiButton { Text = "Prepare for ChatGPT…", Appearance = HankiButtonStyle.Quiet, AutoSize = true, Enabled = false };
        var activity = new Label { Text = "Ready • Start a check to see results", Dock = DockStyle.Top, Height = 38, Padding = new Padding(8), AccessibleRole = AccessibleRole.StatusBar };
        var progress = new ProgressBar { Dock = DockStyle.Top, Height = 3, Style = ProgressBarStyle.Marquee, Visible = false };
        output.BorderStyle = BorderStyle.None;
        bar.Padding = new Padding(0, 4, 0, 10);
        bar.Controls.AddRange([run, stop, export, assistant]); Controls.Add((Control?)results ?? output); Controls.Add(activity); Controls.Add(progress); Controls.Add(bar);
        stop.Click += (_, _) => Cancel();
        run.Click += async (_, _) => {
            if (pending is not null) return;
            using var cts = new CancellationTokenSource(); pending = cts;
            run.Enabled = export.Enabled = assistant.Enabled = false; stop.Enabled = true;
            activity.Text = "Collecting • You can cancel this check"; progress.Visible = true;
            results?.ShowCards([new("Reading protection status…", "The check is running. You can cancel from the toolbar.")], false);
            output.Text = "Collecting…\r\n" + disclosure;
            try { output.Text = await Task.Run(() => collect(cts.Token), cts.Token); if (present is not null) results!.ShowCards(present(output.Text)); export.Enabled = assistant.Enabled = true; activity.Text = $"Check finished at {DateTime.Now:t} • Review the findings below"; }
            catch (OperationCanceledException) { output.Text = "Cancelled — incomplete report discarded."; activity.Text = "Cancelled • Run the check again when ready"; results?.ShowCards([new("Check cancelled", output.Text)], false); }
            catch (Exception ex) { activity.Text = "Could not complete the check • See details below"; output.Text = "Collection failed: " + ex.Message + "\r\nNo settings changed."; results?.ShowCards([new("Check unavailable", output.Text)]); }
            finally { progress.Visible = false; pending = null; run.Enabled = true; stop.Enabled = false; }
        };
        assistant.Click += (_, _) => PrepareRequested?.Invoke(output.Text);
        export.Click += (_, _) => {
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
        };
    }
}
