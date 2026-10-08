using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;

namespace IgezziGuard.Shell;

/// <summary>Gaming → Overview: a read-only check of the gaming setup, then a reviewed set of recommendations for a goal.</summary>
internal sealed class GamingOverviewView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly GamingState state;
    private readonly ComboBox goal = new() { Width = 190, Margin = new Thickness(8, 0, 8, 8) };
    private readonly Button scan = Buttons.Primary("Scan gaming setup"), review = Buttons.Secondary("Review recommendations"), cancel = Buttons.Secondary("Cancel"), settings = Buttons.Quiet("Windows Graphics settings");
    private readonly TextBlock status = UiKit.Text("Ready when you are", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly DiagnosisView view = new();
    private CancellationTokenSource? running;
    internal DiagnosisView Result => view;

    internal GamingOverviewView(IShellServices shell, GamingState state)
    {
        this.shell = shell; this.state = state;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        root.Children.Add(UiKit.Text("A read-only check of your gaming setup: display refresh rate, which GPU apps use, Windows Game Mode and power settings, NVIDIA driver settings, and overlays or background programs that can cost frames. Scanning changes nothing; every recommendation is reviewed before it is applied and can be undone in Recovery.", 14, UiKit.Res("TextMuted"), wrap: true));
        foreach (var g in Enum.GetValues<GamingGoal>()) goal.Items.Add(GamingProfiles.Name(g)); goal.SelectedIndex = 0;
        System.Windows.Automation.AutomationProperties.SetName(goal, "Optimization goal");
        var label = UiKit.Text("Goal", 13.5, UiKit.Res("TextMuted")); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(8, 0, 0, 8);
        var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 4) }; bar.Children.Add(scan); bar.Children.Add(cancel); bar.Children.Add(label); bar.Children.Add(goal); bar.Children.Add(review); bar.Children.Add(settings);
        root.Children.Add(bar); root.Children.Add(status); view.Margin = new Thickness(0, 6, 0, 0); root.Children.Add(view); Content = root; cancel.IsEnabled = false;
        scan.Click += async (_, _) => await Scan();
        cancel.Click += (_, _) => running?.Cancel();
        review.Click += async (_, _) => await Review();
        settings.Click += (_, _) => HealthSettings.Open(shell.DialogOwner, "ms-settings:display-advancedgraphics", "Settings → System → Display → Graphics");
        goal.SelectionChanged += (_, _) => { if (running is null && state.Graphics is not null) status.Text = GamingProfiles.Describe(Goal) + "\n\nChoose Review recommendations to see what this goal would change."; };
    }

    private GamingGoal Goal => Enum.GetValues<GamingGoal>()[Math.Max(0, goal.SelectedIndex)];

    private async Task Scan()
    {
        if (running is not null) return;
        using var cts = new CancellationTokenSource(); running = cts; scan.IsEnabled = review.IsEnabled = false; cancel.IsEnabled = true;
        using var ticket = shell.Tasks.Begin("Gaming", cts.Cancel);
        view.Clear(); status.Text = "Collecting results. Your previous report remains available after this operation.";
        try {
            await Task.Run(state.Collect, cts.Token);
            try { PerformanceStatus.History.Add(new DiagnosticScan(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 1, false, state.Findings)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            var cards = state.Findings.Select(f => new ResultCard(f.Title, f.Explanation + (f.Severity is FindingSeverity.Warning or FindingSeverity.Critical
                    ? $"\r\nNow: {f.Metadata.GetValueOrDefault("current")} · Recommended: {f.Metadata.GetValueOrDefault("recommended")}" : ""), GamingState.Status(f),
                GamingHealth.CanApply(f) ? "Review change" : f.Metadata.GetValueOrDefault("settings") is { } uri ? "Open settings" : null,
                GamingHealth.CanApply(f) ? () => Application.Current.Dispatcher.BeginInvoke(() => ReviewOnly(f)) : f.Metadata.GetValueOrDefault("settings") is { } u ? () => HealthSettings.Open(shell.DialogOwner, u, f.Title) : null)).ToList();
            int opportunities = state.Findings.Count(f => f.Severity is FindingSeverity.Warning or FindingSeverity.Critical);
            var report = "Gaming Health Scan • " + DateTimeOffset.Now.ToString("g") + "\r\nRead-only; nothing was changed.\r\n\r\n" + state.Hardware() + "\r\n" +
                string.Join("\r\n\r\n", state.Findings.Select(f => $"{f.Title} · {f.Severity}\r\n{f.Explanation}\r\n{f.Evidence}"));
            view.Show(Diagnosis.From(report, cards, opportunities == 0 ? "No gaming configuration problems found" : $"{opportunities} optimization {(opportunities == 1 ? "opportunity" : "opportunities")} found"));
            status.Text = "Report ready · " + DateTime.Now.ToString("t");
        }
        catch (OperationCanceledException) { status.Text = "Cancelled. Nothing on your PC was changed; incomplete results were discarded. Run it again when you're ready."; }
        catch (Exception ex) { status.Text = "Operation stopped: " + ex.Message; }
        finally { running = null; scan.IsEnabled = review.IsEnabled = true; cancel.IsEnabled = false; }
    }

    private void ReviewOnly(DiagnosticResult finding)
    {
        if (state.Graphics is null) return;
        _ = ChangeReview.ReviewAndApply(shell.DialogOwner, GamingProfiles.Propose(GamingGoal.Balanced, state.Graphics, [finding], null), finding.Title, null, text => status.Text = text.Replace("\r\n", "\n"));
    }

    private async Task Review()
    {
        if (state.Graphics is null) { status.Text = "Scan your gaming setup first; recommendations come from what the scan finds."; return; }
        var changes = GamingProfiles.Propose(Goal, state.Graphics, state.Findings, null);
        await ChangeReview.ReviewAndApply(shell.DialogOwner, changes, GamingProfiles.Name(Goal) + " for this PC", "Gaming setup: " + GamingProfiles.Name(Goal), text => status.Text = text.Replace("\r\n", "\n"));
    }
}

/// <summary>Gaming: the overview is native; the game list and the NVIDIA and AMD settings are still the existing pages inside it.</summary>
internal sealed class GamingPage : NativePage
{
    private readonly SubTabs tabs = new();
    internal GamingOverviewView? Overview => tabs.ContentOf("Overview") as GamingOverviewView;
    internal string? CurrentTab => tabs.Current;
    internal IEnumerable<string> TabKeys => tabs.Keys;

    internal GamingPage(IShellServices shell)
    {
        tabs.Add("Overview", "Overview", () => new GamingOverviewView(shell, LegacyWorkspace.Gaming));
        tabs.Add("Games", "Games", () => GameTools.Games(shell, LegacyWorkspace.Gaming));
        tabs.Add("NVIDIA", "NVIDIA", () => GameTools.NvidiaPage(shell, LegacyWorkspace.Gaming));
        tabs.Add("AMD Radeon", "AMD Radeon", () => GameTools.AmdPage(shell, LegacyWorkspace.Gaming));
        Content = tabs;
    }
    internal override void OnShown() { if (tabs.Current is null) tabs.Select("Overview"); }
    internal override void OnRoute(IReadOnlyList<string> path) { if (path.Count > 1 && tabs.Keys.Contains(path[1])) tabs.Select(path[1]); }
}
