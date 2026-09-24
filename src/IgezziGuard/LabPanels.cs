using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace IgezziGuard;

/// <summary>The Lab's latest measurement and chosen baseline, shared by its tabs.</summary>
internal sealed class LabState
{
    internal MonitorRun? Latest, Baseline;
    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = false };
}

/// <summary>Performance Lab → Monitor: measure the PC, optionally with a game, and compare against a baseline (HANKI-PERF-301/303/312, HANKI-GAME-212, HANKI-GPU-109).</summary>
public sealed class LabMonitorPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    private readonly ComboBox target = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "What to measure", Margin = new Padding(0, 6, 8, 0) };
    private readonly ComboBox duration = new() { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "How long", Margin = new Padding(0, 6, 8, 0) };
    private readonly LabState state;
    private IReadOnlyList<Process> processes = [];
    internal LabMonitorPanel(LabState state) : base("Measure while you play or work: every second, processor load per thread, clocks, memory, disk, GPU load, temperature and video memory, and the busiest programs. Choose a game to also record its frame rate (DirectX games, with Hanki running as administrator). Measure once as a baseline, change one thing, then measure the same way again to compare. Nothing is changed.")
    {
        this.state = state;
        Bar.Controls.Add(target);
        duration.Items.AddRange(["30 seconds", "1 minute", "2 minutes", "5 minutes", "15 minutes"]); duration.SelectedIndex = 1;
        Bar.Controls.Add(duration);
        Button("Start measuring", Measure);
        Button("Use as baseline", () => {
            if (state.Latest is null) { Output.Text = "Measure first."; return; }
            state.Baseline = state.Latest; Output.Text = "This measurement is now the baseline. Change one thing, measure the same game for the same time, then choose Compare with baseline.";
        });
        Button("Compare with baseline", Compare);
        Button("Save to Performance sessions", SaveSession);
        Button("Refresh list", LoadTargets);
        Button("Save to file", SaveFile);
        Button("Load from file", LoadFile);
        VisibleChanged += (_, _) => { if (Visible && target.Items.Count == 0) LoadTargets(); };
    }
    private int Seconds => new[] { 30, 60, 120, 300, 900 }[Math.Max(0, duration.SelectedIndex)];

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

    private async void Measure()
    {
        var chosen = Target;
        if (chosen is not null) { try { if (chosen.HasExited) { Output.Text = "That program has closed. Choose Refresh list."; return; } } catch (InvalidOperationException) { } }
        var progress = new Progress<string>(text => { if (IsBusy) Output.Text = text; });
        await Run(async token => {
            var run = await PerformanceRecorder.Record(Seconds, chosen, progress, token);
            state.Latest = run;
            return Summary(run, state.Baseline);
        });
    }

    internal static Diagnosis Summary(MonitorRun run, MonitorRun? baseline)
    {
        var s = run.Samples; var cards = new List<ResultCard>();
        if (s.Count == 0) return Diagnosis.From("No measurement was taken.", [new("No data", "The measurement stopped before the first second.", CardStatus.Unknown)]);
        if (run.Frames is { } f)
            cards.Add(new("Frame rate", $"{f.AverageFps:0} FPS on average · 1% low {f.Low1Fps:0} FPS · 99% of frames within {f.P99FrameMs:0.0} ms", CardStatus.Info));
        cards.Add(new("Processor", $"Busiest thread {BottleneckEngine.Percentile(s.Select(x => x.BusiestCore), 90):0}% · all cores {s.Average(x => x.CpuTotal):0}% on average" +
            (s.Select(x => x.CpuEffectiveMhz).Where(x => x is not null).DefaultIfEmpty(null).Max() is { } mhz ? $" · up to {mhz / 1000:0.0} GHz" : ""), CardStatus.Info));
        var gpu = s.Select(x => x.TargetGpuBusy ?? x.GpuBusy).Where(x => x is not null).Select(x => x!.Value).ToArray();
        cards.Add(new("Graphics", gpu.Length == 0 ? "GPU load isn't reported on this PC." :
            $"GPU {gpu.Average():0}% busy on average" + (s.Select(x => x.GpuTemperature).Where(x => x is not null).DefaultIfEmpty(null).Max() is { } t ? $" · up to {t:0} °C" : "") +
            (s.Select(x => x.GpuVramUsedMb).Where(x => x is not null).DefaultIfEmpty(null).Max() is { } v && run.GpuVramTotalMb is { } total ? $" · video memory up to {v / 1024:0.0} of {total / 1024:0.0} GB" : ""), CardStatus.Info));
        cards.Add(new("Memory", $"At least {s.Min(x => x.AvailableMb) / 1024:0.0} GB free · committed up to {s.Max(x => x.CommitPercent):0}% · hard faults up to {s.Max(x => x.HardFaultsPerSecond):0}/s",
            s.Max(x => x.CommitPercent) >= 90 || s.Min(x => x.AvailableMb) < 700 ? CardStatus.Review : CardStatus.Info));
        cards.Add(new("Disk", $"Busiest disk {s.Average(x => x.DiskActivePercent):0}% busy on average, fully busy in {s.Count(x => x.DiskActivePercent >= 90)} of {s.Count} seconds", CardStatus.Info));
        var bottleneck = BottleneckEngine.Analyze(run);
        cards.Add(new("What limits it: " + bottleneck.Diagnosis, $"Confidence: {bottleneck.Confidence}. {string.Join(" ", bottleneck.Reasoning)}", bottleneck.Limiter is Limiter.None or Limiter.Insufficient ? CardStatus.Good : CardStatus.Review));
        foreach (var note in run.Notes) cards.Add(new("Note", note, CardStatus.Unknown));
        string report = PerformanceRecorder.Report(run);
        if (baseline is not null) {
            var before = BottleneckEngine.Measurement(baseline); var after = BottleneckEngine.Measurement(run);
            report = "COMPARISON WITH BASELINE\r\n" + PerformanceComparison.Describe(PerformanceComparison.Outcome(before, after)) + "\r\n" + PerformanceComparison.Table(before, after) + "\r\n\r\n" + report;
        }
        return Diagnosis.From(report, cards, run.Frames is { } fr ? $"{fr.AverageFps:0} FPS on average; {bottleneck.Diagnosis.TrimEnd('.')}" : bottleneck.Diagnosis);
    }

    private void Compare()
    {
        if (state.Latest is null || state.Baseline is null) { Output.Text = "Measure a baseline, choose Use as baseline, make one change, then measure again the same way."; return; }
        if (ReferenceEquals(state.Latest, state.Baseline)) { Output.Text = "The latest measurement is the baseline. Measure again after your change."; return; }
        var before = BottleneckEngine.Measurement(state.Baseline); var after = BottleneckEngine.Measurement(state.Latest);
        var outcome = PerformanceComparison.Outcome(before, after);
        var changes = ChangesBetween(state.Baseline, state.Latest);
        Output.Text = PerformanceComparison.Describe(outcome) + "\r\n\r\n" + PerformanceComparison.Table(before, after) +
            "\r\n\r\nChanges made in between (from Recovery):\r\n" + (changes.Count == 0 ? "none recorded" : string.Join("\r\n", changes.Select(c => $"• {c.Kind}: {c.Target}: {c.Before} → {c.After} ({c.Status})"))) +
            "\r\n\r\nSave to Performance sessions keeps this comparison. To undo the changes, use Recovery or the session's Restore settings.";
    }
    private static IReadOnlyList<SettingChange> ChangesBetween(MonitorRun baseline, MonitorRun latest)
    {
        try { return WindowsSettings.Journal().Read().Where(c => Navigation.IsPerformanceChange(c.Kind) && c.Status == "Applied" && c.At >= baseline.Ended && c.At <= latest.Started).ToArray(); }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
    }
    private void SaveSession()
    {
        if (state.Latest is not { } latest) { Output.Text = "Measure first."; return; }
        bool compared = state.Baseline is not null && !ReferenceEquals(state.Baseline, latest);
        var before = BottleneckEngine.Measurement(compared ? state.Baseline! : latest); var after = compared ? BottleneckEngine.Measurement(latest) : null;
        var changes = compared ? ChangesBetween(state.Baseline!, latest).Select(c => c.Id).ToArray() : [];
        var outcome = PerformanceComparison.Outcome(before, after);
        var name = PerformanceSessionStore.DefaultName(latest.Target is { } t ? (compared ? "Comparison: " : "Measurement: ") + t : compared ? "Comparison" : "Measurement", latest.Started);
        try {
            PerformanceSessionsPanel.Store.Add(new PerformanceSession(Guid.NewGuid(), name, DateTimeOffset.UtcNow, before, changes, after, outcome, BottleneckEngine.Analyze(latest).Diagnosis));
            Output.Text = "Saved to History → Performance sessions.\r\n\r\n" + PerformanceComparison.Describe(outcome) + "\r\n\r\n" + PerformanceComparison.Table(before, after);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Output.Text = "Couldn't save the session: " + ex.Message; }
    }
    private void SaveFile()
    {
        if (state.Latest is null) { Output.Text = "Measure first."; return; }
        using var picker = new SaveFileDialog { Filter = "Hanki measurement|*.json", FileName = "Hanki-measurement-" + state.Latest.Started.ToLocalTime().ToString("yyyyMMdd-HHmmss") + ".json", OverwritePrompt = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(picker.FileName, JsonSerializer.Serialize(state.Latest, LabState.Json)); Output.Text = "Saved. It contains measurements and program names from this PC; review before sharing."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Output.Text = ex.Message; }
    }
    private void LoadFile()
    {
        using var picker = new OpenFileDialog { Filter = "Hanki measurement|*.json" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try {
            if (new FileInfo(picker.FileName).Length > 50_000_000) throw new IOException("The file is too large.");
            var run = JsonSerializer.Deserialize<MonitorRun>(File.ReadAllText(picker.FileName), LabState.Json) ?? throw new IOException("Not a Hanki measurement.");
            if (run.Samples is null || run.Samples.Count > 100_000 || run.Notes is null) throw new IOException("Not a Hanki measurement.");
            state.Baseline = run;
            Output.Text = $"Loaded a measurement from {run.Started.ToLocalTime():g} as the baseline.";
        } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { Output.Text = "Couldn't load it: " + ex.Message; }
    }
}

