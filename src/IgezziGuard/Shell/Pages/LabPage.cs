using System.Windows;
using Application = System.Windows.Application;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using System.Windows.Controls;

namespace IgezziGuard.Shell;

/// <summary>Performance Lab: measure while you play, compare runs, find bottlenecks and stutter.</summary>
internal sealed class LabPage : NativePage
{
    private readonly SubTabs tabs = new();
    internal MonitorView? Monitor => tabs.ContentOf("Monitor") as MonitorView;
    internal BottleneckView? Bottleneck => tabs.ContentOf("Bottleneck Analyzer") as BottleneckView;
    internal StutterView? Stutter => tabs.ContentOf("Stutter Diagnostics") as StutterView;
    internal string? CurrentTab => tabs.Current;
    internal IEnumerable<string> TabKeys => tabs.Keys;

    internal LabPage(IShellServices shell)
    {
        var state = LegacyWorkspace.Lab;
        tabs.Add("Monitor", "Monitor", () => new MonitorView(shell, state));
        tabs.Add("Comparisons", "Comparisons", () => new ReportView(shell, "Comparisons", "Measures how busy the processor is and how full memory gets over 30 seconds. Keep doing what normally feels slow while it runs. The first run becomes your baseline, and later runs are compared with it, so you can see whether a change helped. Nothing is changed or uploaded. Disk and GPU are measured in the Monitor tab.", MemoryActions.Comparisons()));
        tabs.Add("Bottleneck Analyzer", "Bottleneck Analyzer", () => new BottleneckView(shell, state));
        tabs.Add("Stutter Diagnostics", "Stutter Diagnostics", () => new StutterView(state));
        tabs.Add("Benchmarks", "Benchmarks", () => Planned("Benchmarks", "Repeatable measurements so before/after comparisons are fair."));
        tabs.Add("Advanced Tuning", "Advanced Tuning", () => Planned("Advanced Tuning", "Vendor-supported GPU auto-tuning, kept separate from normal optimization: it is never started by a profile or by Fix My PC, always asks for explicit confirmation, and saves the starting configuration first."));
        Content = tabs;
    }

    private static UIElement Planned(string title, string text)
    {
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text(title, 19, weight: FontWeights.SemiBold));
        var body = UiKit.Text(text + " This part is still being built; nothing runs by opening it.", 14, UiKit.Res("TextMuted"), wrap: true); body.Margin = new Thickness(0, 6, 0, 0); stack.Children.Add(body);
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(24), Margin = new Thickness(24, 0, 24, 0), VerticalAlignment = VerticalAlignment.Top, Child = stack };
    }

    internal override void OnShown() { if (tabs.Current is null) tabs.Select("Monitor"); }
    internal override void OnRoute(IReadOnlyList<string> path) { if (path.Count > 1 && tabs.Keys.Contains(path[1])) tabs.Select(path[1]); }
    internal void Select(string key) => tabs.Select(key);
}
