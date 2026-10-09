using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace IgezziGuard.Shell;

/// <summary>
/// Opt-in structural check of the WPF shell and every hosted page (the release build runs it as --ui-smoke-test).
/// No action buttons, diagnostics, repairs or network calls are invoked.
/// </summary>
internal static class ShellSmokeTest
{
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr device, uint flags);

    private static void Flush(Dispatcher dispatcher) { dispatcher.Invoke(() => { }, DispatcherPriority.Render); System.Windows.Forms.Application.DoEvents(); }

    internal static void Run(string reportPath, string? screenshotFolder)
    {
        UiSmokeTest.StartProgress(reportPath);
        var visited = new List<string>(); string? error = null, screenshotError = null; var screenshots = new List<string>(); var shell = new List<string>();
        double dpi = 96;
        var app = ShellApp.Create();
        var window = new ShellWindow();
        window.ContentRendered += (_, _) => window.Dispatcher.BeginInvoke(() => {
            try {
                dpi = VisualTreeHelper.GetDpi(window).PixelsPerInchX;
                var workspace = window.Workspace;
                void Visit(Control root, string prefix) {
                    if (root is TabControl tabs) {
                        var original = tabs.SelectedTab;
                        foreach (TabPage page in tabs.TabPages) {
                            UiSmokeTest.Note("open " + prefix + page.Text);
                            tabs.SelectedTab = page; page.PerformLayout(); workspace.PerformLayout(); Flush(window.Dispatcher);
                            if (page.ClientSize.Width <= 0 || page.ClientSize.Height <= 0) throw new IOException("Empty workspace bounds: " + page.Text);
                            visited.Add(prefix + page.Text);
                            foreach (Control child in page.Controls) Visit(child, prefix + page.Text + "/");
                        }
                        if (original is not null) tabs.SelectedTab = original;
                    } else foreach (Control child in root.Controls) Visit(child, prefix);
                }
                foreach (var size in new[] { new System.Windows.Size(1320, 880), new System.Windows.Size(1120, 740) }) {
                    window.Width = size.Width; window.Height = size.Height; window.UpdateLayout(); Flush(window.Dispatcher);
                    Visit(workspace, (int)size.Width + "px/");
                }
                if (visited.Count < 50) throw new IOException("Fewer workspace views than expected were visited.");
                // Guided checks open tools by route name; a renamed tab must not silently break a step.
                var routes = workspace.Routes;
                var missing = TroubleshootingPanel.Guides.SelectMany(g => g.Steps).Select(s => s.Route).OfType<string>()
                    .Where(route => routes.All(r => r.Name != route)).Distinct().ToArray();
                if (missing.Length > 0) throw new IOException("Guided check routes not found: " + string.Join("; ", missing));
                // Every System, Performance, Performance Lab and History destination has a page (HANKI-ARCH-200).
                var destinations = Navigation.Items.Select(i => i.Page).Concat(Navigation.LabTools.Select(t => "Performance Lab  /  " + t))
                    .Concat(Navigation.Moved.Values);
                var absent = destinations.Where(d => routes.All(r => r.Name != d)).ToArray();
                if (absent.Length > 0) throw new IOException("Navigation destinations without a page: " + string.Join("; ", absent));
                // rc.5: a button labelled with "&" was measured wider than drawn and grew on every layout pass.
                using (var amp = new HankiButton { Text = "Memory & pagefile", AutoSize = true, Appearance = HankiButtonStyle.Tab }) {
                    var once = amp.GetPreferredSize(System.Drawing.Size.Empty); amp.Size = once;
                    if (amp.GetPreferredSize(System.Drawing.Size.Empty) != once) throw new IOException("A button labelled with & changes size on every layout.");
                }
                CheckShell(window, shell);
                // Screenshots for reviewing layout changes; a capture problem is reported but doesn't fail the check.
                if (screenshotFolder is not null) {
                    try {
                        Directory.CreateDirectory(screenshotFolder);
                        window.Width = 1320; window.Height = 880; window.UpdateLayout(); Flush(window.Dispatcher);
                        foreach (var (file, route) in UiSmokeTest.Screens) {
                            if (routes.FirstOrDefault(r => r.Name == route) is not { } open) { screenshotError = "No route " + route; continue; }
                            UiSmokeTest.Note("screenshot " + route);
                            window.ResetHistory(); open.Open(); workspace.PerformLayout(); Flush(window.Dispatcher);
                            if (file == "tune-plan" && window.CurrentNative is PerformanceOverviewPage tuning) { tuning.Tune.Preview(UiSmokeTest.ExamplePlan()); window.UpdateLayout(); Flush(window.Dispatcher); }
                            if (file == "home-search" && window.CurrentNative is HomePage searching) { searching.SetQuery("slow"); window.UpdateLayout(); Flush(window.Dispatcher); }
                            if (file == "home-glance" && window.CurrentNative is HomePage home) { home.SetQuery(""); home.ScrollTo(460); window.UpdateLayout(); Flush(window.Dispatcher); }
                            if (file == "update" && window.CurrentNative is DiagnosePage page) { WaitFor(page.Update!.RunAsync("Check Windows Update", DiagnoseActions.WindowsUpdate(window)), window); window.UpdateLayout(); Flush(window.Dispatcher); }
                            if (file == "memory" && window.CurrentNative is MemoryPage mem) { WaitFor(mem.Health!.RunAsync("Check memory health", SystemPages.MemoryActions(window)), window); window.UpdateLayout(); Flush(window.Dispatcher); }
                            var path = Path.Combine(screenshotFolder, file + ".png");
                            Capture(window, path);
                            screenshots.Add(path);
                        }
                    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ExternalException) { screenshotError = ex.Message; }
                }
            }
            catch (Exception ex) { error = ex.ToString(); }
            finally {
                UiSmokeTest.Note(error is null ? "finished" : "failed: " + error);
                try { File.WriteAllText(Path.GetFullPath(reportPath), JsonSerializer.Serialize(new {
                    Version = AppInfo.Version, Language = Localizer.CurrentLanguage, Passed = error is null, At = DateTimeOffset.Now, Dpi = (int)dpi, Shell = "WPF",
                    Visited = visited, ShellChecks = shell, Error = error, Screenshots = screenshots, ScreenshotError = screenshotError,
                    Limitation = "Structural navigation only. Does not validate pixels, screen readers, native actions, Defender, networking or repairs."
                }, new JsonSerializerOptions { WriteIndented = true })); }
                catch { error = "Could not save smoke-test report."; }
                Environment.ExitCode = error is null ? 0 : 1; app.Shutdown();
            }
        });
        app.Run(window);
    }

    /// <summary>Checks of the shell itself: rail, header, back link and the command palette.</summary>
    private static void CheckShell(ShellWindow window, List<string> notes)
    {
        notes.Add("handle:" + window.Workspace.IsHandleCreated + "/" + window.Workspace.Tabs.IsHandleCreated);
        if (window.Rail.Count != Navigation.Sidebar.Count) throw new IOException("Navigation rail does not list every destination.");
        foreach (var entry in window.Rail) {
            entry.Button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Flush(window.Dispatcher);
            if (window.Workspace.Tabs.SelectedTab?.Text != entry.Page) throw new IOException("Rail item did not open " + entry.Page);
            if (entry.Button.IsChecked != true) throw new IOException("Rail item not selected on " + entry.Page + " (" + string.Join(",", notes) + ")");
            if (window.CurrentTitle.Length == 0) throw new IOException("Empty header title on " + entry.Page);
            // Home, Fix my PC and Tune my PC are native WPF pages; every other destination is still a hosted WinForms page.
            bool expectNative = entry.Page is "Home" or "System overview" or "Performance overview" or "History" or "Help" or "Hanki Pro";
            if ((window.CurrentNative is not null) != expectNative) throw new IOException("Wrong kind of page shown for " + entry.Page);
            notes.Add("rail/" + entry.Page);
        }
        window.Workspace.Navigate("Home"); Flush(window.Dispatcher);
        if (window.CurrentNative is not HomePage home) throw new IOException("Home is not the native page.");
        home.SetQuery("dns"); Flush(window.Dispatcher);
        if (home.ResultCount == 0) throw new IOException("Home search finds nothing for dns.");
        home.SetQuery("zzzzqqq"); Flush(window.Dispatcher);
        if (home.ResultCount != 0) throw new IOException("Home search lists results for nonsense.");
        home.SetQuery(""); notes.Add("home-search");
        window.Workspace.Navigate("Fix My PC"); Flush(window.Dispatcher);
        if (window.CurrentNative is not FixScanPage scanPage || !scanPage.CanStart) throw new IOException("The full-scan page is missing or cannot start a scan.");
        notes.Add("full-scan-page");
        CheckDrawer(window, notes);
        CheckMaintain(window, notes);
        CheckShield(window, notes);
        CheckLab(window, notes);
        CheckSystemPages(window, notes);
        CheckDiagnose(window, notes);
        CheckGuided(window, notes);
        window.Workspace.Navigate("Recovery"); Flush(window.Dispatcher);
        if (window.CurrentNative is not RecoveryPage) throw new IOException("Recovery is not the native page.");
        notes.Add("recovery-page");
        if (LanguagePicker.Choices().Count != 13 || LanguagePicker.Choices()[0].Code.Length != 0) throw new IOException("The language selector must offer Windows language plus twelve languages.");
        foreach (var entry in window.Rail) { var text = ((System.Windows.Controls.TextBlock)((System.Windows.Controls.StackPanel)entry.Button.Content).Children[1]).Text; if (text != Localizer.T(Navigation.Find(entry.Page)!.Label)) throw new IOException("Rail label not translated: " + entry.Page); }
        notes.Add("language/" + Localizer.CurrentLanguage);
        window.Workspace.Navigate("Home"); Flush(window.Dispatcher);
        window.Workspace.Navigate("Maintain"); Flush(window.Dispatcher);
        if (!window.BackVisible || window.CurrentIntroduction.Length == 0) throw new IOException("A tool page lacks its back link or introduction.");
        var routes = window.Workspace.Routes;
        if (Palette.Match(routes, "").Count != routes.Count || Palette.Match(routes, "dns").Count == 0 || Palette.Match(routes, "zzzzqqq").Count != 0)
            throw new IOException("Find a tool does not filter routes correctly.");
        var palette = new Palette(routes) { Owner = window };
        palette.Show(); Flush(window.Dispatcher);
        if (palette.Results.Items.Count != routes.Count) throw new IOException("The palette did not list every tool.");
        palette.Close();
        window.Workspace.Navigate("Home"); Flush(window.Dispatcher);
        notes.Add("back-link"); notes.Add("palette/" + routes.Count + " tools");
    }






    /// <summary>Guided troubleshooting: the scan summary with fixed example data, the internet journey and Back that follows real visits.</summary>
    private static void CheckGuided(ShellWindow window, List<string> notes)
    {
        void Require(bool condition, string message) { if (!condition) throw new IOException("Guided: " + message); }
        window.Workspace.Navigate("Fix My PC"); Flush(window.Dispatcher);
        var page = (FixScanPage)window.CurrentNative!;
        Require(!page.RepairsOffered, "Repairs must not appear before a scan.");
        var now = DateTimeOffset.UtcNow;
        DiagnosticResult Result(string id, string title, CollectionOutcome outcome, FindingSeverity severity) => new("storage", id, DiagnosticCategory.Storage, outcome, severity,
            title, "Example data for the UI check. Nothing was read or changed.", now, now, coverage: "Example coverage");
        var low = Result("capacity", "Example — low free space", CollectionOutcome.Completed, FindingSeverity.Warning);
        ((IShellServices)window).Scan.Preview(new(Guid.NewGuid(), now, now, 3, 3, false, [low, Result("healthy", "Example healthy check", CollectionOutcome.Completed, FindingSeverity.Healthy), Result("missing", "Example unavailable check", CollectionOutcome.Unavailable, FindingSeverity.Unknown)]));
        Flush(window.Dispatcher);
        Require(page.ResultCardCount == 3, "Expected one finding to review plus the incomplete and other-checks cards, found " + page.ResultCardCount);
        page.ToggleOther(); Require(page.ResultCardCount == 4, "Expanding other checks must reveal the healthy finding.");
        page.ToggleOther(); page.ToggleGaps(); Require(page.ResultCardCount == 4, "Missing evidence must be available separately."); page.ToggleGaps();
        page.ShowDetail(low); Require(page.InDetail && page.ResultCardCount == 3, "A finding must lead with what was found, a manual next step and coverage.");
        page.ShowResults(); Require(!page.InDetail, "Back to scan results must leave the detail.");
        ((IShellServices)window).Scan.Preview(new(Guid.NewGuid(), now, now, 1, 1, false, []));
        notes.Add("guided-scan");

        window.Workspace.Navigate("Home"); Flush(window.Dispatcher);
        window.Workspace.Routes.First(r => r.Name == "Connect  /  Guided troubleshooting").Open(); Flush(window.Dispatcher);
        if (window.CurrentNative is not ConnectPage { Guide: { } guide }) throw new IOException("Guided: Connect does not lead with guided troubleshooting.");
        Require(guide.Result.CardCount == 2, "The journey must start with its introduction.");
        guide.Preview(new ConnectionCheck(new(1, "Example Wi-Fi", true, true, false, false, null, true, 20, null, null), "Example report. No probes were run."));
        Require(guide.Result.CardCount == 2, "A check must lead with one next step.");
        window.Workspace.Routes.First(r => r.Name == "Connect  /  Basic checks").Open(); Flush(window.Dispatcher);
        Require(window.BackVisible && window.CurrentRoute == "Connect  /  Basic checks", "A tab inside a page must be recorded as a visit.");
        window.GoBack(); Flush(window.Dispatcher);
        Require(window.CurrentRoute == "Connect  /  Guided troubleshooting" && ReferenceEquals(((ConnectPage)window.CurrentNative!).Guide, guide), "Back must restore the exact guided tab and keep its state.");
        Require(guide.Result.CardCount == 2 && !guide.Resolved, "Returning must retain the previous finding.");
        notes.Add("guided-internet-back");
    }

    private static void WaitFor(Task task, ShellWindow window, int seconds = 90)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Flush(window.Dispatcher); Thread.Sleep(20); }
        if (!task.IsCompleted) throw new IOException("A check did not finish in " + seconds + " seconds.");
        task.GetAwaiter().GetResult();
    }

    /// <summary>GPU, CPU, Memory and Storage: native pages that really run their read-only checks and must produce results.</summary>
    private static void CheckSystemPages(ShellWindow window, List<string> notes)
    {
        IShellServices services = window;
        window.Workspace.Navigate("GPU"); Flush(window.Dispatcher);
        if (window.CurrentNative is not GpuPage gpu) throw new IOException("GPU is not the native page.");
        WaitFor(gpu.View.RunAsync("Show graphics details", gpu.Actions), window);
        if (gpu.View.Result.CardCount == 0) throw new IOException("The GPU check produced no cards.");
        window.Workspace.Navigate("CPU"); Flush(window.Dispatcher);
        if (window.CurrentNative is not CpuPage cpu || cpu.Processor is null) throw new IOException("CPU is not the native page.");
        WaitFor(cpu.Processor.RunAsync("Check processor settings", SystemPages.CpuActions(services)), window);
        if (cpu.Processor.Result.CardCount == 0) throw new IOException("The processor check produced no cards.");
        window.Workspace.Routes.First(r => r.Name == "CPU  /  Power plans").Open(); Flush(window.Dispatcher);
        if (cpu.CurrentTab != "Power plans") throw new IOException("A route did not select the Power plans tab.");
        window.Workspace.Navigate("Memory"); Flush(window.Dispatcher);
        if (window.CurrentNative is not MemoryPage memory) throw new IOException("Memory is not the native page.");
        window.Workspace.Routes.First(r => r.Name == "Memory  /  Memory health").Open(); Flush(window.Dispatcher);
        if (memory.Health is null) throw new IOException("Memory health did not build.");
        WaitFor(memory.Health.RunAsync("Check memory health", SystemPages.MemoryActions(services)), window);
        if (memory.Health.Result.CardCount == 0) throw new IOException("The memory check produced no cards.");
        window.Workspace.Navigate("Gaming"); Flush(window.Dispatcher);
        if (window.CurrentNative is not GamingPage gaming || gaming.Overview is null) throw new IOException("Gaming does not open on its native overview.");
        foreach (var key in gaming.TabKeys) { window.Workspace.Routes.First(r => r.Name == "Gaming  /  " + key).Open(); window.UpdateLayout(); Flush(window.Dispatcher); if (gaming.CurrentTab != key) throw new IOException("A route did not select the Gaming tab " + key); }
        window.Workspace.Navigate("Storage"); Flush(window.Dispatcher);
        if (window.CurrentNative is not StoragePage storage) throw new IOException("Storage is not the native page.");
        WaitFor(storage.View.RunAsync("Check storage", storage.Actions), window);
        if (storage.View.Result.CardCount == 0) throw new IOException("The storage check produced no cards.");
        window.Workspace.Navigate("Home"); Flush(window.Dispatcher);
        notes.Add("system-pages");
    }

    /// <summary>Connect and Diagnose: native tabs and routes, every hosted tab builds, and the local read-only checks really run. Network checks are not run.</summary>
    private static void CheckDiagnose(ShellWindow window, List<string> notes)
    {
        IShellServices services = window;
        window.Workspace.Navigate("Connect"); Flush(window.Dispatcher);
        if (window.CurrentNative is not ConnectPage connect || connect.CurrentTab != "Guided troubleshooting") throw new IOException("Connect is not the native page.");
        foreach (var key in connect.TabKeys) { window.Workspace.Routes.First(r => r.Name == "Connect  /  " + key).Open(); window.UpdateLayout(); Flush(window.Dispatcher); if (connect.CurrentTab != key) throw new IOException("A route did not select the Connect tab " + key); }
        if (connect.Basic is null || connect.Wifi is null) throw new IOException("A Connect check did not build.");
        window.Workspace.Navigate("Diagnose"); Flush(window.Dispatcher);
        if (window.CurrentNative is not DiagnosePage diagnose) throw new IOException("Diagnose is not the native page.");
        foreach (var key in diagnose.TabKeys) { window.Workspace.Routes.First(r => r.Name == "Diagnose  /  " + key).Open(); window.UpdateLayout(); Flush(window.Dispatcher); if (diagnose.CurrentTab != key) throw new IOException("A route did not select the Diagnose tab " + key); }
        WaitFor(diagnose.EventLogs!.RunAsync("Check the last 7 days", DiagnoseActions.EventLogs()), window);
        if (diagnose.EventLogs.Result.CardCount == 0) throw new IOException("The event log check produced no cards.");
        WaitFor(diagnose.Update!.RunAsync("Check Windows Update", DiagnoseActions.WindowsUpdate(services)), window);
        if (diagnose.Update.Result.CardCount == 0) throw new IOException("The Windows Update check produced no cards.");
        WaitFor(diagnose.Battery!.RunAsync("Check battery and startup", DiagnoseActions.BatteryStartup(services)), window);
        if (diagnose.Battery.Result.CardCount == 0) throw new IOException("The battery and startup check produced no cards.");
        window.Workspace.Navigate("Home"); Flush(window.Dispatcher);
        notes.Add("diagnose-connect");
    }
    /// <summary>Shield: native tabs, the Defender views build without running anything, and the scan history loads.</summary>
    private static void CheckShield(ShellWindow window, List<string> notes)
    {
        window.Workspace.Navigate("Shield"); Flush(window.Dispatcher);
        if (window.CurrentNative is not ShieldPage page) throw new IOException("Shield is not the native page.");
        if (page.CurrentTab != "Defender audit" || page.Audit is null) throw new IOException("Shield does not open on the Defender audit.");
        foreach (var key in page.TabKeys) { page.Select(key); window.UpdateLayout(); Flush(window.Dispatcher); }
        if (page.Controls is null || page.Scanner is null || page.History is null) throw new IOException("A Shield tab did not build.");
        if (page.Controls.MonitoringOn) throw new IOException("Defender monitoring starts switched on.");
        window.Workspace.Routes.First(r => r.Name == "Shield  /  File scan history").Open(); Flush(window.Dispatcher);
        if (page.CurrentTab != "File scan history") throw new IOException("A route did not select its Shield tab.");
        window.Workspace.Navigate("Home"); Flush(window.Dispatcher);
        notes.Add("shield-page");
    }

    /// <summary>Performance Lab: native tabs, a prepared measurement shown with its charts, and the analysis views. Nothing is measured.</summary>
    private static void CheckLab(ShellWindow window, List<string> notes)
    {
        window.Workspace.Navigate("Performance Lab"); Flush(window.Dispatcher);
        if (window.CurrentNative is not LabPage page) throw new IOException("Performance Lab is not the native page.");
        if (page.CurrentTab != "Monitor" || page.Monitor is not { } monitor) throw new IOException("The Lab does not open on Monitor.");
        var start = DateTimeOffset.Now.AddMinutes(-1);
        var samples = Enumerable.Range(0, 40).Select(i => new MonitorSample(start.AddSeconds(i), 20 + i % 15, [10.0 + i, 30.0 + i % 40, 5], null, 3500, 8000, 55 + i % 5, 0, 10 + i % 20, 2, 5, 1,
            i % 7 == 0 ? null : 40 + i, 3000, 55, 1800, null, [], null)).ToArray();
        var run = new MonitorRun(start, start.AddSeconds(40), null, null, samples, null, 12288, 2500, 16384, 144, []);
        LegacyWorkspace.Lab.Latest = run;
        monitor.Show(run); window.UpdateLayout(); Flush(window.Dispatcher);
        if (monitor.CpuChart.PointCount != 40 || monitor.Result.CardCount < 4) throw new IOException("The Monitor does not show the prepared measurement.");
        foreach (var key in page.TabKeys) { page.Select(key); window.UpdateLayout(); Flush(window.Dispatcher); }
        page.Stutter!.Analyze(); if (page.Stutter.ItemCount == 0) throw new IOException("Stutter diagnostics shows nothing for a measurement.");
        page.Bottleneck!.Result.Show(BottleneckPanel.Diagnose(run)); if (page.Bottleneck.Result.CardCount == 0) throw new IOException("The Bottleneck Analyzer shows no cards.");
        window.Workspace.Routes.First(r => r.Name == "Performance Lab  /  Stutter Diagnostics").Open(); Flush(window.Dispatcher);
        if (page.CurrentTab != "Stutter Diagnostics") throw new IOException("A route did not select its Lab tab.");
        LegacyWorkspace.Lab.Latest = null;
        window.Workspace.Navigate("Home"); Flush(window.Dispatcher);
        notes.Add("lab-page");
    }
    /// <summary>Maintain: native tabs, route selection, and the Files list with a prepared inventory (sort, filter, selection).</summary>
    private static void CheckMaintain(ShellWindow window, List<string> notes)
    {
        window.Workspace.Navigate("Maintain"); Flush(window.Dispatcher);
        if (window.CurrentNative is not MaintainPage page) throw new IOException("Maintain is not the native page.");
        if (page.CurrentTab != "Files & storage" || page.Files is not { } files) throw new IOException("Maintain does not open on Files & storage.");
        var now = DateTime.UtcNow;
        files.Load(Enumerable.Range(1, 400).Select(i => new InventoryFile($@"C:\fixture\folder{i % 7}\file{i:000}{(i % 3 == 0 ? ".zip" : i % 3 == 1 ? ".txt" : ".mp4")}", i * 10_000L, now.AddDays(-i), now.AddDays(-i))), "fixture");
        window.UpdateLayout(); Flush(window.Dispatcher);
        if (files.VisibleCount != 400) throw new IOException("The Files list does not show every file.");
        files.SelectRows(0, 1, 2); Flush(window.Dispatcher);
        // Apps: a prepared list (filter, sort, leftover toggle); nothing is uninstalled.
        page.Select("Apps & storage"); window.UpdateLayout(); Flush(window.Dispatcher);
        if (page.Apps is not { } appsView) throw new IOException("Apps is not a native view.");
        appsView.Load([
            new InstalledApp("Alpha Tool", "Alpha Inc", "1.0", new DateTime(2026, 1, 2), 50_000_000, "Registry"),
            new InstalledApp("Beta Suite", "Beta Ltd", "2.3", null, null, "Registry"),
            new InstalledApp("Gone App", "Gone Co", "0.9", new DateTime(2025, 5, 5), 1_000, "Registry", Leftover: true)]);
        Flush(window.Dispatcher);
        if (appsView.ShownCount != 3) throw new IOException("Apps does not list the prepared apps.");
        // Startup: reads the current user's Run key (read-only) and shows both sections.
        page.Select("Startup / undo"); window.UpdateLayout(); Flush(window.Dispatcher);
        if (page.Startup is not { } startupView) throw new IOException("Startup is not a native view.");
        startupView.RefreshData(); startupView.ShowSection("Action history / undo"); Flush(window.Dispatcher); startupView.ShowSection("Startup entries");
        window.Workspace.Routes.First(r => r.Name == "Maintain  /  Startup / undo").Open(); Flush(window.Dispatcher);
        if (page.CurrentTab != "Startup / undo") throw new IOException("The Startup route did not open the Startup tab.");
        // Duplicates: a prepared result with one group.
        page.Select("Duplicates"); window.UpdateLayout(); Flush(window.Dispatcher);
        if (page.Duplicates is not { } duplicatesView) throw new IOException("Duplicates is not a native view.");
        var copy1 = new InventoryFile(@"C:\fixture\a\same.bin", 4096, now, now); var copy2 = new InventoryFile(@"C:\fixture\b\same.bin", 4096, now, now);
        duplicatesView.Load(new DuplicateResult([new DuplicateGroup("ABCDEF", [copy1, copy2])], 0, "Compared 2 files."));
        Flush(window.Dispatcher);
        if (duplicatesView.GroupCount != 1) throw new IOException("Duplicates does not list the prepared group.");
        window.Workspace.Routes.First(r => r.Name == "Maintain  /  Startup folders").Open(); Flush(window.Dispatcher);
        if (page.CurrentTab != "Startup folders") throw new IOException("A route did not select its Maintain tab.");
        foreach (var key in page.TabKeys) { page.Select(key); window.UpdateLayout(); Flush(window.Dispatcher); }
        page.Select("Files & storage");
        window.Workspace.Navigate("Home"); Flush(window.Dispatcher);
        notes.Add("maintain-page");
    }
    /// <summary>The review drawer: choices, the "I understand" gate, validation and cancelling, all without a click.</summary>
    private static void CheckDrawer(ShellWindow window, List<string> notes)
    {
        var delete = new ReviewRequest("Delete 2 files?", "2 files · 5 KB.") {
            ListCaption = "Files", ListLines = ["   2 KB   C:\a.txt", "   3 KB   C:\b.txt"],
            Choices = [new("recycle", "Move to Recycle Bin", "You can restore them.", "Move 2 files to Recycle Bin"),
                new("permanent", "Delete permanently", "This can't be undone.", "Delete 2 files permanently", Danger: true, Acknowledge: "I understand these files can't be recovered")],
        };
        var drawer = new ReviewDrawer(delete, (FrameworkElement)window.Content) { Owner = window };
        drawer.Show(); Flush(window.Dispatcher);
        if (!drawer.ConfirmEnabled || drawer.ConfirmText != "Move 2 files to Recycle Bin" || drawer.AcknowledgeVisible) throw new IOException("The delete review does not start on the Recycle Bin choice.");
        drawer.SelectChoice("permanent"); Flush(window.Dispatcher);
        if (drawer.ConfirmEnabled || !drawer.AcknowledgeVisible || drawer.ConfirmText != "Delete 2 files permanently") throw new IOException("Permanent delete is not gated by the acknowledgement.");
        drawer.Acknowledge(true); Flush(window.Dispatcher);
        if (!drawer.ConfirmEnabled) throw new IOException("Acknowledging does not enable permanent delete.");
        drawer.CancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Flush(window.Dispatcher);
        if (drawer.Result is not { Confirmed: false }) throw new IOException("Cancel did not cancel the review.");

        var repairs = new ReviewRequest("Review repairs", "Nothing changes until you choose Repair selected.") {
            Items = [new("sfc", "Repair protected system files", ["What it does: runs SFC."]), new("dism", "Repair the component store", ["What it does: runs DISM."], Blocked: "Needs administrator rights.")],
            Options = [new("network", "Allow network use", "Windows may download files.")], ConfirmLabel = "Repair selected",
            Validate = state => state.Items.Count == 0 ? "Choose at least one repair." : null,
        };
        var second = new ReviewDrawer(repairs, (FrameworkElement)window.Content) { Owner = window };
        second.Show(); Flush(window.Dispatcher);
        if (second.ConfirmEnabled || second.ProblemText != "Choose at least one repair.") throw new IOException("Repairs can be confirmed with nothing ticked.");
        second.Tick("dism", true); Flush(window.Dispatcher);
        if (second.ConfirmEnabled) throw new IOException("A blocked repair can be ticked.");
        second.Tick("sfc", true); Flush(window.Dispatcher);
        if (!second.ConfirmEnabled) throw new IOException("Ticking a repair does not enable Repair selected.");
        second.Close(); Flush(window.Dispatcher);
        // A change review: optional changes start unticked, mandatory ones ticked, "you change it" steps are cards without a box.
        var changeReview = new ReviewRequest("Tune my PC: Gaming + Performance", "Hanki saves the current value of each change first.") {
            Items = [new("a", "Game Mode  ·  Windows", ["Now: Off   →   Recommended: On"]) { Ticked = true },
                new("b", "Mouse acceleration  ·  Windows", ["Now: On   →   Recommended: Off   (optional)"]),
                new("c", "Resizable BAR  ·  Display  ·  you change it", ["Enable it in the BIOS."]) { Informational = true, LinkLabel = "Open settings", Link = () => { } }],
            ConfirmLabel = "Apply selected",
        };
        var third = new ReviewDrawer(changeReview, (FrameworkElement)window.Content) { Owner = window };
        third.Show(); Flush(window.Dispatcher);
        if (!third.ConfirmEnabled) throw new IOException("A review with a pre-ticked change cannot be confirmed.");
        third.Tick("c", true); Flush(window.Dispatcher);
        third.CancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Flush(window.Dispatcher);
        if (third.Result is not { Confirmed: false } cancelled || cancelled.State.Items.Count != 1 || !cancelled.State.Items.Contains("a")) throw new IOException("A pre-ticked change is not counted, or a step for you is counted as chosen.");
        notes.Add("review-drawer");
    }
    private static void Capture(ShellWindow window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        int width = (int)Math.Round(window.ActualWidth * dpi.DpiScaleX), height = (int)Math.Round(window.ActualHeight * dpi.DpiScaleY);
        // A native page is drawn straight from WPF into a bitmap: that needs no visible desktop, so the check can run on a hidden one.
        // A hosted WinForms page still needs the window itself (PrintWindow).
        if (window.CurrentNative is not null && window.Content is System.Windows.Media.Visual root) {
            window.UpdateLayout();
            var target = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            var backdrop = new System.Windows.Media.DrawingVisual();
            using (var dc = backdrop.RenderOpen()) dc.DrawRectangle(UiKit.Res("Canvas"), null, new System.Windows.Rect(0, 0, window.ActualWidth, window.ActualHeight));
            target.Render(backdrop); target.Render(root);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
            using var file = File.Create(path); encoder.Save(file);
            return;
        }
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) {
            var device = graphics.GetHdc();
            try { PrintWindow(window.Hwnd, device, 2); } finally { graphics.ReleaseHdc(device); }
        }
        bitmap.Save(path, ImageFormat.Png);
    }
}
