using System.Drawing.Imaging;
using System.Text.Json;
namespace IgezziGuard;

internal static class UiSmokeTest
{
    /// <summary>Pages saved as screenshots for review (file name, route), when a screenshot folder is given.</summary>
    internal static readonly IReadOnlyList<(string File, string Route)> Screens = [
        ("home", "Home"), ("fix-my-pc", "System overview"), ("tune-my-pc", "Performance overview"), ("gaming", "Gaming  /  Overview"),
        ("nvidia", "Gaming  /  NVIDIA"), ("diagnose", "Diagnose"), ("history", "History"), ("help", "Help")];

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
