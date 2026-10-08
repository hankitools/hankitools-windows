using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using RadioButton = System.Windows.Controls.RadioButton;

namespace IgezziGuard.Shell;

/// <summary>A row of tabs over one content area. Content is built the first time its tab is shown.</summary>
internal sealed class SubTabs : DockPanel
{
    private readonly WrapPanel strip = new() { Margin = new Thickness(24, 0, 24, 8) };
    private readonly ContentPresenter area = new();
    private readonly Dictionary<string, (RadioButton Button, Func<UIElement> Create, UIElement? Content)> tabs = [];
    private readonly string group = "subtabs" + Guid.NewGuid().ToString("N");
    /// <summary>Raised after a tab was shown, with its key.</summary>
    internal event Action<string>? Changed;
    /// <summary>Raised for every SubTabs in the app after a tab was shown; the shell uses it to keep the Back history.</summary>
    internal static event Action<SubTabs, string>? AnySelected;
    internal string? Current { get; private set; }
    internal IEnumerable<string> Keys => tabs.Keys;

    internal SubTabs()
    {
        SetDock(strip, Dock.Top); Children.Add(strip); Children.Add(area);
    }

    /// <summary>Adds a tab. The key is what routes call it (for example "Apps &amp; storage"); the label is what is shown.</summary>
    internal void Add(string key, string label, Func<UIElement> create)
    {
        var button = new RadioButton { Style = (Style)Application.Current.FindResource("SubTab"), GroupName = group, Content = label };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => Select(key);
        tabs[key] = (button, create, null); strip.Children.Add(button);
    }

    internal UIElement? ContentOf(string key) => tabs.TryGetValue(key, out var tab) ? tab.Content : null;

    /// <summary>Shows a tab; an unknown key is ignored.</summary>
    internal void Select(string key)
    {
        if (!tabs.TryGetValue(key, out var tab)) return;
        tab.Content ??= tab.Create(); tabs[key] = tab;
        foreach (var (k, t) in tabs) t.Button.IsChecked = k == key;
        area.Content = tab.Content; Current = key; Changed?.Invoke(key); AnySelected?.Invoke(this, key);
    }
}
