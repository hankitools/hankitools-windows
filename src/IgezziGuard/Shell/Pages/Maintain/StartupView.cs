using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;

namespace IgezziGuard.Shell;

/// <summary>Startup: the apps that launch at sign-in, in each scope, with advice; disabling saves the exact value so it can be restored from the action history.</summary>
internal sealed class StartupView : Grid
{
    private readonly IShellServices shell;
    private UserRunBackend backend = new();
    private StartupActions actions;
    private readonly Button refresh = Buttons.Primary("Refresh entries / history"), disable = Buttons.Secondary("Review / disable selected…"), windows = Buttons.Quiet("All startup apps in Windows…"), restore = Buttons.Secondary("Review / restore selected…");
    private readonly TextBlock status = UiKit.Text("", 13, UiKit.Res("TextMuted"), wrap: true), problem = UiKit.Text("", 13.5, UiKit.Res("Review"), wrap: true);
    private readonly DataList<StartupValue> entries;
    private readonly DataList<StartupAction> history;
    private readonly SubTabs sections = new();
    private IReadOnlyList<StartupValue> values = [];
    private IReadOnlyList<StartupAction> saved = [];

    internal int EntryCount => values.Count;

    internal StartupView(IShellServices shell)
    {
        this.shell = shell;
        actions = new(backend, Path.Combine(SecurityPaths.Root, "startup-actions.json"));
        Margin = new Thickness(0, 0, 0, 20);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        entries = new([new("Name", 210, v => v.Name), new("What it is", 130, v => StartupAdvice.Describe(v.Name, v.Command).Kind), new("Advice", 380, v => StartupAdvice.Describe(v.Name, v.Command).Advice), new("Command (never run by Hanki)", 520, v => v.Command)], false, "Startup entries");
        history = new([new("Time", 170, a => a.At.ToString("g")), new("Name", 210, a => a.Original.Name), new("State", 150, a => a.State), new("Saved command", 480, a => a.Original.Command)], false, "Startup action history");

        var scopes = new WrapPanel { Margin = new Thickness(24, 0, 24, 6) };
        string[] names = ["Current user", "All users (64-bit)", "All users (32-bit)"];
        for (int i = 0; i < names.Length; i++) {
            int index = i;
            var chip = new RadioButton { Style = (Style)Application.Current.FindResource("SubTab"), GroupName = "startupscope", Content = names[i], IsChecked = i == 0, FontSize = 13 };
            chip.Click += (_, _) => SelectScope(index); scopes.Children.Add(chip);
        }
        scopes.Children.Add(refresh); scopes.Children.Add(windows); refresh.Margin = new Thickness(8, 0, 8, 6); windows.Margin = new Thickness(0, 0, 8, 6);
        SetRow(scopes, 0); Children.Add(scopes);

        sections.Add("Startup entries", "Startup entries", () => Section(entries.View, disable));
        sections.Add("Action history / undo", "Action history / undo", () => Section(history.View, restore));
        SetRow(sections, 1); Children.Add(sections);
        var foot = new StackPanel { Margin = new Thickness(24, 4, 24, 0) }; foot.Children.Add(problem); foot.Children.Add(status); problem.Visibility = Visibility.Collapsed;
        SetRow(foot, 2); Children.Add(foot);
        sections.Select("Startup entries");

        status.Text = "Click Refresh to list apps that launch when you sign in. Choose Current user or All users above.\nDisabling removes the registration for future sign-ins; it doesn't close or uninstall anything. All-users changes may need administrator rights.";
        refresh.Click += (_, _) => Run(RefreshData);
        windows.Click += (_, _) => DesktopShortcuts.Open(shell.DialogOwner, "startup");
        entries.SelectionChanged += () => {
            if (entries.Selected is not [var value]) return;
            var (kind, advice) = StartupAdvice.Describe(value.Name, value.Command);
            status.Text = $"{value.Name} — {kind}. {advice}\nThis is a hint from the name and command, not a verdict. Disabling only stops it launching at sign-in; nothing is uninstalled, and you can restore it under Action history / undo.";
        };
        disable.Click += (_, _) => {
            if (entries.Selected is not [var value]) return;
            if (!ReviewPresenter.Ask($"Remove this startup registration in the selected scope?\n\n{value.Name}\n{value.Command}\n\nHanki will save the exact value for undo. No program is stopped or uninstalled. Disabling a security, sync or accessibility app may stop its expected function at your next sign-in.")) return;
            Run(() => { actions.Disable(value); RefreshData(); });
        };
        restore.Click += (_, _) => {
            if (history.Selected is not [var action]) return;
            if (!ReviewPresenter.Ask($"Restore the saved startup registration?\n\n{action.Original.Name}\n{action.Original.Command}\n\nThis permits Windows to launch it at sign-in, subject to Windows Startup settings. Hanki does not launch it now. A conflicting current value blocks the restore.")) return;
            Run(() => { actions.Undo(action.Id); RefreshData(); });
        };
    }

    private static UIElement Section(UIElement list, Button action)
    {
        var grid = new Grid { Margin = new Thickness(24, 0, 24, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
        action.Margin = new Thickness(0, 0, 0, 8); action.HorizontalAlignment = HorizontalAlignment.Left;
        SetRow(action, 0); grid.Children.Add(action); SetRow(list, 1); grid.Children.Add(list);
        return grid;
    }

    private void SelectScope(int index)
    {
        backend = index == 0 ? new() : new(RegistryHive.LocalMachine, index == 1 ? RegistryView.Registry64 : RegistryView.Registry32);
        actions = new(backend, Path.Combine(SecurityPaths.Root, index == 0 ? "startup-actions.json" : $"startup-machine-{index}.json"));
        values = []; saved = []; entries.SetItems(values); history.SetItems(saved);
        Run(RefreshData);
    }

    private void Run(Action work)
    {
        problem.Visibility = Visibility.Collapsed;
        try { work(); }
        catch (Exception ex) { problem.Text = "Startup action stopped: " + ex.Message + "\nRefresh and inspect Action history before retrying."; problem.Visibility = Visibility.Visible; }
    }

    internal void RefreshData()
    {
        values = backend.All(); saved = actions.ReadHistory().OrderByDescending(a => a.At).ToArray();
        entries.SetItems(values); history.SetItems(saved);
        status.Text = values.Count == 0 ? "No startup entries in this scope. Check the other scopes, Startup folders, or Windows Settings → Apps → Startup."
            : $"{values.Count} app(s) launch at sign-in from this scope. Select one to see advice. Fewer startup apps usually means a faster start; every change can be undone.";
    }

    /// <summary>Selects the inner section for a route ("Startup entries" or "Action history / undo").</summary>
    internal void ShowSection(string key) => sections.Select(key);
}
