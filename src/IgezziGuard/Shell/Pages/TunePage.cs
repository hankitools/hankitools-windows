using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;

namespace IgezziGuard.Shell;

/// <summary>
/// Tune my PC: what do you want today, read the current settings, show a plan with before and after, then review and apply only what
/// you approve. Building the plan changes nothing; every change goes through the review drawer and Recovery.
/// </summary>
internal sealed class TunePage : StackPanel
{
    private const string SyncHintText = "It's on the monitor's box, in its specifications or on-screen menu: G-SYNC, G-SYNC Compatible, FreeSync or Adaptive-Sync.";
    private readonly IShellServices shell;
    private readonly WrapPanel scenarios = new(), syncChoices = new();
    private readonly List<(RadioButton Radio, TuneScenario Scenario)> scenarioRadios = [];
    private readonly List<(RadioButton Radio, AdaptiveSync Sync)> syncRadios = [];
    private readonly StackPanel syncRow = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock syncHint = UiKit.Text(SyncHintText, 13, UiKit.Res("TextMuted"), wrap: true);
    private readonly Button scan = Buttons.Primary("Scan and suggest changes");
    private readonly TextBlock status = UiKit.Text("Choose one, then scan. Hanki reads your settings and shows every change before anything happens.", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly Border resultCard = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel result = new();
    private TunePlan? plan;
    private bool busy, detected;

    internal TunePlan? Plan => plan;
    internal int ScenarioCount => scenarioRadios.Count;
    internal bool PlanVisible => resultCard.Visibility == Visibility.Visible;

    internal TunePage(IShellServices shell)
    {
        this.shell = shell;
        var hero = new StackPanel { Margin = new Thickness(8) };
        hero.Children.Add(new TextBlock { Text = "TUNE MY PC", Style = (Style)Application.Current.FindResource("Eyebrow"), Foreground = UiKit.Res("AccentPerformance") });
        var headline = UiKit.Text("How do you want to tune your PC today?", 24, weight: FontWeights.SemiBold, wrap: true); headline.Margin = new Thickness(0, 6, 0, 12); hero.Children.Add(headline);
        foreach (var scenario in Enum.GetValues<TuneScenario>()) {
            var content = new StackPanel { Width = 230 };
            content.Children.Add(UiKit.Text(TunePlanner.Name(scenario), 15, weight: FontWeights.SemiBold));
            var text = UiKit.Text(TunePlanner.Describe(scenario), 13, UiKit.Res("TextMuted"), wrap: true); text.Margin = new Thickness(0, 3, 0, 0); content.Children.Add(text);
            var radio = new RadioButton { Style = (Style)Application.Current.FindResource("ChoiceCard"), GroupName = "tunescenario", Content = content, Tag = UiKit.Res("AccentPerformance"), Margin = new Thickness(0, 0, 12, 12) };
            System.Windows.Automation.AutomationProperties.SetName(radio, TunePlanner.Name(scenario)); System.Windows.Automation.AutomationProperties.SetHelpText(radio, TunePlanner.Describe(scenario));
            var captured = scenario;
            radio.Checked += (_, _) => syncRow.Visibility = TunePlanner.IsGaming(captured) ? Visibility.Visible : Visibility.Collapsed;
            scenarioRadios.Add((radio, scenario)); scenarios.Children.Add(radio);
        }
        hero.Children.Add(scenarios);
        var question = UiKit.Text("Does your display have G-SYNC or FreeSync?", 15, weight: FontWeights.SemiBold); syncRow.Children.Add(question);
        foreach (var (label, value, hint) in new[] { ("Yes", AdaptiveSync.Yes, "V-Sync and caps set for adaptive sync"), ("No", AdaptiveSync.No, "Settings for a fixed refresh rate"), ("Not sure", AdaptiveSync.NotSure, "Hanki leaves V-Sync and frame caps alone") }) {
            var content = new StackPanel { Width = 190 };
            content.Children.Add(UiKit.Text(label, 14.5, weight: FontWeights.SemiBold));
            var h = UiKit.Text(hint, 12.5, UiKit.Res("TextMuted"), wrap: true); h.Margin = new Thickness(0, 2, 0, 0); content.Children.Add(h);
            var radio = new RadioButton { Style = (Style)Application.Current.FindResource("ChoiceCard"), GroupName = "tunesync", Content = content, Tag = UiKit.Res("AccentPerformance"), IsChecked = value == AdaptiveSync.NotSure, Margin = new Thickness(0, 8, 10, 0) };
            System.Windows.Automation.AutomationProperties.SetName(radio, label);
            syncRadios.Add((radio, value)); syncChoices.Children.Add(radio);
        }
        syncRow.Children.Add(syncChoices); syncHint.Margin = new Thickness(0, 8, 0, 0); syncRow.Children.Add(syncHint);
        hero.Children.Add(syncRow);
        scan.Margin = new Thickness(0, 14, 0, 4); scan.HorizontalAlignment = HorizontalAlignment.Left; hero.Children.Add(scan); hero.Children.Add(status);
        Children.Add(new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(20, 18, 20, 12), Child = hero });
        resultCard.Style = (Style)Application.Current.FindResource("Card"); resultCard.Padding = new Thickness(24, 20, 24, 22); resultCard.Margin = new Thickness(0, 14, 0, 0); resultCard.Child = result;
        Children.Add(resultCard);
        scan.Click += async (_, _) => await Scan();
    }

