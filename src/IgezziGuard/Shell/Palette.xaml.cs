using System.Windows;
using System.Windows.Input;

namespace IgezziGuard.Shell;

/// <summary>"Find a tool" (Ctrl+K): type to filter every navigable tool, arrows to choose, Enter to open.</summary>
internal sealed partial class Palette : Window
{
    private readonly IReadOnlyList<ToolLauncher.Route> routes;
    /// <summary>The route chosen, opened by the caller after the palette closes.</summary>
    internal ToolLauncher.Route? Chosen { get; private set; }
    /// <summary>The routes matching a query: every word must appear in the route's name or its search words.</summary>
    internal static IReadOnlyList<ToolLauncher.Route> Match(IReadOnlyList<ToolLauncher.Route> all, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return all.Where(r => words.All(w => r.SearchText.Contains(w, StringComparison.CurrentCultureIgnoreCase))).ToList();
    }

    internal Palette(IReadOnlyList<ToolLauncher.Route> routes)
    {
        this.routes = routes;
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Query.TextChanged += (_, _) => Filter();
        OpenButton.Click += (_, _) => Choose();
        Results.MouseDoubleClick += (_, _) => Choose();
        PreviewKeyDown += OnKey;
        Loaded += (_, _) => { Query.Focus(); };
        Filter();
    }

    private void Filter()
    {
        var matches = Match(routes, Query.Text);
        Results.ItemsSource = matches;
        OpenButton.IsEnabled = matches.Count > 0;
        if (matches.Count > 0) Results.SelectedIndex = 0;
        Hint.Text = matches.Count > 0 ? $"{matches.Count} tools    ↑ ↓ Choose    Enter Open    Esc Close" : "No matching tools. Try a broader term, such as network.";
    }

    private void Choose()
    {
        if (Results.SelectedItem is not ToolLauncher.Route route) return;
        Chosen = route; Close();
    }

    private void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key) {
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.Enter: Choose(); e.Handled = true; break;
            case Key.Down or Key.Up when Results.Items.Count > 0:
                Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Results.Items.Count - 1);
                Results.ScrollIntoView(Results.SelectedItem); e.Handled = true; break;
        }
    }
}
