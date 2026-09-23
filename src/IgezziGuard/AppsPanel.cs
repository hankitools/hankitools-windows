namespace IgezziGuard;

public sealed class AppsPanel : UserControl
{
    public event Action<InstalledApp>? MapUsageRequested;
    private readonly HankiButton refresh = new() { Text = "Refresh installed apps", Primary = true, AutoSize = true };
    private readonly TextBox search = new() { Width = 230, PlaceholderText = "Search name or publisher…" };
    private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false, BackColor = Color.FromArgb(24, 36, 52), ForeColor = Color.WhiteSmoke };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 70 };
    private readonly HankiButton uninstall = new() { Text = "Review / uninstall in Windows…", AutoSize = true };
    private List<InstalledApp> apps = new();
    private CancellationTokenSource? pending;
    private int sortColumn = 3, errors;
    private bool descending = true;
    public bool IsBusy => pending is not null;
    public void Cancel() => pending?.Cancel();
    public AppsPanel()
    {
        Dock = DockStyle.Fill;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        bar.Controls.AddRange([refresh, search, uninstall]);
        var map = new HankiButton { Text = "Map selected app for usage review…", AutoSize = true };
        map.Click += (_, _) => { if (list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is InstalledApp app) MapUsageRequested?.Invoke(app); };
        bar.Controls.Add(map);
        foreach (var (name, width) in new[] { ("App", 230), ("Publisher", 160), ("Version", 100), ("Reported size ↓", 125),
            ("Install date", 110), ("Usage", 200), ("Source", 190) }) list.Columns.Add(name, width);
        Controls.Add(list); Controls.Add(status); Controls.Add(bar);
        refresh.Click += async (_, _) => await LoadApps();
        search.TextChanged += (_, _) => Populate();
        list.ColumnClick += (_, e) => { descending = sortColumn == e.Column ? !descending : e.Column is 3 or 4; sortColumn = e.Column; Populate(); };
        uninstall.Click += (_, _) => {
            var app = list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as InstalledApp : null;
            var message = app is null ? "Open Windows Installed apps?" : $"Review '{app.Name}' in Windows Installed apps? Find it by name and confirm any uninstall there.";
            if (MessageBox.Show(this, message + "\nHanki will not run an uninstaller or remove anything.", "Review installed apps",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.OK)
                DesktopShortcuts.Open(this, "installed-apps");
        };
        Populate();
    }
    private async Task LoadApps()
    {
        if (pending is not null) return;
        using var cts = new CancellationTokenSource(); pending = cts; refresh.Enabled = false;
        status.Text = "Reading installed desktop-app registrations…";
        try { var result = await Task.Run(() => InstalledApps.Read(cts.Token), cts.Token); apps = result.Apps; errors = result.Errors; Populate(); }
        catch (OperationCanceledException) { status.Text = "Refresh cancelled; prior inventory retained."; }
        catch (Exception ex) { status.Text = "Inventory unavailable: " + ex.Message; }
        finally { pending = null; refresh.Enabled = true; }
    }
    private void Populate()
    {
        var matching = apps.Where(a => a.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || a.Publisher.Contains(search.Text, StringComparison.OrdinalIgnoreCase));
        IEnumerable<InstalledApp> sorted = sortColumn switch {
            3 => descending ? matching.OrderBy(a => !a.EstimatedBytes.HasValue).ThenByDescending(a => a.EstimatedBytes) : matching.OrderBy(a => !a.EstimatedBytes.HasValue).ThenBy(a => a.EstimatedBytes),
            4 => descending ? matching.OrderBy(a => !a.InstallDate.HasValue).ThenByDescending(a => a.InstallDate) : matching.OrderBy(a => !a.InstallDate.HasValue).ThenBy(a => a.InstallDate),
            _ => descending ? matching.OrderByDescending(Key, StringComparer.OrdinalIgnoreCase) : matching.OrderBy(Key, StringComparer.OrdinalIgnoreCase)
        };
        list.BeginUpdate(); list.Items.Clear();
        foreach (var app in sorted)
            list.Items.Add(new ListViewItem([app.Name, app.Publisher, app.Version, app.EstimatedBytes.HasValue ? MaintainPanel.SizeText(app.EstimatedBytes.Value) + " est." : "Unknown",
                app.InstallDate?.ToString("yyyy-MM-dd") ?? "Unknown", app.Usage, app.Source]) { Tag = app });
        list.EndUpdate();
        string[] titles = ["App", "Publisher", "Version", "Reported size", "Install date", "Usage", "Source"];
        for (int i = 0; i < titles.Length; i++) list.Columns[i].Text = titles[i] + (i == sortColumn ? descending ? " ↓" : " ↑" : "");
        long reported = apps.Where(a => a.EstimatedBytes.HasValue).Sum(a => a.EstimatedBytes!.Value);
        var biggest = apps.Where(a => a.EstimatedBytes.HasValue).OrderByDescending(a => a.EstimatedBytes).Take(3).Select(a => $"{a.Name} ({MaintainPanel.SizeText(a.EstimatedBytes!.Value)})").ToArray();
        status.Text = apps.Count == 0 ? "Click Refresh installed apps to list desktop apps and their reported sizes. To remove one, select it and choose Review / uninstall in Windows.\nStore apps and portable apps may not appear here."
            : $"{list.Items.Count} of {apps.Count} apps shown · about {MaintainPanel.SizeText(reported)} reported in total" + (biggest.Length > 0 ? " · largest: " + string.Join(", ", biggest) : "") + (errors > 0 ? $" · {errors} could not be read" : "") + ".\n" +
              "Sizes and dates come from each app's installer and can be missing or out of date. Uninstalling happens in Windows Settings, never automatically. Store and portable apps may be missing.";
    }
    private string Key(InstalledApp app) => sortColumn switch { 1 => app.Publisher, 2 => app.Version, 5 => app.Usage, 6 => app.Source, _ => app.Name };
}
