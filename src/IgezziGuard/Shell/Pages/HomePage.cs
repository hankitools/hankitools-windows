using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ListBox = System.Windows.Controls.ListBox;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using Application = System.Windows.Application;

namespace IgezziGuard.Shell;

/// <summary>Home: search for a problem or tool, the two ways into Hanki, your PC at a glance and recent activity.</summary>
internal sealed class HomePage : NativePage
{
    private readonly IShellServices shell;
    private readonly DiagnosticHistory scans = new(Path.Combine(SecurityPaths.Root, "diagnostic-history.json"));
    private readonly TextBox query = new();
    private readonly ListBox results = new();
    private readonly WrapPanel suggestions = new();
    private readonly TextBlock noMatch = UiKit.Text("Nothing matched. Try a broader word, such as network, storage or games, or press Ctrl+K to list every tool.", 14, UiKit.Res("TextMuted"), wrap: true);
    private readonly TextBlock systemStatus = new(), performanceStatus = new();
    private readonly UniformGrid heroes = new() { Columns = 2 };
    private readonly UniformGrid glance = new() { Columns = 3 };
    private readonly StackPanel activity = new();
    private readonly ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private IReadOnlyList<SearchEntry>? index;
    private DateTime glanceRead = DateTime.MinValue;
    private bool reading;

