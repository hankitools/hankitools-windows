namespace IgezziGuard;

/// <summary>
/// Every WinForms page of the app, wired together and ready to be hosted: by the legacy <see cref="HankiForm"/> chrome or by the
/// WPF shell inside a WindowsFormsHost. It owns page construction, navigation between pages, the "Find a tool" routes and the
/// running-task bookkeeping. It draws no sidebar, header or footer.
/// </summary>
internal sealed class LegacyWorkspace : Panel
{
    private readonly Func<IWin32Window> owner;
    private readonly WorkspacePages tabs = new() { Dock = DockStyle.Fill };
    private readonly ScannerPanel scanner = new();
    private readonly DiagnosticPanel connection = new("Check my connection", "Checks your network adapter, router, name lookups (DNS for www.microsoft.com, cloudflare.com and example.com) and whether Cloudflare (1.1.1.1) and the first site that resolves answer on port 443. Those servers can see your IP address. Nothing is uploaded and no settings are changed.", "What this checks", NetworkDiagnostics.Check);
    private readonly DiagnosticHistoryPanel diagnosticHistory = new();
    private readonly ActivationPanel activation = new();
    private readonly UpdateHealthPanel updateHealth = new();
    private readonly BatteryStartupPanel batteryStartup = new();
    private readonly FullScanPanel fullScan = new();
    private readonly MaintainPanel maintain = new();
    private readonly AppsPanel apps = new();
    private readonly PerformancePanel performance = new();
    private readonly AssistantPanel assistant = new();
    private readonly AiChatPanel ai = new();
    private readonly UsagePanel usage = new();
    private readonly StartupPanel startup = new();
    private readonly DiagnosticPanel diagnose = new("Check the last 7 days", "Reads warnings, errors and restart records from the Windows System and Application logs for the last 7 days, then explains the common ones in plain language: what they mean, whether they matter, and what to do. The report can contain names and paths. Nothing is cleared or changed.", "What this checks", ReadOnlyDiagnostics.EventLogs);
    private readonly DiagnosticPanel defender = new("Audit Defender settings", "Reads Defender status and configured exclusions using built-in PowerShell. No changes or auto-elevation. Exclusion paths may contain private data.", "What this checks", async token => ResultPresentation.DefenderDiagnosis(await ReadOnlyDiagnostics.Defender(token)));
    private readonly DiagnosticPanel networkDeep = new("Test Wi-Fi and response times", "Reads your Wi-Fi signal and sends 10 pings each to your router and to Cloudflare (1.1.1.1), so you can see whether delays start at home or further out. Cloudflare sees your IP address. The report can contain network names and addresses. No settings are changed.", "What this checks", ReadOnlyDiagnostics.Network);
    private readonly SamplingPanel sampling = new();
    private readonly CrashTimelinePanel timeline = new();
    private readonly DuplicatePanel duplicates = new();
    private readonly StartupFoldersPanel startupFolders = new();
    private static readonly LabState lab = new();
    private readonly LabMonitorPanel longPerformance = new(lab);
    private readonly BottleneckPanel bottleneck = new(lab);
    private readonly StutterPanel stutter = new(lab);
    private readonly TuningPanel tuning = new();
    private readonly NetworkToolsPanel networkTools = new();
    private readonly DefenderToolsPanel defenderTools = new();
    private readonly DumpAnalysisPanel dumps = new();
    private readonly TroubleshootingPanel guidance = new();
    private readonly RecoveryPanel recovery = new();
    private readonly SystemActionsPanel systemActions = new();
    private readonly PerformanceSessionsPanel performanceSessions = new();
    private static readonly GamingState gaming = new();
    private readonly GamingOverviewPanel gamingOverview = new(gaming);
    private readonly GamesPanel gamesPanel = new(gaming);
    private readonly NvidiaPanel nvidiaPanel = new(gaming);
    private readonly AmdPanel amdPanel = new(gaming);
    private readonly GpuPanel gpuPanel = new();
    private readonly CpuPanel cpuPanel = new();
    private readonly MemoryHealthPanel memoryHealth = new();
    private readonly StoragePanel storagePanel = new();

    /// <summary>The page container; its selected tab is the current destination.</summary>
    internal WorkspacePages Tabs => tabs;
    /// <summary>Every navigable tool, as listed in Find a tool.</summary>
    internal IReadOnlyList<ToolLauncher.Route> Routes { get; private set; } = [];
    /// <summary>A message for the status area (for example a Defender protection alert).</summary>
    internal event Action<string>? StatusMessage;
    private ToolPage[] ExtraPages => [activation, updateHealth, batteryStartup, diagnosticHistory, systemActions, performanceSessions, gamingOverview, gamesPanel, nvidiaPanel, amdPanel, gpuPanel, bottleneck, stutter, cpuPanel, memoryHealth, storagePanel, fullScan, duplicates, startupFolders, longPerformance, tuning, networkTools, defenderTools, dumps, guidance, recovery, scanner];

