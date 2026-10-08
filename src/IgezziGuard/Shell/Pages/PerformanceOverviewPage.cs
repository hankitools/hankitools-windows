using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Application = System.Windows.Application;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Button = System.Windows.Controls.Button;

namespace IgezziGuard.Shell;

/// <summary>Tune my PC (the Performance landing): the tuning flow first, what Hanki deliberately won't do, then the detailed tools as cards.</summary>
internal sealed class PerformanceOverviewPage : NativePage
{
    private readonly TunePage tune;
    internal TunePage Tune => tune;

    internal PerformanceOverviewPage(IShellServices shell)
    {
        tune = new TunePage(shell);
        var root = new StackPanel { Margin = new Thickness(24, 4, 24, 28) };
        root.Children.Add(tune);

        // HANKI-GAME-211 / HANKI-PERF-314: what Hanki deliberately won't do, and why; one click away.
        var refused = new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(22, 16, 22, 16), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8),
            Child = UiKit.Text(string.Join("\n", Guardrails.NotRecommended.Select(g => $"•  {g.Tweak}: {g.Why}")), 13.5, UiKit.Res("TextMuted"), wrap: true) };
        var toggle = Buttons.Quiet($"Tweaks Hanki won't make ({Guardrails.NotRecommended.Count})"); toggle.Margin = new Thickness(0, 10, 0, 6); toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Click += (_, _) => { bool show = refused.Visibility != Visibility.Visible; refused.Visibility = show ? Visibility.Visible : Visibility.Collapsed; toggle.Content = show ? "Hide the tweaks Hanki won't make" : $"Tweaks Hanki won't make ({Guardrails.NotRecommended.Count})"; };
        root.Children.Add(toggle); root.Children.Add(refused);

        root.Children.Add(new TextBlock { Text = "More tools", Style = (Style)Application.Current.FindResource("SectionHeading") });
        var tools = new UniformGrid { Columns = 3 };
        tools.SizeChanged += (_, e) => tools.Columns = e.NewSize.Width >= 900 ? 3 : e.NewSize.Width >= 560 ? 2 : 1;
        foreach (var item in Navigation.Tools(ProductArea.Performance)) {
            var page = item.Page;
            var card = UiKit.ToolCard(item.Icon, Navigation.Title(item), item.Introduction, () => shell.Navigate(page), "AccentPerformance");
            card.Margin = new Thickness(0, 0, 14, 14); tools.Children.Add(card);
        }
        root.Children.Add(tools);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
    }

    internal override void OnShown() => _ = tune.OnShownAsync();
}