    internal HomePage(IShellServices shell)
    {
        this.shell = shell;
        var root = new StackPanel { Margin = new Thickness(24, 4, 24, 28) };
        root.Children.Add(BuildSearch());
        BuildHeroes(); root.Children.Add(heroes);
        heroes.SizeChanged += (_, e) => heroes.Columns = e.NewSize.Width >= 820 ? 2 : 1;
        root.Children.Add(Heading("Your PC at a glance"));
        glance.SizeChanged += (_, e) => glance.Columns = e.NewSize.Width >= 900 ? 3 : e.NewSize.Width >= 560 ? 2 : 1;
        root.Children.Add(glance);
        root.Children.Add(Heading("Recent activity"));
        root.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(20, 8, 16, 8), Child = activity });
        scroller.Content = root; Content = scroller;
        ShowGlance(Loading());
    }

    private static TextBlock Heading(string text) => new() { Text = text, Style = (Style)Application.Current.FindResource("SectionHeading") };

    private UIElement BuildSearch()
    {
        var box = new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(16, 0, 16, 0), Height = 54 };
        var row = new DockPanel();
        var icon = UiKit.Icon("Search", 16, UiKit.Res("TextMuted")); icon.Margin = new Thickness(0, 0, 12, 0); DockPanel.SetDock(icon, Dock.Left);
        query.Background = System.Windows.Media.Brushes.Transparent; query.BorderThickness = new Thickness(0); query.Foreground = UiKit.Res("TextPrimary"); query.CaretBrush = UiKit.Res("TextPrimary");
        query.FontSize = 17; query.VerticalContentAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(query, "Search for a problem or a tool");
        var hint = UiKit.Text("What do you need help with?  For example: slow, blue screen, Wi-Fi, FPS", 17, UiKit.Res("TextMuted")); hint.IsHitTestVisible = false; hint.VerticalAlignment = VerticalAlignment.Center; hint.Margin = new Thickness(2, 0, 0, 0);
        var field = new Grid(); field.Children.Add(query); field.Children.Add(hint);
        row.Children.Add(icon); row.Children.Add(field); box.Child = row;

        foreach (var suggestion in HomeSearch.Suggestions) {
            var chip = new Button { Style = (Style)Application.Current.FindResource("SecondaryButton"), Content = suggestion, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0), FontSize = 13 };
            System.Windows.Automation.AutomationProperties.SetName(chip, "Search for " + suggestion);
            chip.Click += (_, _) => { query.Text = suggestion; query.Focus(); query.CaretIndex = query.Text.Length; };
            suggestions.Children.Add(chip);
        }
        var tryRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 12, 0, 0) };
        var tryLabel = UiKit.Text("Try", 13, UiKit.Res("TextMuted")); tryLabel.VerticalAlignment = VerticalAlignment.Center; tryLabel.Margin = new Thickness(0, 0, 10, 0);
        tryRow.Children.Add(tryLabel); tryRow.Children.Add(suggestions);

        results.Background = System.Windows.Media.Brushes.Transparent; results.BorderThickness = new Thickness(0); results.Visibility = Visibility.Collapsed; results.Margin = new Thickness(0, 8, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(results, "Search results");
        results.ItemContainerStyle = (Style)Application.Current.FindResource("ResultItem");
        results.ItemTemplate = ResultTemplate();
        results.MouseUp += (_, _) => OpenSelected();
        noMatch.Visibility = Visibility.Collapsed; noMatch.Margin = new Thickness(4, 12, 0, 0);

        query.TextChanged += (_, _) => { hint.Visibility = query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Search(); };
        query.PreviewKeyDown += (_, e) => {
            switch (e.Key) {
                case Key.Enter: OpenSelected(); e.Handled = true; break;
                case Key.Down when results.Items.Count > 0: results.SelectedIndex = Math.Min(results.SelectedIndex + 1, results.Items.Count - 1); e.Handled = true; break;
                case Key.Up when results.Items.Count > 0: results.SelectedIndex = Math.Max(results.SelectedIndex - 1, 0); e.Handled = true; break;
                case Key.Escape: query.Clear(); e.Handled = true; break;
            }
        };
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        stack.Children.Add(box); stack.Children.Add(tryRow); stack.Children.Add(results); stack.Children.Add(noMatch);
        return stack;
    }

    private static DataTemplate ResultTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SearchEntry.Title))); title.SetValue(TextBlock.FontSizeProperty, 15.0); title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SearchEntry.Detail))); detail.SetValue(TextBlock.FontSizeProperty, 12.5); detail.SetValue(TextBlock.ForegroundProperty, UiKit.Res("TextMuted"));
        factory.AppendChild(title); factory.AppendChild(detail);
        return new DataTemplate { VisualTree = factory };
    }

    private IReadOnlyList<SearchEntry> Entries() => index ??= TroubleshootingPanel.Guides.Select((g, i) => HomeSearch.Guide(i, g.Symptom, g.Steps.Length, g.Steps.Select(x => x.Title)))
        .Concat(shell.Routes.Where(r => r.Name != "Home").Select(r => HomeSearch.Tool(r.Name, r.SearchText, Navigation.Find(r.Name)?.Introduction))).ToArray();

    private void Search()
    {
        bool any = query.Text.Trim().Length > 0;
        suggestions.Parent!.SetValue(UIElement.VisibilityProperty, any ? Visibility.Collapsed : Visibility.Visible);
        var found = any ? HomeSearch.Find(query.Text, Entries()) : [];
        results.ItemsSource = found;
        results.Visibility = found.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        noMatch.Visibility = any && found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (found.Count > 0) results.SelectedIndex = 0;
    }

    private void OpenSelected()
    {
        if (results.SelectedItem is not SearchEntry entry) return;
        if (entry.GuidedFix && int.TryParse(entry.Target.AsSpan("guide:".Length), out var guide)) shell.OpenGuide(guide);
        else shell.Routes.FirstOrDefault(r => r.Name == entry.Target)?.Open();
    }

    private Border Hero(string eyebrow, string accent, string headline, string description, TextBlock status, string primary, Action primaryAction, string secondary, Action secondaryAction)
    {
        var stack = new StackPanel { Margin = new Thickness(6) };
        var eyebrowText = new TextBlock { Text = eyebrow, Style = (Style)Application.Current.FindResource("Eyebrow"), Foreground = UiKit.Res(accent) };
        stack.Children.Add(eyebrowText);
        var title = UiKit.Text(headline, 24, weight: FontWeights.SemiBold); title.Margin = new Thickness(0, 6, 0, 6); stack.Children.Add(title);
        stack.Children.Add(UiKit.Text(description, 15, UiKit.Res("TextPrimary"), wrap: true));
        status.Style = (Style)Application.Current.FindResource("Muted"); status.FontSize = 13.5; status.Margin = new Thickness(0, 10, 0, 16); stack.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var main = new Button { Style = (Style)Application.Current.FindResource("PrimaryButton"), Content = primary, Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.SemiBold };
        main.Click += (_, _) => primaryAction();
        var other = new Button { Style = (Style)Application.Current.FindResource("QuietButton"), Content = secondary, Foreground = UiKit.Res(accent), FontWeight = FontWeights.SemiBold };
        other.Click += (_, _) => secondaryAction();
        buttons.Children.Add(main); buttons.Children.Add(other); stack.Children.Add(buttons);
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(22), Margin = new Thickness(0, 0, 14, 0), Child = stack };
    }

    private void BuildHeroes()
    {
        heroes.Children.Add(Hero("FIX MY PC", "Accent", "Something not working?", "Scan Windows and fix what's wrong, safely.", systemStatus,
            "Scan my PC", shell.StartFixMyPc, "Open Fix my PC", () => shell.Navigate("System overview")));
        heroes.Children.Add(Hero("TUNE MY PC", "AccentPerformance", "Want more from your PC?", "Tune it for gaming, creative work or low power.", performanceStatus,
            "Tune my PC", () => shell.Navigate("Performance overview"), "Performance Lab", () => shell.Navigate("Performance Lab")));
    }

    /// <summary>For the UI check: types a query as if the person had.</summary>
    internal void SetQuery(string text) => query.Text = text;
    /// <summary>For the UI check: scrolls the page.</summary>
    internal void ScrollTo(double offset) { scroller.UpdateLayout(); scroller.ScrollToVerticalOffset(offset); }
    internal int ResultCount => results.Items.Count;

    private static IEnumerable<GlanceTileModel> Loading() =>
        new[] { ("Overview", "Windows"), ("Diagnostic", "Running since restart"), ("Storage", "System drive"), ("Memory", "Memory in use"), ("GPU", "Graphics"), ("Shield", "Protection") }
            .Select(t => new GlanceTileModel(t.Item1, t.Item2, "…", "Reading", CardStatus.Info, null, ""));

    private void ShowGlance(IEnumerable<GlanceTileModel> models)
    {
        glance.Children.Clear();
        foreach (var model in models) {
            var tile = UiKit.StatusTile(model, () => { if (model.Target.Length > 0) shell.Routes.FirstOrDefault(r => r.Name == model.Target)?.Open(); });
            tile.Margin = new Thickness(0, 0, 14, 14); glance.Children.Add(tile);
        }
    }

    internal override void OnShown()
    {
        RefreshStatus();
        _ = RefreshGlanceAsync();
    }

    private void RefreshStatus()
    {
        DiagnosticScan? latest = null;
        string? problem = null;
        try { latest = scans.Read().OrderByDescending(s => s.Ended).FirstOrDefault(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { problem = "Saved scan history couldn't be read."; }
        systemStatus.Text = problem ?? (latest is null ? "No scan yet. Fix My PC checks Windows in one read-only pass." : HomePanel.SystemStatus(latest));
        var performance = PerformanceStatus.Latest();
        performanceStatus.Text = PerformanceStatus.Describe(performance);
        IReadOnlyList<SettingChange> changes = [];
        try { changes = WindowsSettings.Journal().Read(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        ShowActivity(HomeActivity.Build(latest, performance, changes));
    }

    private void ShowActivity(IReadOnlyList<ActivityItem> items)
    {
        activity.Children.Clear();
        if (items.Count == 0) {
            var empty = UiKit.Text("Nothing yet. Scans, checks and the changes Hanki makes will show up here, newest first.", 14, UiKit.Res("TextMuted"), wrap: true); empty.Margin = new Thickness(0, 10, 0, 10);
            activity.Children.Add(empty); return;
        }
        for (int i = 0; i < items.Count; i++) {
            var item = items[i];
            var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            text.Children.Add(UiKit.Text(item.Title, 15, weight: FontWeights.SemiBold));
            text.Children.Add(UiKit.Text(item.Detail, 13, UiKit.Res("TextMuted"), wrap: true));
            var when = UiKit.Text(UiKit.When(item.At), 13, UiKit.Res("TextMuted")); when.VerticalAlignment = VerticalAlignment.Center; when.Margin = new Thickness(12, 0, 12, 0); Grid.SetColumn(when, 1);
            var action = new Button { Style = (Style)Application.Current.FindResource("QuietButton"), Content = item.ActionLabel, Foreground = UiKit.Res("Accent"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(action, 2);
            System.Windows.Automation.AutomationProperties.SetName(action, item.ActionLabel + ": " + item.Title);
            action.Click += (_, _) => shell.Navigate(item.Target);
            row.Children.Add(text); row.Children.Add(when); row.Children.Add(action);
            activity.Children.Add(row);
            if (i < items.Count - 1) activity.Children.Add(new Border { Height = 1, Background = UiKit.Res("Hairline") });
        }
    }

    /// <summary>Reads the glance facts in the background, at most once a minute.</summary>
    private async Task RefreshGlanceAsync()
    {
        if (reading || DateTime.UtcNow - glanceRead < TimeSpan.FromMinutes(1)) return;
        reading = true;
        try {
            var facts = await Task.Run(PcGlanceProbe.Collect);
            ShowGlance(PcGlance.Tiles(facts));
            glanceRead = DateTime.UtcNow;
        } finally { reading = false; }
    }
}