    internal LegacyWorkspace(Func<IWin32Window> owner)
    {
        this.owner = owner;
        Dock = DockStyle.Fill;
        Font = new Font("Segoe UI", 10.5f);
        BackColor = HankiTheme.Canvas;
        ForeColor = HankiTheme.Text;
        // One workspace page per navigation destination, in sidebar order (HANKI-ARCH-200).
        foreach (var item in Navigation.Items) Page(item.Page);
        TabPage At(string page) => tabs.TabPages.Cast<TabPage>().Single(p => p.Text == page);
        // Home's search opens a guided check with its symptom chosen.
        void OpenGuide(int index) { Routes.FirstOrDefault(r => r.Name == "Diagnose  /  Guided checks")?.Open(); guidance.ShowSymptom(index); }
        At("Home").Controls.Add(new HomePanel(Navigate, StartFixMyPc, () => Routes, OpenGuide));
        At("System overview").Controls.Add(new Dashboard(Navigate, StartFixMyPc));
        At("Fix My PC").Controls.Add(fullScan);
        var shield = At("Shield");
        shield.Controls.Add(defender);
        var net = At("Connect"); net.Controls.Add(connection);
        var historyText = Report();
        historyText.VisibleChanged += (_, _) => {
            if (!historyText.Visible) return;
            try {
                var entries = new HistoryStore().GetEntries();
                historyText.Text = entries.Count == 0 ? "No scans recorded yet. Run a file or folder scan in Shield to create a summary." : string.Join("\r\n\r\n", entries.Select(x =>
                    $"{x.FinishedAt.ToLocalTime():g} | {x.Target}\r\n{x.FilesScanned} files, {x.DetectionCount} findings, {x.Skipped} skipped, {x.Errors} errors"));
            } catch (IOException ex) { historyText.Text = ex.Message; }
        };
        var maintenance = new HankiTabs { Dock = DockStyle.Fill };
        var filesPage = new TabPage("Files & storage"); filesPage.Controls.Add(maintain);
        var appsPage = new TabPage("Apps & storage"); appsPage.Controls.Add(apps);
        maintenance.TabPages.AddRange([filesPage, appsPage]);
        var usagePage = new TabPage("Usage review"); usagePage.Controls.Add(usage);
        var startupPage = new TabPage("Startup / undo"); startupPage.Controls.Add(startup);
        maintenance.TabPages.AddRange([usagePage, startupPage]);
        AddTab(maintenance, "Duplicates", duplicates); AddTab(maintenance, "Startup folders", startupFolders);
        apps.MapUsageRequested += app => { usage.Map(app); maintenance.SelectedTab = usagePage; };
        At("Maintain").Controls.Add(maintenance);
        var assistantPage = At("Assistant"); assistantPage.Controls.Add(assistant);
        AttachDetail(assistantPage, "Prepare / redact", "In-app AI (optional)", ai);
        void SelectInner(Control control) { if (control.Parent is TabPage page && page.Parent is TabControl inner) inner.SelectedTab = page; }
        assistant.AiRequested += text => { if (ai.LoadDraft(text)) SelectInner(ai); };
        void Prepare(string text) { if (assistant.LoadReport(text)) { tabs.SelectedTab = assistantPage; SelectInner(assistant); } }
        // Guided checks lead: people start from a symptom, then open the tool each step points to.
        var diagnosePage = At("Diagnose"); diagnosePage.Controls.Add(guidance);
        AttachDetail(diagnosePage, "Guided checks", "Crash timeline", timeline);
        timeline.PrepareRequested += Prepare;
        var diagnoseTabs = diagnosePage.Controls.OfType<TabControl>().Single();
        AddTab(diagnoseTabs, "Recent Event Logs", diagnose); AddTab(diagnoseTabs, "Windows Activation", activation); AddTab(diagnoseTabs, "Windows Update", updateHealth);
        // Battery wear, restarts and startup records are health checks, so they live in Hanki System.
        AddTab(diagnoseTabs, "Battery & startup", batteryStartup); AddTab(diagnoseTabs, "Dump analysis", dumps);
        // Microsoft Defender is the real protection; Hanki's experimental scanner comes last.
        AttachDetail(shield, "Defender audit", "Defender controls / alerts", defenderTools);
        var shieldTabs = shield.Controls.OfType<TabControl>().Single();
        AddTab(shieldTabs, "File scanner (experimental)", scanner); AddTab(shieldTabs, "File scan history", historyText);
        AttachDetail(net, "Basic checks", "Wi-Fi / latency", networkDeep);
        AddTab(net.Controls.OfType<TabControl>().Single(), "Advanced / DNS repair", networkTools);
        At("Recovery").Controls.Add(recovery);

        // Hanki Performance. Pages without their tools yet say what will be there; nothing runs by opening them.
        At("Performance overview").Controls.Add(new PerformanceOverviewPanel(Navigate));
        var gamingTabs = new HankiTabs { Dock = DockStyle.Fill };
        AddTab(gamingTabs, "Overview", gamingOverview); AddTab(gamingTabs, "Games", gamesPanel); AddTab(gamingTabs, "NVIDIA", nvidiaPanel); AddTab(gamingTabs, "AMD Radeon", amdPanel);
        At("Gaming").Controls.Add(gamingTabs);
        At("GPU").Controls.Add(gpuPanel);
        var cpuTabs = new HankiTabs { Dock = DockStyle.Fill };
        AddTab(cpuTabs, "Processor", cpuPanel); AddTab(cpuTabs, "Power plans", tuning);
        At("CPU").Controls.Add(cpuTabs);
        var memoryTabs = new HankiTabs { Dock = DockStyle.Fill };
        AddTab(memoryTabs, "Memory & pagefile", performance); AddTab(memoryTabs, "Memory health", memoryHealth);
        At("Memory").Controls.Add(memoryTabs);
        At("Storage").Controls.Add(storagePanel);
        var labTabs = new HankiTabs { Dock = DockStyle.Fill };
        AddTab(labTabs, "Monitor", longPerformance); AddTab(labTabs, "Comparisons", sampling);
        AddTab(labTabs, "Bottleneck Analyzer", bottleneck);
        AddTab(labTabs, "Stutter Diagnostics", stutter);
        AddTab(labTabs, "Benchmarks", new PlannedPanel("Benchmarks", "Repeatable measurements so before/after comparisons are fair."));
        AddTab(labTabs, "Advanced Tuning", new PlannedPanel("Advanced Tuning", "Vendor-supported GPU auto-tuning, kept separate from normal optimization: it is never started by a profile or by Fix My PC, always asks for explicit confirmation, and saves the starting configuration first."));
        At("Performance Lab").Controls.Add(labTabs);

        var systemHistory = new HankiTabs { Dock = DockStyle.Fill };
        AddTab(systemHistory, "Timeline", systemActions); AddTab(systemHistory, "Saved scans", diagnosticHistory);
        At("System actions").Controls.Add(systemHistory);
        At("Performance sessions").Controls.Add(performanceSessions);
        At("Help & community").Controls.Add(new SupportPanel());
        At("Hanki Pro").Controls.Add(new LicensePanel());
        At("History").Controls.Add(new HistoryLanding(Navigate));
        At("Help").Controls.Add(new HelpLanding(Navigate, () => QuickAssist.Open(owner(), gettingHelp: true)));
        foreach (var extra in ExtraPages) extra.PrepareRequested += Prepare;
        diagnose.PrepareRequested += Prepare; defender.PrepareRequested += Prepare;
        networkDeep.PrepareRequested += Prepare; sampling.PrepareRequested += Prepare;
        performance.PrepareRequested += Prepare;
        connection.PrepareRequested += Prepare;

        var routes = new List<ToolLauncher.Route>();
        void AddRoutes(TabControl group, string prefix = "") {
            foreach (TabPage page in group.TabPages) {
                var destination = page;
                var name = prefix + page.Text;
                routes.Add(new ToolLauncher.Route(name, () => {
                    for (Control? current = destination; current is not null; current = current.Parent)
                        if (current is TabPage selected && selected.Parent is TabControl inner) inner.SelectedTab = selected;
                }));
                foreach (var child in page.Controls.OfType<TabControl>()) AddRoutes(child, name + "  /  ");
            }
        }
        AddRoutes(tabs);
        Routes = routes;
        guidance.OpenRequested += name => routes.FirstOrDefault(r => r.Name == name)?.Open();
        foreach (var shortcut in new[] { ("Windows / Task Manager", "task-manager"), ("Windows / Event Viewer", "event-viewer"), ("Windows / Settings", "settings"), ("Windows / File Explorer", "explorer") }) {
            var item = shortcut;
            routes.Add(new ToolLauncher.Route(item.Item1, () => DesktopShortcuts.Open(owner(), item.Item2)));
        }
        routes.Add(new ToolLauncher.Route("Quick Assist: get remote help", () => QuickAssist.Open(owner(), gettingHelp: true)));
        routes.Add(new ToolLauncher.Route("Quick Assist: help someone remotely", () => QuickAssist.Open(owner(), gettingHelp: false)));
        defenderTools.ProtectionAlert += message => StatusMessage?.Invoke(message);
        Controls.Add(tabs);
        HankiTheme.Apply(this);
    }

