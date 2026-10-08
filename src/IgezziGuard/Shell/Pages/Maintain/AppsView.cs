using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace IgezziGuard.Shell;

/// <summary>Apps: installed desktop apps with their reported sizes; uninstall through the app's own uninstaller, or remove a leftover entry of an app that is already gone.</summary>
internal sealed class AppsView : Grid
{
    private readonly IShellServices shell;
    private readonly Button refresh = Buttons.Primary("Refresh installed apps"), uninstall = Buttons.Secondary("Uninstall…"), leftovers = Buttons.Secondary("Leftover entries"), map = Buttons.Quiet("Map selected app for usage review…"), windows = Buttons.Quiet("Windows Installed apps");
    private readonly TextBox search = Buttons.Field("Search name or publisher…", 260);
    private readonly TextBlock status = UiKit.Text("", 13, UiKit.Res("TextMuted"), wrap: true);
    private readonly DataList<InstalledApp> list;
    private List<InstalledApp> apps = [];
    private IReadOnlyList<InstalledApp> shown = [];
    private CancellationTokenSource? pending;
    private int sortColumn = 3, errors;
    private bool descending = true, onlyLeftovers;
    private string? note;

    internal bool IsBusy => pending is not null;
    internal int ShownCount => shown.Count;

    internal AppsView(IShellServices shell)
    {
        this.shell = shell;
        Margin = new Thickness(24, 0, 24, 20);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        list = new DataList<InstalledApp>([
            new("App", 240, a => a.Name), new("Publisher", 170, a => a.Publisher), new("Version", 100, a => a.Version),
            new("Reported size", 120, a => a.EstimatedBytes.HasValue ? ByteSize.Text(a.EstimatedBytes.Value) + " est." : "Unknown"),
            new("Install date", 110, a => a.InstallDate?.ToString("yyyy-MM-dd") ?? "Unknown"), new("State", 170, State), new("Source", 190, a => a.Source)], multiSelect: false, "Installed apps");
        list.ShowSort(sortColumn, descending);
        var bar = new WrapPanel(); foreach (UIElement e in new UIElement[] { refresh, search, uninstall, leftovers, map, windows }) bar.Children.Add(e);
        SetRow(bar, 0); Children.Add(bar);
        list.View.Margin = new Thickness(0, 0, 0, 10); SetRow(list.View, 1); Children.Add(list.View);
        SetRow(status, 2); Children.Add(status);

        refresh.Click += async (_, _) => await LoadApps();
        search.TextChanged += (_, _) => Populate();
        list.HeaderClicked += column => { descending = sortColumn == column ? !descending : column is 3 or 4; sortColumn = column; Populate(); };
        list.SelectionChanged += UpdateActions;
        list.View.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Delete && list.Selected.Count == 1) { e.Handled = true; await Remove(); } };
        uninstall.Click += async (_, _) => await Remove();
        leftovers.Click += (_, _) => { onlyLeftovers = !onlyLeftovers; Populate(); };
        windows.Click += (_, _) => DesktopShortcuts.Open(shell.DialogOwner, "installed-apps");
        map.Click += (_, _) => { if (list.Selected is [var app]) { shell.MapUsage(app); shell.Navigate("Maintain"); shell.Routes.First(r => r.Name == "Maintain  /  Usage review").Open(); } };
        Populate();
    }

    /// <summary>For the UI check: shows a prepared list as if the registry had been read.</summary>
    internal void Load(IEnumerable<InstalledApp> items) { apps = items.ToList(); Populate(); }

    private static string State(InstalledApp app) => app.Leftover ? "Leftover entry (files gone)" : app.NoRemove ? "Can't be removed" : "Installed";
    private string Key(InstalledApp app) => sortColumn switch { 1 => app.Publisher, 2 => app.Version, 5 => State(app), 6 => app.Source, _ => app.Name };

    private void UpdateActions()
    {
        var app = list.Selected is [var one] ? one : null;
        uninstall.IsEnabled = app is not null && !IsBusy;
        uninstall.Content = app?.Leftover == true ? "Remove leftover entry…" : "Uninstall…";
        map.IsEnabled = app is not null;
    }

    private async Task LoadApps()
    {
        if (pending is not null) return;
        using var cts = new CancellationTokenSource(); pending = cts; refresh.IsEnabled = false;
        using var ticket = shell.Tasks.Begin("Apps", cts.Cancel);
        status.Text = "Reading installed desktop-app registrations…";
        try { var result = await Task.Run(() => InstalledApps.Read(cts.Token), cts.Token); apps = result.Apps; errors = result.Errors; Populate(); }
        catch (OperationCanceledException) { status.Text = "Refresh cancelled; prior inventory retained."; }
        catch (Exception ex) { status.Text = "Inventory unavailable: " + ex.Message; }
        finally { pending = null; refresh.IsEnabled = true; UpdateActions(); }
    }

    /// <summary>Uninstalls the selected app, or removes its leftover entry when its files are already gone.</summary>
    private async Task Remove()
    {
        if (IsBusy || list.Selected is not [var app]) return;
        if (app.Leftover) { await RemoveLeftover(app); return; }
        if (AppRemoval.UninstallCommand(app, out var reason) is not { } command) {
            if (RemovalDialogs.Confirm(shell.DialogOwner, $"{app.Name} can't be uninstalled here", reason + " Open Windows Installed apps?", "Open Installed apps")) DesktopShortcuts.Open(shell.DialogOwner, "installed-apps");
            return;
        }
        if (!RemovalDialogs.Confirm(shell.DialogOwner, $"Uninstall {app.Name}?",
            $"Hanki starts the app's own uninstaller, the same one Windows Settings uses{(app.Publisher is { Length: > 0 } p && p != "Unknown" ? $" ({p})" : "")}. Follow its steps; Windows may ask for administrator permission. " +
            "Files and settings the app leaves behind in your folders aren't removed.", "Uninstall")) return;
        using var cts = new CancellationTokenSource(); pending = cts; refresh.IsEnabled = uninstall.IsEnabled = false;
        using var ticket = shell.Tasks.Begin("Apps", cts.Cancel);
        status.Text = $"Running the uninstaller for {app.Name}… Follow its steps. Cancel stops waiting; it doesn't stop the uninstaller.";
        string outcome;
        try {
            await InstalledApps.RunUninstaller(command, cts.Token);
            // Some uninstallers hand over to a copy of themselves and close at once: wait up to 2 minutes for the entry to go.
            InstalledApp? after = await Task.Run(() => InstalledApps.Reread(app));
            for (int i = 0; after is not null && !after.Leftover && i < 40; i++) {
                status.Text = $"Waiting for {app.Name}'s uninstaller to finish…";
                await Task.Delay(3000, cts.Token);
                after = await Task.Run(() => InstalledApps.Reread(app));
            }
            if (after is null) {
                outcome = $"{app.Name} was uninstalled.";
                RemovalLog.TryAdd(new(DateTimeOffset.Now, "App uninstalled", $"{app.Name} {app.Version} ({app.Publisher})"));
            } else if (after.Leftover) {
                outcome = $"{app.Name}'s files are gone, but its entry is still listed.";
                RemovalLog.TryAdd(new(DateTimeOffset.Now, "App uninstalled", $"{app.Name} {app.Version} ({app.Publisher}); its entry was left behind"));
                if (RemovalDialogs.Confirm(shell.DialogOwner, "Remove the leftover entry?", outcome + " Remove it from the installed apps list? A backup is saved first.", "Remove entry")) outcome = LeftoverOutcome(after);
            } else outcome = $"{app.Name} is still installed. The uninstaller may have been cancelled, or it's still running.";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { outcome = "Cancelled at the administrator prompt. Nothing was removed."; }
        catch (System.ComponentModel.Win32Exception ex) { outcome = "The uninstaller couldn't be started: " + ex.Message; }
        catch (OperationCanceledException) { outcome = "Stopped waiting. The uninstaller may still be running; refresh the list when it's done."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { outcome = "The result couldn't be checked: " + ex.Message; }
        finally { pending = null; refresh.IsEnabled = true; }
        note = outcome;
        await LoadApps();
    }

    private async Task RemoveLeftover(InstalledApp app)
    {
        if (!RemovalDialogs.Confirm(shell.DialogOwner, $"Remove the leftover entry for {app.Name}?",
            "The app's files are already gone, but Windows still lists it. Hanki saves the entry to a .reg file first (double-click it to put the entry back), then removes it. " +
            (app.Hive == "LocalMachine" ? "This entry is for all users, so Windows asks for administrator permission." : ""), "Remove entry")) return;
        pending = new CancellationTokenSource(); refresh.IsEnabled = uninstall.IsEnabled = false;
        try { note = await Task.Run(() => LeftoverOutcome(app)); }
        finally { pending.Dispose(); pending = null; refresh.IsEnabled = true; }
        await LoadApps();
    }

    private static string LeftoverOutcome(InstalledApp app)
    {
        try {
            var backup = InstalledApps.RemoveLeftover(app);
            RemovalLog.TryAdd(new(DateTimeOffset.Now, "Leftover app entry removed", $"{app.Name} {app.Version} ({app.Publisher}). Backup: {backup}"));
            return $"Removed the leftover entry for {app.Name}. Backup: {backup}";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return "Cancelled at the administrator prompt. The entry was kept."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception) { return "The entry was kept: " + ex.Message; }
    }

    private void Populate()
    {
        var matching = apps.Where(a => (a.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || a.Publisher.Contains(search.Text, StringComparison.OrdinalIgnoreCase)) && (!onlyLeftovers || a.Leftover));
        IEnumerable<InstalledApp> sorted = sortColumn switch {
            3 => descending ? matching.OrderBy(a => !a.EstimatedBytes.HasValue).ThenByDescending(a => a.EstimatedBytes) : matching.OrderBy(a => !a.EstimatedBytes.HasValue).ThenBy(a => a.EstimatedBytes),
            4 => descending ? matching.OrderBy(a => !a.InstallDate.HasValue).ThenByDescending(a => a.InstallDate) : matching.OrderBy(a => !a.InstallDate.HasValue).ThenBy(a => a.InstallDate),
            _ => descending ? matching.OrderByDescending(Key, StringComparer.OrdinalIgnoreCase) : matching.OrderBy(Key, StringComparer.OrdinalIgnoreCase)
        };
        shown = sorted.ToArray(); list.SetItems(shown); list.ShowSort(sortColumn, descending);
        int leftoverCount = apps.Count(a => a.Leftover);
        leftovers.Visibility = leftoverCount > 0 || onlyLeftovers ? Visibility.Visible : Visibility.Collapsed;
        leftovers.Content = onlyLeftovers ? "Show all apps" : $"Leftover entries ({leftoverCount})";
        long reported = apps.Where(a => a.EstimatedBytes.HasValue).Sum(a => a.EstimatedBytes!.Value);
        var biggest = apps.Where(a => a.EstimatedBytes.HasValue).OrderByDescending(a => a.EstimatedBytes).Take(3).Select(a => $"{a.Name} ({ByteSize.Text(a.EstimatedBytes!.Value)})").ToArray();
        status.Text = (note is null ? "" : note + "\n") + (apps.Count == 0
            ? "Click Refresh installed apps to list desktop apps and their reported sizes. Select one and choose Uninstall… to remove it.\nStore apps and portable apps aren't listed here."
            : $"{shown.Count} of {apps.Count} apps shown · about {ByteSize.Text(reported)} reported in total" + (biggest.Length > 0 ? " · largest: " + string.Join(", ", biggest) : "") + (errors > 0 ? $" · {errors} entries couldn't be read" : "") +
              (leftoverCount > 0 ? $" · {leftoverCount} leftover {(leftoverCount == 1 ? "entry" : "entries")} of apps that are already gone" : "") + ".\n" +
              "Uninstall runs each app's own uninstaller, one at a time. Sizes and dates come from the installers and can be missing or out of date. Store and portable apps may be missing.");
        note = null;
        UpdateActions();
    }
}
