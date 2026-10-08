using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBox = System.Windows.Controls.TextBox;

namespace IgezziGuard.Shell;

/// <summary>
/// The local app-usage observation: the mapped executables, the 30-second sampler and the saved file. One instance lives for the whole run,
/// so mapping an app on Apps &amp; storage and watching it on Usage review share the same list.
/// </summary>
internal sealed class UsageObserver
{
    internal static UsageObserver Shared { get; } = new();
    private readonly string path = Path.Combine(SecurityPaths.Root, "app-observations.json");
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Stopwatch interval = new();
    private bool loaded;
    internal List<TrackedApp> Apps { get; private set; } = [];
    internal bool Collecting { get; private set; }
    internal bool Running => timer.IsEnabled;
    internal string Status { get; private set; } = "Map an app's executable from Apps & storage first. Observation is off by default; runs only while Hanki is open and enabled.\nChecks process paths every 30 seconds, not foreground use. Protected, short-lived, renamed or other executables may be missed. Review suggestions require 30 days since mapping/last sighting and 20 observed hours. Local names, paths and dates are saved on this PC only.";
    internal event Action? Changed;

    private UsageObserver() => timer.Tick += async (_, _) => await Observe();

    internal void Load()
    {
        if (loaded) return;
        Apps = File.Exists(path) ? JsonSerializer.Deserialize<List<TrackedApp>>(File.ReadAllText(path)) ?? throw new IOException("Invalid observation history.") : [];
        loaded = true;
    }
    internal void Stop() { timer.Stop(); Changed?.Invoke(); }

    internal void Toggle()
    {
        Load(); if (timer.IsEnabled) timer.Stop(); else timer.Start();
        interval.Restart();
        Status = timer.IsEnabled ? "Observation on. Next sample in about 30 seconds. Only mapped executable sightings are saved locally. Inaccessible and short-lived processes may be missed; process presence is not active use." : "Observation stopped. Saved mappings and dates retained locally; no background service runs.";
        Changed?.Invoke();
    }

    internal void Forget(TrackedApp selected)
    {
        var next = Apps.Where(a => a != selected).ToList(); UsageReview.Save(path, next); Apps = next; Changed?.Invoke();
    }

    /// <summary>Asks for the app's main executable and, after review, starts watching that exact path.</summary>
    internal void Map(InstalledApp app)
    {
        if (Collecting) return;
        try {
            Load();
            var pick = new Microsoft.Win32.OpenFileDialog { Filter = "Application executable|*.exe", Title = "Select the main executable for " + app.Name };
            if (pick.ShowDialog() != true) return;
            var executable = Path.GetFullPath(pick.FileName);
            if (Apps.Any(a => a.Executable.Equals(executable, StringComparison.OrdinalIgnoreCase))) { Blocks.Notice("Already mapped", "That executable is already mapped."); return; }
            if (!Blocks.Confirm("Review mapping", $"Map {app.Name} to:\n{executable}\n\nHanki will only observe whether this exact process path is running. It will not launch the file. You can start/stop observation in Usage review.", "Map it")) return;
            var next = Apps.Append(new TrackedApp(app.Name, executable, DateTimeOffset.Now, null, 0)).ToList();
            UsageReview.Save(path, next); Apps = next; Changed?.Invoke();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Blocks.Notice("Mapping unavailable", ex.Message); }
    }

    private async Task Observe()
    {
        if (Collecting || Apps.Count == 0) { interval.Restart(); return; }
        Collecting = true;
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
            if (!timer.IsEnabled) return;
            var now = DateTimeOffset.Now;
            var next = Apps.Select(a => a with { LastSeen = result.paths.Contains(a.Executable) ? now : a.LastSeen, ObservedSeconds = result.paths.Contains(a.Executable) ? 0 : a.ObservedSeconds + seconds }).ToList();
            UsageReview.Save(path, next); Apps = next;
            Status = $"Observation on; {result.inaccessible} process paths inaccessible this sample. Coverage is incomplete; running does not mean actively used.\nSuggestions are review candidates, not safe-to-uninstall verdicts. Tracking stops when Hanki exits. Names, paths and dates stay in the local observation file.";
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { timer.Stop(); Status = "Observation stopped: " + ex.Message; }
        finally { Collecting = false; Changed?.Invoke(); }
    }
}

/// <summary>Maintain → Usage review: the apps you chose to watch and when each was last seen running.</summary>
internal sealed class UsageView : ScrollViewer
{
    private readonly UsageObserver observer = UsageObserver.Shared;
    private readonly DataList<TrackedApp> list = new([
        new("App", 180, a => a.Name), new("Review", 360, a => UsageReview.Describe(a, DateTimeOffset.Now)),
        new("Hours since sighting", 150, a => (a.ObservedSeconds / 3600).ToString("0.0")), new("Executable you selected", 420, a => a.Executable)], false, "Watched apps");
    private readonly Button toggle = Buttons.Primary("Start local observation"), forget = Buttons.Secondary("Forget selected mapping / history");
    private readonly TextBlock status = UiKit.Text("", 13.5, UiKit.Res("TextMuted"), wrap: true);
    internal int AppCount => list.Count;
    internal bool Running => observer.Running;

