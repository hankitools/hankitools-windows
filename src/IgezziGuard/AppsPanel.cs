namespace IgezziGuard;

public sealed class AppsPanel : UserControl
{
    public event Action<InstalledApp>? MapUsageRequested;
    private readonly HankiButton refresh = new() { Text = "Refresh installed apps", Primary = true, AutoSize = true };
    private readonly TextBox search = new() { Width = 230, PlaceholderText = "Search name or publisher…" };
    private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false, BackColor = Color.FromArgb(24, 36, 52), ForeColor = Color.WhiteSmoke };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 70 };
    private readonly HankiButton uninstall = new() { Text = "Uninstall…", AutoSize = true, Enabled = false };
    private readonly HankiButton leftovers = new() { Text = "Leftover entries", AutoSize = true, Visible = false, AccessibleName = "Show only leftover entries" };
    private readonly HankiButton windows = new() { Text = "Windows Installed apps", AutoSize = true, Appearance = HankiButtonStyle.Quiet };
    private List<InstalledApp> apps = new();
    private CancellationTokenSource? pending;
    private int sortColumn = 3, errors;
    private bool descending = true, onlyLeftovers;
    private string? note;
    public bool IsBusy => pending is not null;
    public void Cancel() => pending?.Cancel();
    public AppsPanel()
    {
        Dock = DockStyle.Fill;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        bar.Controls.AddRange([refresh, search, uninstall, leftovers]);
        var map = new HankiButton { Text = "Map selected app for usage review…", AutoSize = true, Appearance = HankiButtonStyle.Quiet };
        map.Click += (_, _) => { if (Selected() is { } app) MapUsageRequested?.Invoke(app); };
        bar.Controls.AddRange([map, windows]);
        foreach (var (name, width) in new[] { ("App", 230), ("Publisher", 160), ("Version", 100), ("Reported size ↓", 125),
            ("Install date", 110), ("State", 150), ("Source", 190) }) list.Columns.Add(name, width);
        Controls.Add(list); Controls.Add(status); Controls.Add(bar);
        refresh.Click += async (_, _) => await LoadApps();
        search.TextChanged += (_, _) => Populate();
        list.ColumnClick += (_, e) => { descending = sortColumn == e.Column ? !descending : e.Column is 3 or 4; sortColumn = e.Column; Populate(); };
        list.SelectedIndexChanged += (_, _) => UpdateActions();
        list.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Delete && Selected() is not null) { e.Handled = true; await Remove(); } };
        uninstall.Click += async (_, _) => await Remove();
        leftovers.Click += (_, _) => { onlyLeftovers = !onlyLeftovers; Populate(); };
        windows.Click += (_, _) => DesktopShortcuts.Open(this, "installed-apps");
        Populate();
    }
    private InstalledApp? Selected() => list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as InstalledApp : null;
    private void UpdateActions()
    {
        var app = Selected();
        uninstall.Enabled = app is not null && !IsBusy;
        uninstall.Text = app?.Leftover == true ? "Remove leftover entry…" : "Uninstall…";
        uninstall.Primary = app is not null;
    }

    private async Task LoadApps()
    {
        if (pending is not null) return;
        using var cts = new CancellationTokenSource(); pending = cts; refresh.Enabled = false;
        status.Text = "Reading installed desktop-app registrations…";
        try { var result = await Task.Run(() => InstalledApps.Read(cts.Token), cts.Token); apps = result.Apps; errors = result.Errors; Populate(); }
        catch (OperationCanceledException) { status.Text = "Refresh cancelled; prior inventory retained."; }
        catch (Exception ex) { status.Text = "Inventory unavailable: " + ex.Message; }
        finally { pending = null; refresh.Enabled = true; UpdateActions(); }
    }

    /// <summary>Uninstalls the selected app, or removes its leftover entry when its files are already gone.</summary>
    private async Task Remove()
    {
        if (IsBusy || Selected() is not { } app) return;
        if (app.Leftover) { await RemoveLeftover(app); return; }
        if (AppRemoval.UninstallCommand(app, out var reason) is not { } command) {
            if (RemovalDialogs.Confirm(this, $"{app.Name} can't be uninstalled here", reason + " Open Windows Installed apps?", "Open Installed apps")) DesktopShortcuts.Open(this, "installed-apps");
            return;
        }
        if (!RemovalDialogs.Confirm(this, $"Uninstall {app.Name}?",
            $"Hanki starts the app's own uninstaller, the same one Windows Settings uses{(app.Publisher is { Length: > 0 } p && p != "Unknown" ? $" ({p})" : "")}. Follow its steps; Windows may ask for administrator permission. " +
            "Files and settings the app leaves behind in your folders aren't removed.", "Uninstall")) return;
        using var cts = new CancellationTokenSource(); pending = cts; refresh.Enabled = uninstall.Enabled = false;
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
                if (RemovalDialogs.Confirm(this, "Remove the leftover entry?", outcome + " Remove it from the installed apps list? A backup is saved first.", "Remove entry"))
                    outcome = LeftoverOutcome(after);
            } else outcome = $"{app.Name} is still installed. The uninstaller may have been cancelled, or it's still running.";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { outcome = "Cancelled at the administrator prompt. Nothing was removed."; }
        catch (System.ComponentModel.Win32Exception ex) { outcome = "The uninstaller couldn't be started: " + ex.Message; }
        catch (OperationCanceledException) { outcome = "Stopped waiting. The uninstaller may still be running; refresh the list when it's done."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { outcome = "The result couldn't be checked: " + ex.Message; }
        finally { pending = null; refresh.Enabled = true; }
        note = outcome;
        await LoadApps();
    }

    private async Task RemoveLeftover(InstalledApp app)
    {
        if (!RemovalDialogs.Confirm(this, $"Remove the leftover entry for {app.Name}?",
            "The app's files are already gone, but Windows still lists it. Hanki saves the entry to a .reg file first (double-click it to put the entry back), then removes it. " +
            (app.Hive == "LocalMachine" ? "This entry is for all users, so Windows asks for administrator permission." : ""), "Remove entry")) return;
        pending = new CancellationTokenSource(); refresh.Enabled = uninstall.Enabled = false;
        try { note = await Task.Run(() => LeftoverOutcome(app)); }
        finally { pending.Dispose(); pending = null; refresh.Enabled = true; }
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
        var matching = apps.Where(a => (a.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || a.Publisher.Contains(search.Text, StringComparison.OrdinalIgnoreCase))
            && (!onlyLeftovers || a.Leftover));
        IEnumerable<InstalledApp> sorted = sortColumn switch {
            3 => descending ? matching.OrderBy(a => !a.EstimatedBytes.HasValue).ThenByDescending(a => a.EstimatedBytes) : matching.OrderBy(a => !a.EstimatedBytes.HasValue).ThenBy(a => a.EstimatedBytes),
            4 => descending ? matching.OrderBy(a => !a.InstallDate.HasValue).ThenByDescending(a => a.InstallDate) : matching.OrderBy(a => !a.InstallDate.HasValue).ThenBy(a => a.InstallDate),
            _ => descending ? matching.OrderByDescending(Key, StringComparer.OrdinalIgnoreCase) : matching.OrderBy(Key, StringComparer.OrdinalIgnoreCase)
        };
        list.BeginUpdate(); list.Items.Clear();
        foreach (var app in sorted)
            list.Items.Add(new ListViewItem([app.Name, app.Publisher, app.Version, app.EstimatedBytes.HasValue ? MaintainPanel.SizeText(app.EstimatedBytes.Value) + " est." : "Unknown",
                app.InstallDate?.ToString("yyyy-MM-dd") ?? "Unknown", State(app), app.Source]) { Tag = app });
        list.EndUpdate();
        string[] titles = ["App", "Publisher", "Version", "Reported size", "Install date", "State", "Source"];
        for (int i = 0; i < titles.Length; i++) list.Columns[i].Text = titles[i] + (i == sortColumn ? descending ? " ↓" : " ↑" : "");
        int leftoverCount = apps.Count(a => a.Leftover);
        leftovers.Visible = leftoverCount > 0 || onlyLeftovers;
        leftovers.Text = onlyLeftovers ? "Show all apps" : $"Leftover entries ({leftoverCount})";
        long reported = apps.Where(a => a.EstimatedBytes.HasValue).Sum(a => a.EstimatedBytes!.Value);
        var biggest = apps.Where(a => a.EstimatedBytes.HasValue).OrderByDescending(a => a.EstimatedBytes).Take(3).Select(a => $"{a.Name} ({MaintainPanel.SizeText(a.EstimatedBytes!.Value)})").ToArray();
        status.Text = (note is null ? "" : note + "\n") + (apps.Count == 0 ? "Click Refresh installed apps to list desktop apps and their reported sizes. Select one and choose Uninstall… to remove it.\nStore apps and portable apps may not appear here."
            : $"{list.Items.Count} of {apps.Count} apps shown · about {MaintainPanel.SizeText(reported)} reported in total" + (biggest.Length > 0 ? " · largest: " + string.Join(", ", biggest) : "") + (errors > 0 ? $" · {errors} could not be read" : "") +
              (leftoverCount > 0 ? $" · {leftoverCount} leftover {(leftoverCount == 1 ? "entry" : "entries")} of apps that are already gone" : "") + ".\n" +
              "Uninstall runs each app's own uninstaller, one at a time. Sizes and dates come from the installers and can be missing or out of date. Store and portable apps may be missing.");
        note = null;
        UpdateActions();
    }
    private static string State(InstalledApp app) => app.Leftover ? "Leftover entry (files gone)" : app.NoRemove ? "Can't be removed" : "Installed";
    private string Key(InstalledApp app) => sortColumn switch { 1 => app.Publisher, 2 => app.Version, 5 => State(app), 6 => app.Source, _ => app.Name };
}
