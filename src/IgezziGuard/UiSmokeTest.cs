using System.Drawing.Imaging;
using System.Text.Json;
namespace IgezziGuard;

internal static class UiSmokeTest
{
    /// <summary>Pages saved as screenshots for review (file name, route), when a screenshot folder is given.</summary>
    internal static readonly IReadOnlyList<(string File, string Route)> Screens = [
        ("home", "Home"), ("fix-my-pc", "System overview"), ("tune-my-pc", "Performance overview"), ("gaming", "Gaming  /  Overview"),
        ("nvidia", "Gaming  /  NVIDIA"), ("amd", "Gaming  /  AMD Radeon"), ("diagnose", "Diagnose"), ("history", "History"), ("help", "Help"), ("tune-plan", "Performance overview")];

    /// <summary>An example Tune my PC plan from fixed data (an untuned desktop with an RTX 4070), for the plan screenshot.</summary>
    internal static TunePlan ExamplePlan()
    {
        var gpu = new GpuAdapter("NVIDIA GeForce RTX 4070", GpuVendor.Nvidia, 0x10DE, 0x2786, 1, 12UL << 30, 16UL << 30, 0, "32.0.15.6094", DateTime.Today.AddMonths(-2), false);
        var display = new DisplayInfo("Monitor", @"\\.\DISPLAY1", 1, new DisplayMode(2560, 1440, 60), [new(2560, 1440, 60), new(2560, 1440, 144), new(2560, 1440, 165)], true, false, true, "DisplayPort");
        var windows = new WindowsGamingSettings(false, false, [], false, null, "Balanced", Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), "Balanced", 80, 100, 5,
            BackgroundRecording: true, Mouse: [6, 10, 1]);
        var nvidia = NvidiaSettings.Catalog.Select(s => new NvidiaGlobalSetting(s, "default", s.Id switch {
            NvidiaSettings.PowerManagementId => NvidiaSettings.PowerNormal, NvidiaSettings.VerticalSyncId => NvidiaSettings.VsyncApplication, NvidiaSettings.AnisotropicLevelId => 1u, _ => 0u }, "default")).ToArray();
        var now = DateTimeOffset.Now;
        DiagnosticResult Finding(string module, string id, string title, string current, string recommended, string recommendation, string? source = null) =>
            new(module, id, DiagnosticCategory.Performance, CollectionOutcome.Completed, FindingSeverity.Warning, title, "Example finding for the UI check.", now, now, recommendation: recommendation,
                metadata: new Dictionary<string, string> { ["current"] = current, ["recommended"] = recommended, ["remedy"] = GamingHealth.RemedyHardware, ["source"] = source ?? "" });
        return TunePlanner.Plan(TuneScenario.GamingPerformance, AdaptiveSync.Yes, new TuneInputs(new GraphicsInventory([gpu], [display], false, true, true, false, []), windows, nvidia, [
            Finding("perf-memory", "memory-speed", "Memory runs below its rated speed", "4800 MT/s", "6000 MT/s", "Enable XMP or EXPO in the BIOS."),
            Finding("gaming", "busy:chrome", "chrome is busy in the background", "23% processor", "Close it", "Close or pause chrome before playing if you don't need it.", "Background")]));
    }

    private static string? progressPath;
    internal static bool Active => progressPath is not null;
    /// <summary>
    /// Appends to "report.progress.txt" as the check goes, so a check that hangs or crashes still shows where it was.
    /// </summary>
    internal static void Note(string text)
    {
        if (progressPath is null) return;
        try { File.AppendAllText(progressPath, $"{DateTimeOffset.Now:HH:mm:ss.fff} {text}\n"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static T? Find<T>(Control root) where T : Control =>
        root as T ?? root.Controls.Cast<Control>().Select(Find<T>).FirstOrDefault(c => c is not null);

    // Opt-in structural UI check. No action buttons, diagnostics, repairs or network calls are invoked.
    internal static void Run(string reportPath, string? screenshotFolder = null)
    {
        progressPath = Path.GetFullPath(reportPath) + ".progress.txt";
        try { File.Delete(progressPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Note("start");
        var visited = new List<string>(); string? error = null, screenshotError = null; var screenshots = new List<string>();
        using var form = new HankiForm();
        form.Shown += (_, _) => form.BeginInvoke((Action)(() => {
            try {
                void Visit(Control root, string prefix) {
                    if (root is TabControl tabs) {
                        var original = tabs.SelectedTab;
                        foreach (TabPage page in tabs.TabPages) {
                            Note("open " + prefix + page.Text);
                            tabs.SelectedTab = page; page.PerformLayout(); form.PerformLayout();
                            if (page.ClientSize.Width <= 0 || page.ClientSize.Height <= 0) throw new IOException("Empty workspace bounds: " + page.Text);
                            visited.Add(prefix + page.Text);
                            foreach (Control child in page.Controls) Visit(child, prefix + page.Text + "/");
                        }
                        if (original is not null) tabs.SelectedTab = original;
                    } else foreach (Control child in root.Controls) Visit(child, prefix);
                }
                foreach (var size in new[] { new Size(1320, 880), new Size(1120, 740) }) { form.Size = size; Visit(form, size.Width + "px/"); }
                if (visited.Count < 50) throw new IOException("Fewer workspace views than expected were visited.");
                // Guided checks open tools by route name; a renamed tab must not silently break a step.
                var missing = TroubleshootingPanel.Guides.SelectMany(g => g.Steps).Select(s => s.Route).OfType<string>()
                    .Where(route => form.Routes.All(r => r.Name != route)).Distinct().ToArray();
                if (missing.Length > 0) throw new IOException("Guided check routes not found: " + string.Join("; ", missing));
                // Every System, Performance, Performance Lab and History destination has a page (HANKI-ARCH-200).
                var destinations = Navigation.Items.Select(i => i.Page).Concat(Navigation.LabTools.Select(t => "Performance Lab  /  " + t))
                    .Concat(Navigation.Moved.Values);
                var absent = destinations.Where(d => form.Routes.All(r => r.Name != d)).ToArray();
                if (absent.Length > 0) throw new IOException("Navigation destinations without a page: " + string.Join("; ", absent));
                // Screenshots for reviewing layout changes; a capture problem is reported but doesn't fail the check.
                if (screenshotFolder is not null) {
                    try {
                        Directory.CreateDirectory(screenshotFolder);
                        form.Size = new Size(1320, 880);
                        foreach (var (file, route) in Screens) {
                            if (form.Routes.FirstOrDefault(r => r.Name == route) is not { } open) { screenshotError = "No route " + route; continue; }
                            Note("screenshot " + route);
                            open.Open(); form.PerformLayout(); Application.DoEvents();
                            if (file == "tune-plan") { Find<TunePanel>(form)?.Preview(ExamplePlan()); form.PerformLayout(); Application.DoEvents(); }
                            using var bitmap = new Bitmap(form.Width, form.Height);
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            var path = Path.Combine(screenshotFolder, file + ".png");
                            bitmap.Save(path, ImageFormat.Png);
                            screenshots.Add(path);
                        }
                    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException) { screenshotError = ex.Message; }
                }
            }
            catch (Exception ex) { error = ex.ToString(); }
            finally {
                Note(error is null ? "finished" : "failed: " + error);
                try { File.WriteAllText(Path.GetFullPath(reportPath), JsonSerializer.Serialize(new {
                    Version = AppInfo.Version, Passed = error is null, At = DateTimeOffset.Now, Dpi = form.DeviceDpi,
                    Visited = visited, Error = error, Screenshots = screenshots, ScreenshotError = screenshotError,
                    Limitation = "Structural navigation only. Does not validate pixels, screen readers, native actions, Defender, networking or repairs."
                }, new JsonSerializerOptions { WriteIndented = true })); }
                catch { error = "Could not save smoke-test report."; }
                Environment.ExitCode = error is null ? 0 : 1; form.Close();
            }
        }));
        Application.Run(form);
    }
}