    internal UsageView()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) }; bar.Children.Add(toggle); bar.Children.Add(forget);
        list.View.Height = 300; status.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(bar); stack.Children.Add(list.View); stack.Children.Add(status); Content = stack;
        toggle.Click += (_, _) => { try { observer.Toggle(); } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { status.Text = "History unavailable; existing file retained: " + ex.Message; } };
        forget.Click += (_, _) => {
            if (observer.Collecting || list.Selected is not [var selected]) return;
            if (!Blocks.Confirm("Forget local observation", "Forget this executable mapping and its recorded history?", "Forget", true)) return;
            try { observer.Forget(selected); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Blocks.Notice("Could not save", ex.Message); }
        };
        observer.Changed += Refresh; Loaded += (_, _) => Load();
    }

    internal void Load()
    {
        try { observer.Load(); } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { status.Text = "History unavailable; existing file retained: " + ex.Message; return; }
        Refresh();
    }
    private void Refresh()
    {
        list.SetItems(observer.Apps.ToArray()); toggle.Content = observer.Running ? "Stop local observation" : "Start local observation"; status.Text = observer.Status;
    }
}

/// <summary>Maintain → Startup folders: files in the current-user and all-users Startup folders; disabling moves one to Hanki's backup, Recovery restores it.</summary>
internal sealed class StartupFoldersView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly ComboBox files = Pick.Box("Startup file", 600);
    private readonly TextBox output = Blocks.Output(600);
    private readonly List<string> paths = [];
    internal int FileCount => paths.Count;

    internal StartupFoldersView(IShellServices shell)
    {
        this.shell = shell; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        stack.Children.Add(UiKit.Text("Manage files in current-user and all-users Startup folders. Disabling moves the file into Hanki's local backup folder; Recovery restores it without overwriting a newer file. All-users changes may need administrator rights. No shortcut is executed. Scheduled tasks and services are outside this tool.", 14, UiKit.Res("TextMuted"), wrap: true));
        var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; bar.Children.Add(files);
        var read = Buttons.Primary("Read startup folders"); var disable = Buttons.Secondary("Review / disable file");
        bar.Children.Add(read); bar.Children.Add(disable); stack.Children.Add(bar); stack.Children.Add(output); Content = stack;
        read.Click += (_, _) => Read(); disable.Click += async (_, _) => await Disable();
    }

    internal void Read()
    {
        try {
            paths.Clear(); files.Items.Clear();
            foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) }) if (Directory.Exists(folder)) {
                // desktop.ini and other hidden system files hold folder settings; they are not startup programs.
                CleanupPolicy.RejectReparseAncestors(folder);
                foreach (var file in Directory.EnumerateFiles(folder).Where(f => !StartupFoldersPanel.IsFolderMetadata(f))) { paths.Add(file); files.Items.Add(file); }
            }
            output.Text = $"{paths.Count} startup files found. Select and review one; nothing selected automatically.";
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { output.Text = ex.Message; }
    }

    private async Task Disable()
    {
        if (files.SelectedIndex < 0) return; var path = paths[files.SelectedIndex];
        try {
            var before = await new WindowsSettings().Read("Startup file", path, CancellationToken.None);
            if (!Blocks.Confirm("Disable this startup file", $"Move this startup file out of the Startup folder?\n{path}\n\nIt will stop launching through this folder at future sign-ins. Restore from Recovery. Keep Hanki's local backup folder until restored.", "Disable file")) return;
            using var ticket = shell.Tasks.Begin("Startup folders", () => { });
            await WindowsSettings.Journal().Apply("Startup file", path, before, "Disabled:" + before[8..], CancellationToken.None);
            output.Text = "Startup file disabled. Restore through Recovery. Refresh this inventory.";
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { output.Text = ex.Message; }
    }
}

