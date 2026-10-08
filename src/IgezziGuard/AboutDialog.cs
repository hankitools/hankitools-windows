namespace IgezziGuard;

/// <summary>"About &amp; privacy": version, runtime, where data is kept, and what the app does not do.</summary>
internal static class AboutDialog
{
    internal static void Show(IWin32Window owner)
    {
        using var dialog = new Form { Text = "About Hanki Tools", Size = new Size(720, 520), MinimumSize = new Size(500, 350), StartPosition = FormStartPosition.CenterParent, Font = new Font("Segoe UI", 10.5f), Padding = new Padding(20) };
        var body = LegacyWorkspace.Report();
        body.Text = $"Hanki Tools {AppInfo.Version}\r\nWindows toolkit • MIT license\r\nRuntime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\r\n\r\n" +
            "No app telemetry, background updater or automatic report upload is implemented. Diagnostics may contain names, paths, network identifiers and application data. Review before sharing.\r\n\r\n" +
            "Network tools contact the targets shown before running. Optional AI sends only the request you review to OpenAI; API billing and provider retention apply. API keys are not intentionally saved to disk.\r\n\r\n" +
            "Local history and recovery backups: " + SecurityPaths.Root + "\r\nKeep this folder until supported changes are undone. The portable app folder is separate from your saved data.\r\n\r\n" +
            "Defender runs independently and may remediate according to Windows policy. The standalone scanner uses a test signature and simple heuristics; it is not a replacement antivirus.\r\n\r\n" +
            "See PRIVACY.md and README-PORTABLE.md included with the download for details.";
        var close = new HankiButton { Text = "Close", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.Cancel };
        dialog.Controls.Add(body); dialog.Controls.Add(close); dialog.CancelButton = close; HankiTheme.Apply(dialog);
        dialog.Shown += (_, _) => { body.Select(0, 0); close.Focus(); };
        dialog.ShowDialog(owner);
    }
}