    private TuneScenario? Scenario => scenarioRadios.FirstOrDefault(r => r.Radio.IsChecked == true) is { Radio: not null } r ? r.Scenario : null;
    private AdaptiveSync Sync => syncRadios.FirstOrDefault(r => r.Radio.IsChecked == true) is { Radio: not null } r ? r.Sync : AdaptiveSync.NotSure;

    /// <summary>Called when the page is shown: answers the G-SYNC question from NVIDIA's driver when it can (read-only).</summary>
    internal async Task OnShownAsync()
    {
        if (detected) return; detected = true;
        if (System.Windows.Forms.Screen.PrimaryScreen?.DeviceName is not { } display) return;
        AdaptiveSyncStatus? found;
        try { found = await Task.Run(() => Nvidia.AdaptiveSync(display)); } catch (Exception ex) when (ex is NvidiaException or IOException or InvalidOperationException) { return; }
        if (found is null) return;
        if (found.On && Sync == AdaptiveSync.NotSure) foreach (var (radio, value) in syncRadios) radio.IsChecked = value == AdaptiveSync.Yes;
        syncHint.Text = found.On ? "NVIDIA's driver reports G-SYNC switched on for this display." :
            found.Supported ? "Your display supports G-SYNC, but it's off in the NVIDIA driver. Choose Yes if you'll switch it on; the plan shows how." : SyncHintText;
    }

    /// <summary>For the UI check: picks a scenario as a click would.</summary>
    internal void Choose(TuneScenario scenario) { foreach (var (radio, s) in scenarioRadios) radio.IsChecked = s == scenario; }

