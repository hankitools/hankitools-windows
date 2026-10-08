using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;

namespace IgezziGuard.Shell;

/// <summary>Fix my PC: one scan across Windows, the last scan at a glance, and the other system tools as cards.</summary>
internal sealed class FixLandingPage : NativePage
{
    private readonly IShellServices shell;
    private readonly DiagnosticHistory history = new(Path.Combine(SecurityPaths.Root, "diagnostic-history.json"));
    private readonly StackPanel lastScan = new();

    internal FixLandingPage(IShellServices shell)
    {
        this.shell = shell;
        var root = new StackPanel { Margin = new Thickness(24, 4, 24, 28) };
        root.Children.Add(BuildHero());
        root.Children.Add(new TextBlock { Text = "More tools", Style = (Style)Application.Current.FindResource("SectionHeading") });
        var tools = new UniformGrid { Columns = 3 };
        tools.SizeChanged += (_, e) => tools.Columns = e.NewSize.Width >= 900 ? 3 : e.NewSize.Width >= 560 ? 2 : 1;
        foreach (var item in Navigation.Tools(ProductArea.System)) {
            var page = item.Page;
            var card = UiKit.ToolCard(item.Icon, Navigation.Title(item), item.Introduction, () => shell.Navigate(page));
            card.Margin = new Thickness(0, 0, 14, 14); tools.Children.Add(card);
        }
        root.Children.Add(tools);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
    }

    private UIElement BuildHero()
    {
        var grid = new Grid { Margin = new Thickness(6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });
        var pitch = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        pitch.Children.Add(new TextBlock { Text = "FIX MY PC", Style = (Style)Application.Current.FindResource("Eyebrow") });
        var headline = UiKit.Text("Check your PC in one pass", 26, weight: FontWeights.SemiBold); headline.Margin = new Thickness(0, 6, 0, 8); pitch.Children.Add(headline);
        pitch.Children.Add(UiKit.Text("Read-only. Nothing changes until you approve a repair, and nothing is uploaded.", 15, UiKit.Res("TextMuted"), wrap: true));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
        var start = new Button { Style = (Style)Application.Current.FindResource("PrimaryButton"), Content = "Scan my PC", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        start.Click += (_, _) => shell.StartFixMyPc();
        var view = new Button { Style = (Style)Application.Current.FindResource("SecondaryButton"), Content = "View results" };
        view.Click += (_, _) => shell.Navigate("Fix My PC");
        buttons.Children.Add(start); buttons.Children.Add(view); pitch.Children.Add(buttons);
        var divider = new Border { Style = (Style)Application.Current.FindResource("Card"), BorderThickness = new Thickness(1, 0, 0, 0), Background = System.Windows.Media.Brushes.Transparent, CornerRadius = new CornerRadius(0), Padding = new Thickness(22, 2, 0, 2), Child = lastScan };
        divider.BorderBrush = UiKit.Res("Border");
        Grid.SetColumn(divider, 1);
        grid.Children.Add(pitch); grid.Children.Add(divider);
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(26, 22, 26, 22), Child = grid };
    }

    internal override void OnShown() => RefreshLastScan();

    private void RefreshLastScan()
    {
        lastScan.Children.Clear();
        lastScan.Children.Add(new TextBlock { Text = "LAST SCAN", Style = (Style)Application.Current.FindResource("Eyebrow"), Foreground = UiKit.Res("TextMuted") });
        DiagnosticScan? scan = null; string? problem = null;
        try { scan = history.Read().OrderByDescending(s => s.Ended).FirstOrDefault(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { problem = "Saved scan history could not be read. Original files were preserved."; }
        if (scan is null) {
            var title = UiKit.Text(problem is null ? "No scans yet" : "History unavailable", 19, weight: FontWeights.SemiBold); title.Margin = new Thickness(0, 6, 0, 4);
            lastScan.Children.Add(title);
            lastScan.Children.Add(UiKit.Text(problem ?? "Your results will appear here after the first scan.", 13.5, UiKit.Res("TextMuted"), wrap: true));
            return;
        }
        var ended = scan.Ended.ToLocalTime();
        string when = ended.Date == DateTime.Today ? "Today, " + ended.ToString("t") : ended.Date == DateTime.Today.AddDays(-1) ? "Yesterday, " + ended.ToString("t") : ended.ToString("g");
        var heading = UiKit.Text(when, 19, weight: FontWeights.SemiBold); heading.Margin = new Thickness(0, 6, 0, 2);
        lastScan.Children.Add(heading);
        lastScan.Children.Add(UiKit.Text((scan.Cancelled ? "Cancelled · " : "") + $"{scan.CompletedModules} of {scan.PlannedModules} checks finished", 13.5, UiKit.Res("TextMuted")));
        var chips = ResultChips.Build(scan.Results); chips.Margin = new Thickness(0, 10, 0, 0); lastScan.Children.Add(chips);
    }
}