/// <summary>CPU → Power plans: inspect the installed plans, switch between Balanced and High performance (recorded in Recovery).</summary>
internal sealed class PowerPlansView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly TextBox output = Blocks.Output(600);
    internal PowerPlansView(IShellServices shell)
    {
        this.shell = shell; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        stack.Children.Add(UiKit.Text("Reversible tuning: choose an installed Windows power plan, then measure the same workload before and after. Higher performance can increase power use, heat and fan noise; it does not guarantee faster games. No service, registry optimizer or security changes.", 14, UiKit.Res("TextMuted"), wrap: true));
        var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var inspect = Buttons.Primary("Inspect available plans"); var balanced = Buttons.Secondary("Balanced"); var high = Buttons.Secondary("High performance");
        bar.Children.Add(inspect); bar.Children.Add(balanced); bar.Children.Add(high); stack.Children.Add(bar); stack.Children.Add(output); Content = stack;
        inspect.Click += async (_, _) => await Inspect();
        balanced.Click += async (_, _) => await Apply("381b4222-f694-41f0-9685-ff5bb260df2e");
        high.Click += async (_, _) => await Apply("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    }

    internal async Task Inspect()
    {
        using var ticket = shell.Tasks.Begin("Power plans", () => { });
        try { output.Text = await WindowsCommand.Run(WindowsSettings.PowerExe, ["/list"], CancellationToken.None); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { output.Text = ex.Message; }
    }

    private async Task Apply(string plan)
    {
        try {
            var before = await new WindowsSettings().Read("Power plan", "Active", CancellationToken.None);
            if (!Blocks.Confirm("Change active power plan", $"Change active power plan?\nBefore: {before}\nAfter: {plan}\n\nOnly installed plans can be activated. Original plan will be recorded in Recovery. High performance can increase heat and battery consumption.", "Change plan")) return;
            using var ticket = shell.Tasks.Begin("Power plans", () => { });
            await WindowsSettings.Journal().Apply("Power plan", "Active", before, plan, CancellationToken.None);
            output.Text = "Power plan changed and verified. Undo is available in Recovery. Repeat the same workload before comparing measurements.";
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { output.Text = ex.Message; }
    }
}

/// <summary>The pagefile explanation, the memory snapshot and the sampling comparison, as report-page actions.</summary>
internal static class MemoryActions
{
    private const string Guide = "WHAT IS THE PAGEFILE?\n\nWindows uses pagefiles to back some committed memory and to move less-used modified memory pages out of RAM. They can also support crash dumps. A pagefile is not a substitute for fast physical RAM.\n\n" +
        "HOW MUCH DO I NEED?\n\nThere is no universal RAM multiplier. Sizing depends on peak committed-memory demand and crash-dump requirements. One snapshot cannot determine the right custom size.\n\nSystem-managed sizing is Windows' default and is the usual starting point unless a workload or administrator requires a specific configuration. Leave disk headroom for growth. Do not disable the pagefile just to chase a performance gain.\n\n" +
        "HOW TO READ THE DASHBOARD\n\nRAM available: physical memory available for allocation.\nCommit / limit: promised memory versus its backing limit, not disk paging activity.\nPeak commit: the highest system commit since boot; it may not include your heaviest future workload.\nActive pagefiles: Windows' current allocated size and usage.\nConfigured entries: the pagefile settings Windows will use after a restart.\n\n" +
        "NEXT STEP\n\nTake snapshots while your normal heavy workload is running. If commit repeatedly approaches its limit, investigate workload demand, system-managed configuration and disk headroom. Increasing the pagefile is not an automatic FPS improvement.\n\nCrash-dump support also depends on dump mode, storage and configuration; Hanki does not certify it.\n\n" +
        "ADVISOR AND SAMPLING\n\nThe snapshot includes guidance based on current commit headroom and peak demand since boot. Use Comparisons in Performance Lab for 30-second sessions. Windows pagefile settings can be opened for manual review; changes there are outside Hanki's undo history. Hanki does not apply pagefile changes.\n\n" +
        "Microsoft reference:\nhttps://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/how-to-determine-the-appropriate-page-file-size-for-64-bit-versions-of-windows";

    internal static IReadOnlyList<ReportAction> Memory(IShellServices shell) => [
        new("Check memory now", Diagnose: async (_, token) => ResultPresentation.PerformanceDiagnosis(await Task.Run(() => PerformanceSnapshot.Collect(token), token)), Primary: true),
        new("Windows pagefile settings…", Open: () => {
            if (Blocks.Confirm("Review Windows settings", "Open Windows Performance Options?\nChoose Advanced → Virtual memory → Change to review pagefile settings.\n\nChanges made there are outside Hanki's undo history and may require a restart. Hanki will not change a setting for you.", "Open it"))
                DesktopShortcuts.Open(shell.DialogOwner, "pagefile");
        }),
        new("Pagefile explained", Open: () => Blocks.EditText(Application.Current.MainWindow!, "Pagefile explained", Guide)),
    ];

    private static PerformanceRun? baseline;

    internal static IReadOnlyList<ReportAction> Comparisons() => [
        new("Sample for 30 seconds", Diagnose: async (_, token) => {
            var current = await PerformanceSampler.Sample(token);
            if (baseline is null) { baseline = current; return PerformanceInsights.Sample(current, null, "BASELINE\r\n" + PerformanceSampler.Describe(current) + "\r\nRun another session for comparison. Baseline exists only until reset/app exit. CPU readings on systems with over 64 logical processors may cover only the calling processor group."); }
            return PerformanceInsights.Sample(current, baseline, "BASELINE\r\n" + PerformanceSampler.Describe(baseline) + "\r\nCURRENT\r\n" + PerformanceSampler.Describe(current) +
                $"\r\nChange in mean CPU: {current.AverageCpu - baseline.AverageCpu:+0.0;-0.0;0.0} percentage points\r\n" +
                $"Change in mean commit: {current.AverageCommitPercent - baseline.AverageCommitPercent:+0.0;-0.0;0.0} percentage points\r\n" +
                "These are observations, not proof that a tweak helped. Workload differences, pagefile growth, cache, background activity and thermal conditions affect comparisons. Thirty seconds is a short observation window; repeat under representative load. No settings changed.");
        }, Primary: true),
        new("Start a new baseline", Open: () => { baseline = null; Blocks.Notice("New baseline", "The next sample you run will become the new baseline. The results shown now are from before the reset."); }),
    ];
}
