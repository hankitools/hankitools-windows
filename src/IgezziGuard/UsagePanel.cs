using System.Diagnostics;
using System.Text.Json;

namespace IgezziGuard;

public sealed class UsagePanel : UserControl
{
    private readonly string path = Path.Combine(SecurityPaths.Root, "app-observations.json");
    private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 100 };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 30000 };
    private readonly Stopwatch interval = new();
    private readonly HankiButton toggle = new() { Text = "Start local observation", Primary = true, AutoSize = true };
    private List<TrackedApp> apps = [];
    private bool loaded, collecting;
    public bool IsBusy => collecting;
    public UsagePanel()
    {
        Dock = DockStyle.Fill;
        var forget = new HankiButton { Text = "Forget selected mapping / history", AutoSize = true };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true }; bar.Controls.AddRange([toggle, forget]);
        foreach (var (name, width) in new[] { ("App", 180), ("Review", 360), ("Hours since sighting", 160), ("Executable you selected", 400) }) list.Columns.Add(name, width);
        Controls.Add(list); Controls.Add(bar); Controls.Add(status);
        status.Text = "Map an app's executable from Apps & storage first. Observation is off by default; runs only while Hanki is open and enabled.\nChecks process paths every 30 seconds, not foreground use. Protected, short-lived, renamed or other executables may be missed. Review suggestions require 30 days since mapping/last sighting and 20 observed hours. Local names, paths and dates are saved; no upload.";
        toggle.Click += (_, _) => {
            try {
                LoadData(); timer.Enabled = !timer.Enabled; interval.Restart();
                toggle.Text = timer.Enabled ? "Stop local observation" : "Start local observation";
                status.Text = timer.Enabled ? "Observation on. Next sample in about 30 seconds. Only mapped executable sightings are saved locally. Inaccessible and short-lived processes may be missed; process presence is not active use." : "Observation stopped. Saved mappings and dates retained locally; no background service runs.";
            } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Observation unavailable"); }
        };
        forget.Click += (_, _) => {
            if (collecting || list.SelectedItems.Count != 1 || list.SelectedItems[0].Tag is not TrackedApp selected) return;
            if (MessageBox.Show(this, "Forget this executable mapping and its recorded history?", "Forget local observation", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            try { var next = apps.Where(a => a != selected).ToList(); UsageReview.Save(path, next); apps = next; Populate(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save"); }
        };
        timer.Tick += async (_, _) => await Observe();
        VisibleChanged += (_, _) => { if (Visible) { try { LoadData(); Populate(); } catch (Exception ex) { status.Text = "History unavailable; existing file retained: " + ex.Message; } } };
        Disposed += (_, _) => timer.Dispose();
    }
    public void Stop() { timer.Stop(); }
    public void Map(InstalledApp app)
    {
        if (collecting) return;
        try {
            LoadData();
            using var pick = new OpenFileDialog { Filter = "Application executable|*.exe", Title = "Select the main executable for " + app.Name };
            if (pick.ShowDialog(this) != DialogResult.OK) return;
            var executable = Path.GetFullPath(pick.FileName);
            if (apps.Any(a => a.Executable.Equals(executable, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(this, "That executable is already mapped."); return; }
            if (MessageBox.Show(this, $"Map {app.Name} to:\n{executable}\n\nHanki will only observe whether this exact process path is running. It will not launch the file. You can start/stop observation in Usage review.", "Review mapping", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            var next = apps.Append(new TrackedApp(app.Name, executable, DateTimeOffset.Now, null, 0)).ToList();
            UsageReview.Save(path, next); apps = next; Populate();
        } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Mapping unavailable"); }
    }
    private void LoadData() {
        if (loaded) return;
        apps = File.Exists(path) ? JsonSerializer.Deserialize<List<TrackedApp>>(File.ReadAllText(path)) ?? throw new IOException("Invalid observation history.") : [];
        loaded = true;
    }
    private async Task Observe()
    {
        if (collecting || apps.Count == 0) { interval.Restart(); return; }
        collecting = true;
        var seconds = interval.Elapsed.TotalSeconds; interval.Restart();
        // Do not count sleep, long stalls, or time while the app was closed as coverage.
        if (seconds > 45) seconds = 0;
        try {
            var result = await Task.Run(() => {
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); int inaccessible = 0;
                foreach (var process in Process.GetProcesses()) using (process) {
                    try { if (process.MainModule?.FileName is { } file) paths.Add(Path.GetFullPath(file)); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { inaccessible++; }
                }
                return (paths, inaccessible);
            });
            if (!timer.Enabled || IsDisposed) return;
            var now = DateTimeOffset.Now;
            var next = apps.Select(a => a with { LastSeen = result.paths.Contains(a.Executable) ? now : a.LastSeen, ObservedSeconds = result.paths.Contains(a.Executable) ? 0 : a.ObservedSeconds + seconds }).ToList();
            UsageReview.Save(path, next); apps = next; Populate();
            status.Text = $"Observation on; {result.inaccessible} process paths inaccessible this sample. Coverage is incomplete; running does not mean actively used.\nSuggestions are review candidates, not safe-to-uninstall verdicts. Tracking stops when Hanki exits. Names, paths and dates stay in the local observation file.";
        } catch (Exception ex) { timer.Stop(); toggle.Text = "Start local observation"; status.Text = "Observation stopped: " + ex.Message; }
        finally { collecting = false; }
    }
    private void Populate() {
        list.Items.Clear();
        foreach (var app in apps) list.Items.Add(new ListViewItem([app.Name, UsageReview.Describe(app, DateTimeOffset.Now), (app.ObservedSeconds / 3600).ToString("0.0"), app.Executable]) { Tag = app });
    }
}
