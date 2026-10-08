using System.Windows;
using Application = System.Windows.Application;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace IgezziGuard.Shell;

/// <summary>Performance Lab → Bottleneck Analyzer: what most likely limits the latest measurement, with the evidence behind it.</summary>
internal sealed class BottleneckView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly LabState state;
    private readonly Button analyze = Buttons.Primary("Analyze latest measurement"), measure = Buttons.Secondary("Measure 60 s and analyze"), cancel = Buttons.Secondary("Cancel");
    private readonly TextBlock status = UiKit.Text("", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly DiagnosisView view = new();
    private CancellationTokenSource? running;
    internal DiagnosisView Result => view;

    internal BottleneckView(IShellServices shell, LabState state)
    {
        this.shell = shell; this.state = state;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        root.Children.Add(UiKit.Text("Explains what most likely limits performance in your latest Monitor measurement: processor, graphics, video memory, system memory, disk, temperature or background programs. Every conclusion shows the measurements behind it and how sure Hanki is. Nothing is changed.", 14, UiKit.Res("TextMuted"), wrap: true));
        var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 4) }; bar.Children.Add(analyze); bar.Children.Add(measure); bar.Children.Add(cancel);
        root.Children.Add(bar); root.Children.Add(status); view.Margin = new Thickness(0, 6, 0, 0); root.Children.Add(view); Content = root; cancel.IsEnabled = false;
        analyze.Click += (_, _) => { if (state.Latest is null) { status.Text = "Measure first in the Monitor tab, ideally while your game runs."; return; } status.Text = ""; view.Show(BottleneckPanel.Diagnose(state.Latest)); };
        measure.Click += async (_, _) => await Measure();
        cancel.Click += (_, _) => running?.Cancel();
    }

    private async Task Measure()
    {
        if (running is not null) return;
        using var cts = new CancellationTokenSource(); running = cts; analyze.IsEnabled = measure.IsEnabled = false; cancel.IsEnabled = true;
        using var ticket = shell.Tasks.Begin("Performance", cts.Cancel);
        view.Clear(); status.Text = "Measuring for 60 seconds…";
        try {
            var run = await Task.Run(() => PerformanceRecorder.Record(60, null, new Progress<string>(t => { if (running == cts) status.Text = t; }), cts.Token), cts.Token);
            state.Latest = run; view.Show(BottleneckPanel.Diagnose(run)); status.Text = "Report ready · " + DateTime.Now.ToString("t");
        }
        catch (OperationCanceledException) { status.Text = "Cancelled. Nothing on your PC was changed; incomplete results were discarded."; }
        catch (Exception ex) { status.Text = "Operation stopped: " + ex.Message; }
        finally { running = null; analyze.IsEnabled = measure.IsEnabled = true; cancel.IsEnabled = false; }
    }
}

/// <summary>Performance Lab → Stutter Diagnostics: frame-time spikes in the latest game measurement, and what the PC was doing at those moments.</summary>
internal sealed class StutterView : ScrollViewer
{
    private readonly LabState state;
    private readonly TextBlock status = UiKit.Text("", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly StackPanel results = new();
    internal int ItemCount { get; private set; }

    internal StutterView(LabState state)
    {
        this.state = state;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        root.Children.Add(UiKit.Text("Looks at your latest Monitor measurement of a game for frame-time spikes, and what happened at the same moments: CPU spikes, GPU load drops, memory or disk pressure, downloads and background programs. Time alignment points to clues, not proven causes.", 14, UiKit.Res("TextMuted"), wrap: true));
        var analyze = Buttons.Primary("Analyze latest measurement"); analyze.Margin = new Thickness(0, 14, 0, 8); analyze.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(analyze); root.Children.Add(status); root.Children.Add(results); Content = root;
        analyze.Click += (_, _) => Analyze();
    }

    internal void Analyze()
    {
        results.Children.Clear(); ItemCount = 0;
        if (state.Latest is null) { status.Text = "Measure a game first in the Monitor tab (choose the game in the list)."; return; }
        status.Text = "";
        Section("Stutter", BottleneckEngine.Stutter(state.Latest));
        Section("Downloads and background activity", BottleneckEngine.Interference(state.Latest));
    }

    private void Section(string heading, IEnumerable<string> lines)
    {
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text(heading, 16, weight: FontWeights.SemiBold));
        foreach (var line in lines) { var t = UiKit.Text("•  " + line, 14, UiKit.Res("TextPrimary"), wrap: true); t.Margin = new Thickness(0, 6, 0, 0); stack.Children.Add(t); ItemCount++; }
        results.Children.Add(new Border { Style = (Style)System.Windows.Application.Current.FindResource("Card"), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 10, 0, 0), Child = stack });
    }
}