    /// <summary>Selects a workspace page by its title (a navigation destination).</summary>
    internal void Navigate(string name) { var page = tabs.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == name); if (page is not null) tabs.SelectedTab = page; }
    /// <summary>Opens Fix my PC and starts its scan.</summary>
    internal void StartFixMyPc() { Navigate("Fix My PC"); fullScan.Start(); }
    /// <summary>Re-applies the theme, for example after Windows colors change.</summary>
    internal void ApplyTheme() => HankiTheme.Apply(this);

    /// <summary>Names of the areas that currently have work in progress.</summary>
    internal string[] ActiveTasks() => new[] {
        (fullScan.IsBusy, "Fix My PC"), (scanner.IsBusy, "File scan"), (maintain.IsBusy, "Files"), (apps.IsBusy, "Apps"), (performance.IsBusy || sampling.IsBusy || longPerformance.IsBusy, "Performance"),
        (diagnose.IsBusy || timeline.IsBusy || dumps.IsBusy || activation.IsBusy, "Diagnose"), (defender.IsBusy || defenderTools.IsBusy || defenderTools.MonitoringBusy, "Defender"),
        (connection.IsBusy || networkDeep.IsBusy || networkTools.IsBusy, "Connect"), (ai.IsBusy, "AI request"), (usage.IsBusy, "App observation"),
        (duplicates.IsBusy || startupFolders.IsBusy || tuning.IsBusy || guidance.IsBusy || recovery.IsBusy || diagnosticHistory.IsBusy, "Maintenance / recovery")
    }.Where(t => t.Item1).Select(t => t.Item2).ToArray();

    internal void CancelTasks()
    {
        connection.Cancel(); maintain.Cancel(); apps.Cancel(); performance.Cancel(); diagnose.Cancel(); defender.Cancel(); networkDeep.Cancel();
        sampling.Cancel(); ai.Cancel(); timeline.Cancel(); usage.Stop(); defenderTools.StopMonitoring(); foreach (var page in ExtraPages) page.Cancel();
    }

    /// <summary>
    /// Called when the window is closing: stops observation, and if work is still running cancels it and returns true,
    /// meaning the window should stay open until the work has finished.
    /// </summary>
    internal bool CancelBeforeClose()
    {
        usage.Stop(); defenderTools.StopMonitoring();
        bool busy = connection.IsBusy || maintain.IsBusy || apps.IsBusy || performance.IsBusy || diagnose.IsBusy || defender.IsBusy || networkDeep.IsBusy || sampling.IsBusy || ai.IsBusy || usage.IsBusy || timeline.IsBusy || ExtraPages.Any(p => p.IsBusy) || defenderTools.MonitoringBusy;
        if (!busy) return false;
        connection.Cancel(); maintain.Cancel(); apps.Cancel(); performance.Cancel(); diagnose.Cancel(); defender.Cancel(); networkDeep.Cancel(); sampling.Cancel(); ai.Cancel(); timeline.Cancel(); foreach (var extra in ExtraPages) extra.Cancel();
        return true;
    }

    internal static void AddTab(TabControl tabs, string title, Control content) { var page = new TabPage(title); page.Controls.Add(content); tabs.TabPages.Add(page); }
    private TabPage Page(string title) { var page = new TabPage(title) { BackColor = BackColor, Padding = new Padding(24, 2, 24, 18) }; tabs.TabPages.Add(page); return page; }
    private static void AttachDetail(TabPage parent, string originalTitle, string addedTitle, Control detail)
    {
        var original = new TabPage(originalTitle) { BackColor = parent.BackColor };
        var controls = parent.Controls.Cast<Control>().ToArray();
        foreach (var control in controls) original.Controls.Add(control);
        // Preserve original docking z-order after reparenting.
        for (int i = 0; i < controls.Length; i++) original.Controls.SetChildIndex(controls[i], i);
        var added = new TabPage(addedTitle) { BackColor = parent.BackColor }; added.Controls.Add(detail);
        var pages = new HankiTabs { Dock = DockStyle.Fill }; pages.TabPages.AddRange([original, added]); parent.Controls.Add(pages);
    }
    internal static TextBox Report()
    {
        var report = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = HankiTheme.Surface, ForeColor = HankiTheme.Text, BorderStyle = BorderStyle.None };
        NativeTheme.PadText(report); report.Select(0, 0);
        return report;
    }
}
