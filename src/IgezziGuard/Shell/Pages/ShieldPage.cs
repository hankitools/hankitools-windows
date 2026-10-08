namespace IgezziGuard.Shell;

/// <summary>Shield: Microsoft Defender's status and scans, then Hanki's experimental file scanner and its history.</summary>
internal sealed class ShieldPage : NativePage
{
    private readonly SubTabs tabs = new();
    internal DefenderAuditView? Audit => tabs.ContentOf("Defender audit") as DefenderAuditView;
    internal DefenderControlsView? Controls => tabs.ContentOf("Defender controls / alerts") as DefenderControlsView;
    internal ScannerView? Scanner => tabs.ContentOf("File scanner (experimental)") as ScannerView;
    internal ScanHistoryView? History => tabs.ContentOf("File scan history") as ScanHistoryView;
    internal string? CurrentTab => tabs.Current;
    internal IEnumerable<string> TabKeys => tabs.Keys;

    internal ShieldPage(IShellServices shell)
    {
        tabs.Add("Defender audit", "Defender audit", () => new DefenderAuditView(shell));
        tabs.Add("Defender controls / alerts", "Scans & alerts", () => new DefenderControlsView(shell));
        tabs.Add("File scanner (experimental)", "File scanner (experimental)", () => new ScannerView(shell));
        tabs.Add("File scan history", "File scan history", () => new ScanHistoryView());
        tabs.Changed += key => { if (tabs.ContentOf(key) is ScanHistoryView history) history.Reload(); };
        Content = tabs;
    }

    internal override void OnShown() { if (tabs.Current is null) tabs.Select("Defender audit"); }
    internal override void OnRoute(IReadOnlyList<string> path) { if (path.Count > 1 && tabs.Keys.Contains(path[1])) tabs.Select(path[1]); }
    internal void Select(string key) => tabs.Select(key);
    /// <summary>Stops the Defender watch when the window closes.</summary>
    internal void StopMonitoring() => Controls?.StopMonitoring();
}
