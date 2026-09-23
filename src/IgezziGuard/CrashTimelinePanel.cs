namespace IgezziGuard;

public sealed class CrashTimelinePanel : UserControl
{
    private readonly ComboBox markers = new() { Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker center = new() { Width = 215, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss" };
    private readonly NumericUpDown minutes = new() { Minimum = 1, Maximum = 60, Value = 15, Width = 65 };
    private readonly TextBox report = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 58, Text = "Read recent markers or enter a known crash time (local time). A marker's logged time may be after restart." };
    private readonly HankiButton load = new() { Text = "Find restart markers", AutoSize = true, Primary = true };
    private readonly HankiButton collect = new() { Text = "Build timeline", AutoSize = true, Primary = true };
    private readonly HankiButton stop = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly HankiButton export = new() { Text = "Review / export", Appearance = HankiButtonStyle.Quiet, AutoSize = true, Enabled = false };
    private readonly HankiButton assistant = new() { Text = "Explain with Assistant", Appearance = HankiButtonStyle.Quiet, AutoSize = true, Enabled = false };
    private CancellationTokenSource? pending;
    private List<CrashEvent> found = [];
    public event Action<string>? PrepareRequested;
    public bool IsBusy => pending is not null;
    public void Cancel() => pending?.Cancel();
    public CrashTimelinePanel()
    {
        Dock = DockStyle.Fill;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
        top.Controls.AddRange([load, markers]);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        controls.Controls.AddRange([new Label { Text = "Local time", AutoSize = true, Margin = new Padding(4, 12, 4, 4) }, center,
            new Label { Text = "± minutes", AutoSize = true, Margin = new Padding(4, 12, 4, 4) }, minutes, collect, stop, export, assistant]);
        Controls.Add(report); Controls.Add(controls); Controls.Add(top); Controls.Add(status);
        report.Text = "SEE WHAT HAPPENED BEFORE A CRASH\r\n\r\n1. Click Find restart markers to list unexpected restarts and blue screens from the last 7 days.\r\n2. Pick one, or type the time you remember the problem happening.\r\n3. Click Build timeline to see everything Windows logged in the minutes around it, with a plain explanation of each event.\r\n\r\nThe restart record itself isn't the cause. Look for disk, hardware or driver events just before it. Nothing is changed or uploaded.";
        load.Click += async (_, _) => await Run(async token => {
            var end = DateTimeOffset.Now;
            var result = await Task.Run(() => CrashEventReader.Read(end.AddDays(-7), end, true, token), token);
            found = result.Events.OrderByDescending(e => e.Time).ToList(); markers.Items.Clear();
            foreach (var e in found) markers.Items.Add($"{e.Time.ToLocalTime():ddd d MMM  HH:mm:ss}  ·  {EventKnowledge.Describe(e.Provider, e.Id).Name}");
            if (found.Count > 0) markers.SelectedIndex = 0;
            report.Text = found.Count == 0
                ? $"No unexpected restarts or blue screens were recorded in the last 7 days.\r\n\r\nIf you know roughly when something went wrong, set that time below and click Build timeline.\r\n\r\n{result.Coverage}"
                : $"Found {found.Count} restart record(s) in the last 7 days; the newest is selected. One restart often leaves two records.\r\n\r\nNext: click Build timeline to see what Windows logged in the minutes around it.\r\n\r\n{result.Coverage}";
        });
        markers.SelectedIndexChanged += (_, _) => { if (markers.SelectedIndex >= 0) center.Value = found[markers.SelectedIndex].Time.LocalDateTime; };
        collect.Click += async (_, _) => await Run(async token => {
            var local = DateTime.SpecifyKind(center.Value, DateTimeKind.Unspecified);
            if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local)) throw new ArgumentException("This local time is invalid or ambiguous at a daylight-saving transition. Choose an unambiguous nearby time and widen the window.");
            var time = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)); int range = (int)minutes.Value;
            var result = await Task.Run(() => CrashEventReader.Read(time.AddMinutes(-range), time.AddMinutes(range), false, token), token);
            report.Text = CrashTimeline.Report(result.Events, time, range, result.Coverage);
            export.Enabled = assistant.Enabled = true;
        });
        stop.Click += (_, _) => Cancel(); assistant.Click += (_, _) => PrepareRequested?.Invoke(report.Text);
        export.Click += (_, _) => {
            using var review = new Form { Text = "Review / redact timeline", Size = new Size(900, 650), StartPosition = FormStartPosition.CenterParent };
            var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Text = report.Text };
            var save = new HankiButton { Text = "Save reviewed report", Dock = DockStyle.Bottom, Height = 44, DialogResult = DialogResult.OK, Primary = true };
            review.Controls.Add(text); review.Controls.Add(save); HankiTheme.Apply(review);
            if (review.ShowDialog(this) != DialogResult.OK) return;
            using var dialog = new SaveFileDialog { Filter = "Text report|*.txt", FileName = "Hanki-crash-timeline.txt", OverwritePrompt = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) try { File.WriteAllText(dialog.FileName, text.Text); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export failed"); }
        };
    }
    private async Task Run(Func<CancellationToken, Task> work) {
        if (IsBusy) return;
        using var cts = new CancellationTokenSource(); pending = cts;
        load.Enabled = collect.Enabled = center.Enabled = minutes.Enabled = markers.Enabled = export.Enabled = assistant.Enabled = false; stop.Enabled = true;
        status.Text = "Reading Windows Event Logs…";
        try { await work(cts.Token); status.Text = "Ready. Review coverage and timestamps before drawing conclusions."; }
        catch (OperationCanceledException) { report.Text = "Cancelled. Incomplete timeline discarded."; status.Text = "Cancelled."; }
        catch (Exception ex) { report.Text = "Timeline unavailable: " + ex.Message; status.Text = "No settings changed."; }
        finally { pending = null; load.Enabled = collect.Enabled = center.Enabled = minutes.Enabled = markers.Enabled = true; stop.Enabled = false; }
    }
}
