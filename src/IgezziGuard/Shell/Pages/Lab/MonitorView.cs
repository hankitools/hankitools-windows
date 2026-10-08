using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using Application = System.Windows.Application;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace IgezziGuard.Shell;

/// <summary>
/// Performance Lab → Monitor: measure the whole PC, or a game, for 30 seconds up to 15 minutes. The result is a verdict, one trend chart
/// per counter and the measured numbers; a measurement can be kept as the baseline, compared after one change, saved to Performance sessions or to a file.
/// </summary>
internal sealed class MonitorView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly LabState state;
    private readonly ComboBox target = new() { Width = 300, Margin = new Thickness(0, 0, 8, 8) }, duration = new() { Width = 130, Margin = new Thickness(0, 0, 8, 8) };
    private readonly Button start = Buttons.Primary("Start measuring"), cancel = Buttons.Secondary("Cancel"), baseline = Buttons.Secondary("Use as baseline"), compare = Buttons.Secondary("Compare with baseline"),
        save = Buttons.Secondary("Save to Performance sessions"), refresh = Buttons.Quiet("Refresh list"), toFile = Buttons.Quiet("Save to file"), fromFile = Buttons.Quiet("Load from file");
    private readonly TextBlock status = UiKit.Text("Ready when you are", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly ProgressBar bar = new() { Height = 6, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 8) };
    private readonly DiagnosisView view = new();
    private readonly UniformGrid charts = new() { Columns = 2, Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TrendChart cpu = new("Processor: busiest thread", "Accent"), gpu = new("Graphics: GPU busy", "AccentPerformance"), memory = new("Memory: committed", "Good"), disk = new("Disk: busiest disk active", "Review");
    private readonly TextBox detail = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12.5, Visibility = Visibility.Collapsed, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 0) };
    private IReadOnlyList<Process> processes = [];
    private CancellationTokenSource? running;

    internal bool IsBusy => running is not null;
    internal TrendChart CpuChart => cpu;
    internal DiagnosisView Result => view;

    internal MonitorView(IShellServices shell, LabState state)
    {
        this.shell = shell; this.state = state;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        root.Children.Add(UiKit.Text("Measure while you play or work: every second, processor load per thread, clocks, memory, disk, GPU load, temperature and video memory, and the busiest programs. Choose a game to also record its frame rate. Nothing is changed.", 14, UiKit.Res("TextMuted"), wrap: true));
        string[] lengths = ["30 seconds", "1 minute", "2 minutes", "5 minutes", "15 minutes"];
        foreach (var l in lengths) duration.Items.Add(l); duration.SelectedIndex = 1;

        System.Windows.Automation.AutomationProperties.SetName(target, "What to measure"); System.Windows.Automation.AutomationProperties.SetName(duration, "How long");
        var row1 = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; row1.Children.Add(target); row1.Children.Add(duration); row1.Children.Add(start); row1.Children.Add(cancel); row1.Children.Add(refresh);
        var row2 = new WrapPanel(); foreach (var b in new[] { baseline, compare, save, toFile, fromFile }) row2.Children.Add(b);
        root.Children.Add(row1); root.Children.Add(row2); root.Children.Add(bar); root.Children.Add(status);
        root.Children.Add(view);
        foreach (var c in new[] { cpu, gpu, memory, disk }) { c.Margin = new Thickness(0, 0, 14, 14); charts.Children.Add(c); }
        charts.SizeChanged += (_, e) => charts.Columns = e.NewSize.Width >= 760 ? 2 : 1;
        root.Children.Add(charts); root.Children.Add(detail);
        Content = root; cancel.IsEnabled = false; SetRunActions(false);

        start.Click += async (_, _) => await Measure();
        cancel.Click += (_, _) => running?.Cancel();
        refresh.Click += (_, _) => LoadTargets();
        baseline.Click += (_, _) => { if (state.Latest is null) { Say("Measure first."); return; } state.Baseline = state.Latest; Say("This measurement is now the baseline. Change one thing, measure the same game for the same time, then choose Compare with baseline."); };
        compare.Click += (_, _) => Compare();
        save.Click += (_, _) => SaveSession();
        toFile.Click += (_, _) => SaveFile();
        fromFile.Click += (_, _) => LoadFile();
        Loaded += (_, _) => { if (target.Items.Count == 0) LoadTargets(); };
    }

    private int Seconds => new[] { 30, 60, 120, 300, 900 }[Math.Max(0, duration.SelectedIndex)];
    private void Say(string text) => status.Text = text;
    private void SetRunActions(bool has) { baseline.IsEnabled = compare.IsEnabled = save.IsEnabled = toFile.IsEnabled = has; }

    private void LoadTargets()
    {
        foreach (var p in processes) p.Dispose();
        IReadOnlyList<GameEntry> games = [];
        try { games = GameLibrary.Read(GameLibrary.StorePath); } catch (IOException) { }
        processes = PerformanceRecorder.Candidates(games);
        target.Items.Clear(); target.Items.Add("The whole PC (no game)");
        foreach (var p in processes) { try { target.Items.Add($"{p.ProcessName} ({p.MainWindowTitle})".Trim()); } catch (InvalidOperationException) { target.Items.Add(p.ProcessName); } }
        target.SelectedIndex = 0;
    }
    private Process? Target => target.SelectedIndex > 0 && target.SelectedIndex - 1 < processes.Count ? processes[target.SelectedIndex - 1] : null;

    private async Task Measure()
    {
        if (IsBusy) return;
        var chosen = Target;
        if (chosen is not null) { try { if (chosen.HasExited) { Say("That program has closed. Choose Refresh list."); return; } } catch (InvalidOperationException) { } }
        using var cts = new CancellationTokenSource(); running = cts; start.IsEnabled = false; cancel.IsEnabled = true;
        using var ticket = shell.Tasks.Begin("Performance", cts.Cancel);
        view.Clear(); charts.Visibility = Visibility.Collapsed; detail.Visibility = Visibility.Collapsed;
        bar.Visibility = Visibility.Visible; bar.IsIndeterminate = true; Say("Starting the measurement…");
        int seconds = Seconds;   // read here: WPF controls can only be touched on the UI thread
        var progress = new Progress<string>(text => { if (running == cts) Say(text); });
        try {
            var run = await Task.Run(() => PerformanceRecorder.Record(seconds, chosen, progress, cts.Token), cts.Token);
            state.Latest = run; Show(run);
        }
        catch (OperationCanceledException) { Say("Cancelled. Nothing on your PC was changed; incomplete results were discarded. Run it again when you're ready."); }
        catch (Exception ex) { Say("Operation stopped: " + ex.Message); }
        finally { running = null; start.IsEnabled = true; cancel.IsEnabled = false; bar.Visibility = Visibility.Collapsed; }
    }

    /// <summary>Shows a measurement: the verdict and cards, then a chart for each counter.</summary>
    internal void Show(MonitorRun run)
    {
        view.Show(LabMonitorPanel.Summary(run, state.Baseline));
        var s = run.Samples;
        cpu.Set(s.Select(x => (double?)x.BusiestCore)); gpu.Set(s.Select(x => x.TargetGpuBusy ?? x.GpuBusy));
        memory.Set(s.Select(x => (double?)x.CommitPercent)); disk.Set(s.Select(x => (double?)x.DiskActivePercent));
        charts.Visibility = s.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        Say("Report ready · " + DateTime.Now.ToString("t")); SetRunActions(true);
    }

    private void ShowDetail(string text) { detail.Text = text; detail.Visibility = Visibility.Visible; detail.Style = (Style)Application.Current.FindResource("FieldBox"); detail.Background = UiKit.Res("Canvas"); }

    private void Compare()
    {
        if (state.Latest is null || state.Baseline is null) { Say("Measure a baseline, choose Use as baseline, make one change, then measure again the same way."); return; }
        if (ReferenceEquals(state.Latest, state.Baseline)) { Say("The latest measurement is the baseline. Measure again after your change."); return; }
        var before = BottleneckEngine.Measurement(state.Baseline); var after = BottleneckEngine.Measurement(state.Latest);
        var outcome = PerformanceComparison.Outcome(before, after);
        var changes = LabMonitorPanel.ChangesBetween(state.Baseline, state.Latest);
        ShowDetail(PerformanceComparison.Describe(outcome) + "\r\n\r\n" + PerformanceComparison.Table(before, after) +
            "\r\n\r\nChanges made in between (from Recovery):\r\n" + (changes.Count == 0 ? "none recorded" : string.Join("\r\n", changes.Select(c => $"• {c.Kind}: {c.Target}: {c.Before} → {c.After} ({c.Status})"))) +
            "\r\n\r\nSave to Performance sessions keeps this comparison. To undo the changes, use Recovery or the session's Restore settings.");
        Say("Comparison ready.");
    }

    private void SaveSession()
    {
        if (state.Latest is not { } latest) { Say("Measure first."); return; }
        bool compared = state.Baseline is not null && !ReferenceEquals(state.Baseline, latest);
        var before = BottleneckEngine.Measurement(compared ? state.Baseline! : latest); var after = compared ? BottleneckEngine.Measurement(latest) : null;
        var changes = compared ? LabMonitorPanel.ChangesBetween(state.Baseline!, latest).Select(c => c.Id).ToArray() : [];
        var outcome = PerformanceComparison.Outcome(before, after);
        var name = PerformanceSessionStore.DefaultName(latest.Target is { } t ? (compared ? "Comparison: " : "Measurement: ") + t : compared ? "Comparison" : "Measurement", latest.Started);
        try {
            PerformanceSessionsPanel.Store.Add(new PerformanceSession(Guid.NewGuid(), name, DateTimeOffset.UtcNow, before, changes, after, outcome, BottleneckEngine.Analyze(latest).Diagnosis));
            ShowDetail(PerformanceComparison.Describe(outcome) + "\r\n\r\n" + PerformanceComparison.Table(before, after)); Say("Saved to History → Performance sessions.");
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Say("Couldn't save the session: " + ex.Message); }
    }

    private void SaveFile()
    {
        if (state.Latest is null) { Say("Measure first."); return; }
        var picker = new Microsoft.Win32.SaveFileDialog { Filter = "Hanki measurement|*.json", FileName = "Hanki-measurement-" + state.Latest.Started.ToLocalTime().ToString("yyyyMMdd-HHmmss") + ".json", OverwritePrompt = true };
        if (picker.ShowDialog() != true) return;
        try { File.WriteAllText(picker.FileName, JsonSerializer.Serialize(state.Latest, LabState.Json)); Say("Saved. It contains measurements and program names from this PC; review before sharing."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Say(ex.Message); }
    }

    private void LoadFile()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Hanki measurement|*.json" };
        if (picker.ShowDialog() != true) return;
        try {
            if (new FileInfo(picker.FileName).Length > 50_000_000) throw new IOException("The file is too large.");
            var run = JsonSerializer.Deserialize<MonitorRun>(File.ReadAllText(picker.FileName), LabState.Json) ?? throw new IOException("Not a Hanki measurement.");
            if (run.Samples is null || run.Samples.Count > 100_000 || run.Notes is null) throw new IOException("Not a Hanki measurement.");
            state.Baseline = run; Say($"Loaded a measurement from {run.Started.ToLocalTime():g} as the baseline.");
        } catch (JsonException) { Say("Couldn't load it: this isn't a complete Hanki measurement file. It may be damaged, cut short or from another program."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { Say("Couldn't load it: " + ex.Message); }
    }
}
