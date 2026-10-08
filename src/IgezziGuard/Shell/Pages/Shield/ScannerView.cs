using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace IgezziGuard.Shell;

/// <summary>Hanki's experimental file scanner: a bundled test signature (EICAR) and simple heuristics. Read-only: nothing is quarantined or deleted, and it does not replace Defender.</summary>
internal sealed class ScannerView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly Button scanFile = Buttons.Primary("Scan a file…"), scanFolder = Buttons.Primary("Scan a folder…"), cancel = Buttons.Secondary("Cancel"), assistant = Buttons.Secondary("Prepare for Assistant");
    private readonly TextBlock status = UiKit.Text("Ready when you are", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly DiagnosisView view = new();
    private CancellationTokenSource? running;

    internal DiagnosisView Result => view;

    internal ScannerView(IShellServices shell)
    {
        this.shell = shell;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
        root.Children.Add(UiKit.Text("Experimental file scanner: checks files against a bundled test signature (EICAR) and simple heuristics. It is not a malware signature feed and does not replace Microsoft Defender. Read-only: nothing is quarantined or deleted. Files over 512 MB are skipped; archives are not unpacked. Findings need human review.", 14, UiKit.Res("TextMuted"), wrap: true));
        var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 4) }; foreach (var b in new[] { scanFile, scanFolder, cancel, assistant }) bar.Children.Add(b);
        root.Children.Add(bar); root.Children.Add(status); view.Margin = new Thickness(0, 6, 0, 0); root.Children.Add(view);
        Content = root; cancel.IsEnabled = false; assistant.IsEnabled = false;
        scanFile.Click += async (_, _) => { var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a file to scan" }; if (dialog.ShowDialog() == true) await Scan(dialog.FileName); };
        scanFolder.Click += async (_, _) => { var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to scan" }; if (dialog.ShowDialog() == true) await Scan(dialog.FolderName); };
        cancel.Click += (_, _) => running?.Cancel();
        assistant.Click += (_, _) => { if (view.ReportText.Length > 0) shell.PrepareForAssistant(view.ReportText); };
    }

    internal async Task Scan(string path)
    {
        if (running is not null) return;
        using var cts = new CancellationTokenSource(); running = cts; scanFile.IsEnabled = scanFolder.IsEnabled = false; cancel.IsEnabled = true; assistant.IsEnabled = false;
        using var ticket = shell.Tasks.Begin("File scan", cts.Cancel);
        view.Clear(); status.Text = "Collecting results. Your previous report remains available after this operation.";
        try {
            // Created on the UI thread so progress callbacks return to it; the scan itself runs in the background.
            var progress = new Progress<ScanProgress>(p => { if (running == cts) status.Text = $"Scanning… {p.FilesScanned:N0} files checked · {p.Detections} findings · {p.Errors} errors\r\n{p.CurrentPath}"; });
            var summary = await new ScannerService(SignatureDatabase.Load()).ScanAsync(path, progress, cts.Token);
            var report = $"Finished: {summary.FilesScanned} files; {summary.Skipped} skipped; {summary.Errors} errors.\r\nNo findings does not prove safety.\r\n\r\n" +
                string.Join("\r\n\r\n", summary.Findings.Select(f => $"{f.Severity}: {f.DetectionName}\r\n{f.FilePath}\r\n{f.Details}\r\nSHA-256: {f.Sha256}"));
            try { new HistoryStore().Add(summary); }
            catch (IOException ex) { report += "\r\n\r\nScan completed, but its history could not be saved: " + ex.Message; }
            view.Show(ShieldInsights.Scanner(summary, report)); status.Text = "Report ready · " + DateTime.Now.ToString("t"); assistant.IsEnabled = true;
        }
        catch (OperationCanceledException) { status.Text = "Cancelled. Nothing on your PC was changed; incomplete results were discarded. Run it again when you're ready."; }
        catch (Exception ex) { status.Text = "Operation stopped: " + ex.Message; }
        finally { running = null; scanFile.IsEnabled = scanFolder.IsEnabled = true; cancel.IsEnabled = false; }
    }
}