    private async Task Scan()
    {
        if (busy) return;
        if (Scenario is not { } scenario) { status.Text = "Choose how you want to tune your PC first."; return; }
        var chosenSync = Sync;
        busy = true; scan.IsEnabled = false;
        using var cts = new CancellationTokenSource(); using var ticket = shell.Tasks.Begin("Tune my PC", cts.Cancel);
        status.Text = "Reading display, Windows, graphics driver, processor, memory, storage and network settings. Nothing is changed…";
        try {
            var inputs = await TunePanel.Collect();
            plan = TunePlanner.Plan(scenario, TunePlanner.IsGaming(scenario) ? TunePlanner.Resolve(chosenSync, inputs.Sync) : AdaptiveSync.NotSure, inputs);
            status.Text = $"Scanned {DateTime.Now:t}. Nothing was changed.";
            Show(plan);
        } catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) {
            status.Text = "The scan couldn't finish: " + ex.Message;
        } finally { busy = false; scan.IsEnabled = true; }
    }

    /// <summary>For the UI check: shows a plan built from fixed example data. Nothing is read or changed.</summary>
    internal void Preview(TunePlan example)
    {
        Choose(example.Scenario); foreach (var (radio, value) in syncRadios) radio.IsChecked = value == example.Sync;
        status.Text = "Example plan (UI check). Nothing was read or changed."; Show(example);
    }

    private void Show(TunePlan p)
    {
        plan = p; result.Children.Clear();
        int changes = p.HankiChanges, steps = p.Steps;
        var title = UiKit.Text(changes == 0 && steps == 0 ? "Your PC is already set up for " + TunePlanner.Name(p.Scenario) :
            $"{changes} {(changes == 1 ? "change" : "changes")} Hanki can make" + (steps > 0 ? $" · {steps} {(steps == 1 ? "step" : "steps")} for you" : ""), 20, weight: FontWeights.SemiBold, wrap: true);
        System.Windows.Automation.AutomationProperties.SetName(title, title.Text); result.Children.Add(title);
        var intro = UiKit.Text($"For {TunePlanner.Name(p.Scenario)}. You pick which changes to apply next; each one is saved in Recovery first so you can undo it.", 14, UiKit.Res("TextMuted"), wrap: true); intro.Margin = new Thickness(0, 4, 0, 6); result.Children.Add(intro);
        foreach (var group in p.Items.GroupBy(i => i.Area).OrderBy(g => g.Key)) {
            var heading = new TextBlock { Text = TunePanel.AreaName(group.Key).ToUpperInvariant(), Style = (Style)Application.Current.FindResource("Eyebrow"), Foreground = UiKit.Res("AccentPerformance"), Margin = new Thickness(0, 16, 0, 6) };
            result.Children.Add(heading);
            foreach (var item in group) {
                var c = item.Change;
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var mark = new TextBlock { Text = c.HankiApplies ? "●" : "○", Foreground = c.HankiApplies ? UiKit.Res("AccentPerformance") : UiKit.Res("TextMuted"), Width = 22, FontSize = 13, Margin = new Thickness(0, 3, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(mark, Dock.Left); row.Children.Add(mark);
                string line = c.HankiApplies ? $"{c.Setting}:  {c.Current}  →  {c.Recommended}" : $"{c.Setting}:  {c.Recommended}";
                row.Children.Add(UiKit.Text(line + (c.Optional ? "   (optional)" : "") + (c.HankiApplies ? "" : "   · you do this"), 14.5, wrap: true));
                result.Children.Add(row);
            }
        }
        var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        var apply = Buttons.Primary(changes > 0 ? "Review and apply" : "Show the steps"); apply.IsEnabled = p.Items.Count > 0;
        var goodToggle = Buttons.Quiet($"What's already right ({p.AlreadyGood.Count})"); goodToggle.IsEnabled = p.AlreadyGood.Count > 0;
        buttons.Children.Add(apply); buttons.Children.Add(goodToggle); result.Children.Add(buttons);
        var good = UiKit.Text(string.Join("\n", p.AlreadyGood.Select(g => $"✓  {TunePanel.AreaName(g.Area)} · {g.Setting}: {g.Current}")), 14, UiKit.Res("TextMuted"), wrap: true);
        good.Visibility = Visibility.Collapsed; good.Margin = new Thickness(0, 8, 0, 0); result.Children.Add(good);
        goodToggle.Click += (_, _) => { good.Visibility = good.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; goodToggle.Content = good.Visibility == Visibility.Visible ? "Hide what's already right" : $"What's already right ({p.AlreadyGood.Count})"; };
        apply.Click += async (_, _) => await Apply(p);
        resultCard.Visibility = Visibility.Visible;
    }

    private async Task Apply(TunePlan p)
    {
        if (busy) return;
        busy = true;
        using var cts = new CancellationTokenSource(); using var ticket = shell.Tasks.Begin("Tune my PC", cts.Cancel);
        try {
            string name = TunePlanner.Name(p.Scenario);
            await ChangeReview.ReviewAndApply(shell.DialogOwner, p.Changes, $"Tune my PC: {name}", $"Tune my PC: {name}", text => {
                result.Children.Clear();
                result.Children.Add(UiKit.Text(text.Replace("\r\n", "\n") + "\n\nScan again to see the new state.", 14, wrap: true));
            });
        } finally { busy = false; }
    }
}
