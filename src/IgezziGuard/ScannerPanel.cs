namespace IgezziGuard;

public sealed class ScannerPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    public ScannerPanel() : base("Experimental file scanner: checks files against a bundled test signature (EICAR) and simple heuristics. It is not a malware signature feed and does not replace Microsoft Defender. Read-only: nothing is quarantined or deleted. Files over 512 MB are skipped; archives are not unpacked. Findings need human review.")
    {
        Button("Scan a file…", async () => { using var d = new OpenFileDialog(); if (d.ShowDialog(this) == DialogResult.OK) await Scan(d.FileName); });
        Button("Scan a folder…", async () => { using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) await Scan(d.SelectedPath); });
    }
    private Task Scan(string path)
    {
        // Created on the UI thread so progress callbacks return to it; the scan itself runs in the background.
        var progress = new Progress<ScanProgress>(p => { if (IsBusy) Output.Text = $"Scanning… {p.FilesScanned:N0} files checked · {p.Detections} findings · {p.Errors} errors\r\n{p.CurrentPath}"; });
        return Run(token => Collect(path, progress, token));
    }
    private static async Task<Diagnosis> Collect(string path, IProgress<ScanProgress> progress, CancellationToken token)
    {
        var summary = await new ScannerService(SignatureDatabase.Load()).ScanAsync(path, progress, token);
        var report = $"Finished: {summary.FilesScanned} files; {summary.Skipped} skipped; {summary.Errors} errors.\r\nNo findings does not prove safety.\r\n\r\n" +
            string.Join("\r\n\r\n", summary.Findings.Select(f => $"{f.Severity}: {f.DetectionName}\r\n{f.FilePath}\r\n{f.Details}\r\nSHA-256: {f.Sha256}"));
        try { new HistoryStore().Add(summary); }
        catch (IOException ex) { report += "\r\n\r\nScan completed, but its history could not be saved: " + ex.Message; }
        return ShieldInsights.Scanner(summary, report);
    }
}