/// <summary>Performance Lab → Bottleneck Analyzer: what most likely limits the latest measurement, with evidence (HANKI-PERF-310/311, HANKI-GAME-206).</summary>
public sealed class BottleneckPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    private readonly LabState state;
    internal BottleneckPanel(LabState state) : base("Explains what most likely limits performance in your latest Monitor measurement: processor, graphics, video memory, system memory, disk, temperature or background programs. It shows the measurements behind each conclusion and says so when the evidence isn't enough.")
    {
        this.state = state;
        Button("Analyze latest measurement", () => { if (state.Latest is null) { Output.Text = "Measure first in the Monitor tab, ideally while your game runs."; return; } Show(state.Latest); });
        Button("Measure 60 s and analyze", async () => await Run(async token => {
            var run = await PerformanceRecorder.Record(60, null, new Progress<string>(t => { if (IsBusy) Output.Text = t; }), token);
            state.Latest = run;
            return Diagnose(run);
        }));
    }
    private void Show(MonitorRun run) { var d = Diagnose(run); Output.Text = d.Report; }
    internal static Diagnosis Diagnose(MonitorRun run)
    {
        var b = BottleneckEngine.Analyze(run);
        var text = new StringBuilder($"{b.Diagnosis}\r\nConfidence: {b.Confidence}\r\n\r\nWHAT WAS MEASURED\r\n");
        foreach (var o in b.Observed) text.AppendLine("• " + o);
        text.AppendLine("\r\nWHY");
        foreach (var r in b.Reasoning) text.AppendLine("• " + r);
        text.AppendLine("\r\nWHAT TO DO");
        foreach (var r in b.Recommendations) text.AppendLine($"• {r.Action}\r\n  Benefit: {r.Benefit} Downside: {r.Downside}\r\n  Because: {r.Evidence}");
        var cards = new List<ResultCard> { new(b.Diagnosis, $"Confidence: {b.Confidence}. " + string.Join(" ", b.Reasoning), b.Limiter is Limiter.None or Limiter.Insufficient ? CardStatus.Good : CardStatus.Review) };
        cards.AddRange(b.Recommendations.Select(r => new ResultCard(r.Action, $"{r.Benefit} Downside: {r.Downside}", CardStatus.Info)));
        return Diagnosis.From(text + "\r\n" + PerformanceRecorder.Report(run), cards, b.Diagnosis);
    }
}

/// <summary>Performance Lab → Stutter Diagnostics: frame-time spikes and background activity at the same moments (HANKI-GAME-209, HANKI-PERF-309).</summary>
public sealed class StutterPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    private readonly LabState state;
    internal StutterPanel(LabState state) : base("Looks at your latest Monitor measurement of a game for frame-time spikes, and what happened at the same moments: CPU spikes, GPU load drops, memory or disk pressure, heat, and background programs. A match is a possibility, not proof. Hanki never closes programs or clears caches for you.")
    {
        this.state = state;
        Button("Analyze latest measurement", () => {
            if (state.Latest is null) { Output.Text = "Measure a game first in the Monitor tab (choose the game in the list)."; return; }
            Output.Text = "STUTTER\r\n" + string.Join("\r\n", BottleneckEngine.Stutter(state.Latest).Select(l => "• " + l)) +
                "\r\n\r\nBACKGROUND ACTIVITY WHILE THE PC WAS BUSY\r\n" + string.Join("\r\n", BottleneckEngine.Interference(state.Latest).Select(l => "• " + l));
        });
    }
}
