namespace IgezziGuard.Shell;

/// <summary>The read-only checks of Connect and Diagnose, as report-page actions.</summary>
internal static class DiagnoseActions
{
    private static void Open(IShellServices shell, string uri, string manual) => HealthSettings.Open(shell.DialogOwner, uri, manual);

    internal static IReadOnlyList<ReportAction> Connection() => [
        new("Check my connection", Diagnose: (_, token) => Task.Run(() => NetworkDiagnostics.Check(token), token), Primary: true)];
    internal static IReadOnlyList<ReportAction> WifiLatency() => [
        new("Test Wi-Fi and response times", Diagnose: (_, token) => Task.Run(() => ReadOnlyDiagnostics.Network(token), token), Primary: true)];
    internal static IReadOnlyList<ReportAction> EventLogs() => [
        new("Check the last 7 days", Diagnose: (_, token) => Task.Run(() => ReadOnlyDiagnostics.EventLogs(token), token), Primary: true)];

    internal static IReadOnlyList<ReportAction> WindowsUpdate(IShellServices shell) => [
        new("Check Windows Update", Diagnose: async (_, token) => {
            var facts = await new WindowsUpdateDiagnostic().ReadAsync(token);
            var items = UpdateHealth.Evaluate(facts, DateTimeOffset.UtcNow);
            return HealthItems.Diagnose(items, UpdateHealth.Headline(items), HealthItems.Report("Windows Update", items, UpdateHealth.HistoryLines(facts), facts.Notes));
        }, Primary: true),
        new("Open Windows Update settings", Open: () => Open(shell, "ms-settings:windowsupdate", "Settings → Windows Update")),
        new("Update history in Settings", Open: () => Open(shell, "ms-settings:windowsupdate-history", "Settings → Windows Update → Update history")),
    ];

    internal static IReadOnlyList<ReportAction> BatteryStartup(IShellServices shell) => [
        new("Check battery and startup", Diagnose: async (_, token) => {
            var facts = await new BatteryStartupDiagnostic().ReadAsync(token);
            var items = IgezziGuard.BatteryStartup.Evaluate(facts, DateTimeOffset.UtcNow);
            return HealthItems.Diagnose(items, IgezziGuard.BatteryStartup.Headline(items), HealthItems.Report("Battery and startup", items, IgezziGuard.BatteryStartup.BootLines(facts), facts.Notes));
        }, Primary: true),
        new("Open Power & battery settings", Open: () => Open(shell, "ms-settings:powersleep", "Settings → System → Power & battery")),
    ];
}

/// <summary>Connect: find where a connection slows down. The two checks are native; the DNS tools are still the existing page.</summary>
internal sealed class ConnectPage : NativePage
{
    private readonly SubTabs tabs = new();
    internal ReportView? Basic => tabs.ContentOf("Basic checks") as ReportView;
    internal ReportView? Wifi => tabs.ContentOf("Wi-Fi / latency") as ReportView;
    internal string? CurrentTab => tabs.Current;
    internal IEnumerable<string> TabKeys => tabs.Keys;

    internal ConnectPage(IShellServices shell)
    {
        tabs.Add("Basic checks", "Basic checks", () => new ReportView(shell, "Connect",
            "Checks your network adapter, router, name lookups (DNS for www.microsoft.com, cloudflare.com and example.com) and whether Cloudflare (1.1.1.1) and the first site that resolves answer on port 443. Those servers can see your IP address. Nothing is uploaded and no settings are changed.", DiagnoseActions.Connection()));
        tabs.Add("Wi-Fi / latency", "Wi-Fi / latency", () => new ReportView(shell, "Connect",
            "Reads your Wi-Fi signal and sends 10 pings each to your router and to Cloudflare (1.1.1.1), so you can see whether delays start at home or further out. Cloudflare sees your IP address. The report can contain network names and addresses. No settings are changed.", DiagnoseActions.WifiLatency()));
        tabs.Add("Advanced / DNS repair", "Network tools", () => new HostedView(shell, "Advanced / DNS repair"));
        Content = tabs;
    }
    internal override void OnShown() { if (tabs.Current is null) tabs.Select("Basic checks"); }
    internal override void OnRoute(IReadOnlyList<string> path) { if (path.Count > 1 && tabs.Keys.Contains(path[1])) tabs.Select(path[1]); }
}

/// <summary>Diagnose: guided checks and the crash timeline (existing pages), event logs, Windows Update and battery (native), activation and dumps (existing pages).</summary>
internal sealed class DiagnosePage : NativePage
{
    private readonly SubTabs tabs = new();
    internal ReportView? EventLogs => tabs.ContentOf("Recent Event Logs") as ReportView;
    internal ReportView? Update => tabs.ContentOf("Windows Update") as ReportView;
    internal ReportView? Battery => tabs.ContentOf("Battery & startup") as ReportView;
    internal string? CurrentTab => tabs.Current;
    internal IEnumerable<string> TabKeys => tabs.Keys;

    internal DiagnosePage(IShellServices shell)
    {
        tabs.Add("Guided checks", "Guided checks", () => new HostedView(shell, "Guided checks"));
        tabs.Add("Crash timeline", "Crash timeline", () => new HostedView(shell, "Crash timeline"));
        tabs.Add("Recent Event Logs", "Recent Event Logs", () => new ReportView(shell, "Diagnose",
            "Reads warnings, errors and restart records from the Windows System and Application logs for the last 7 days, then explains the common ones in plain language: what they mean, whether they matter, and what to do. The report can contain names and paths. Nothing is cleared or changed.", DiagnoseActions.EventLogs()));
        tabs.Add("Windows Activation", "Windows Activation", () => new HostedView(shell, "Windows Activation"));
        tabs.Add("Windows Update", "Windows Update", () => new ReportView(shell, "Diagnose",
            "See whether Windows is installing its updates: when the last Windows update installed, which updates failed and why, and whether a restart is waiting. Hanki only reads Windows' own records; it doesn't install, hide or remove updates.", DiagnoseActions.WindowsUpdate(shell)));
        tabs.Add("Battery & startup", "Battery & startup", () => new ReportView(shell, "Diagnose",
            "Check how much of its original capacity a laptop battery still holds, when Windows last fully restarted and how the last startup went. Hanki reads Windows' own records and changes nothing.", DiagnoseActions.BatteryStartup(shell)));
        tabs.Add("Dump analysis", "Dump analysis", () => new HostedView(shell, "Dump analysis"));
        Content = tabs;
    }
    internal override void OnShown() { if (tabs.Current is null) tabs.Select("Guided checks"); }
    internal override void OnRoute(IReadOnlyList<string> path) { if (path.Count > 1 && tabs.Keys.Contains(path[1])) tabs.Select(path[1]); }
}
