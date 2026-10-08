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
                            open.Open(); workspace.PerformLayout(); Flush(window.Dispatcher);
                            if (file == "tune-plan") { UiSmokeTest.Find<TunePanel>(workspace)?.Preview(UiSmokeTest.ExamplePlan()); workspace.PerformLayout(); Flush(window.Dispatcher); }
                            if (file == "home-search" && window.CurrentNative is HomePage searching) { searching.SetQuery("slow"); window.UpdateLayout(); Flush(window.Dispatcher); }
                            if (file == "home-glance" && window.CurrentNative is HomePage home) { home.SetQuery(""); home.ScrollTo(460); window.UpdateLayout(); Flush(window.Dispatcher); }
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
                    Version = AppInfo.Version, Passed = error is null, At = DateTimeOffset.Now, Dpi = (int)dpi, Shell = "WPF",
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
            if (window.BackVisible) throw new IOException("A landing page shows a back link: " + entry.Page);
            // Home and Fix my PC are native WPF pages; every other destination is still a hosted WinForms page.
            bool expectNative = entry.Page is "Home" or "System overview";
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

    private static void Capture(ShellWindow window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        int width = (int)Math.Round(window.ActualWidth * dpi.DpiScaleX), height = (int)Math.Round(window.ActualHeight * dpi.DpiScaleY);
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) {
            var device = graphics.GetHdc();
            try { PrintWindow(window.Hwnd, device, 2); } finally { graphics.ReleaseHdc(device); }
        }
        bitmap.Save(path, ImageFormat.Png);
    }
}
