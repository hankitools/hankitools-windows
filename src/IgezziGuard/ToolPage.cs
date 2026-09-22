namespace IgezziGuard;

public class ToolPage : UserControl
{
    protected readonly FlowLayoutPanel Bar = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
    protected readonly TextBox Output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Both };
    private readonly HankiButton stop = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly HankiButton export = new() { Text = "Review / export", Appearance = HankiButtonStyle.Quiet, AutoSize = true };
    private readonly HankiButton assistant = new() { Text = "Prepare for Assistant", Appearance = HankiButtonStyle.Quiet, AutoSize = true };
    private CancellationTokenSource? pending;
    public bool IsBusy => pending is not null;
    public void Cancel() => pending?.Cancel();
    public event Action<string>? PrepareRequested;
    public ToolPage(string disclosure)
    {
        Dock = DockStyle.Fill; Output.Text = disclosure;
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true }; footer.Controls.AddRange([stop, export, assistant]);
        Controls.Add(Output); Controls.Add(Bar); Controls.Add(footer);
        stop.Click += (_, _) => Cancel(); assistant.Click += (_, _) => PrepareRequested?.Invoke(Output.Text);
        export.Click += (_, _) => {
            using var review = new Form { Text = "Review and redact report", Size = new Size(900, 650), StartPosition = FormStartPosition.CenterParent };
            var body = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Text = Output.Text };
            var save = new HankiButton { Text = "Save reviewed text", Dock = DockStyle.Bottom, Height = 44, DialogResult = DialogResult.OK };
            review.Controls.Add(body); review.Controls.Add(save); HankiTheme.Apply(review);
            if (review.ShowDialog(this) != DialogResult.OK) return;
            using var picker = new SaveFileDialog { Filter = "Text report|*.txt", FileName = "Hanki-report.txt", OverwritePrompt = true };
            if (picker.ShowDialog(this) == DialogResult.OK) try { File.WriteAllText(picker.FileName, body.Text); } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        };
    }
    protected HankiButton Button(string text, Action action)
    {
        var b = new HankiButton { Text = text, AutoSize = true, Primary = !Bar.Controls.OfType<HankiButton>().Any() }; b.Click += (_, _) => { if (!IsBusy) action(); }; Bar.Controls.Add(b); return b;
    }
    protected bool Review(string text) => MessageBox.Show(this, text, "Review action", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.OK;
    protected async Task Run(Func<CancellationToken, Task<string>> work)
    {
        if (IsBusy) return;
        using var cts = new CancellationTokenSource(); pending = cts;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) => stop.Text = $"Cancel · {elapsed.Elapsed:mm\\:ss}";
        timer.Start();
        Bar.Enabled = export.Enabled = assistant.Enabled = false; stop.Enabled = true; Output.Text = "Working…";
        try { Output.Text = await Task.Run(() => work(cts.Token), cts.Token); }
        catch (OperationCanceledException) { Output.Text = "Cancelled. Any completed actions remain in effect. Check Recovery and the relevant Windows status before retrying."; }
        catch (Exception ex) { Output.Text = "Operation stopped: " + ex.Message + "\r\nFor a setting change, inspect Recovery before retrying. Access denied may require running Hanki as administrator; Hanki does not auto-elevate."; }
        finally { timer.Stop(); stop.Text = "Cancel"; pending = null; Bar.Enabled = export.Enabled = assistant.Enabled = true; stop.Enabled = false; }
    }
}
