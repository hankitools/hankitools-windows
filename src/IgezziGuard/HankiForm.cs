namespace IgezziGuard;

public sealed class HankiForm : Form
{
    private readonly WorkspacePages tabs = new() { Dock = DockStyle.Fill };
    private readonly ScannerPanel scanner = new();
    private readonly DiagnosticPanel connection = new("Check my connection", "Checks your network adapter, router, name lookups (DNS for www.microsoft.com, cloudflare.com and example.com) and whether Cloudflare (1.1.1.1) and the first site that resolves answer on port 443. Those servers can see your IP address. Nothing is uploaded and no settings are changed.", "What this checks", NetworkDiagnostics.Check);
    private readonly HankiButton cancel = new() { Text = "Cancel", Dock = DockStyle.Bottom, Enabled = false, Visible = false };
    private readonly Label status = new() { Text = "Ready — no checks run", Dock = DockStyle.Bottom, Height = 32, Tag = "intro" };
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
    private readonly ExtendedPerformancePanel longPerformance = new();
    private readonly TuningPanel tuning = new();
    private readonly NetworkToolsPanel networkTools = new();
    private readonly DefenderToolsPanel defenderTools = new();
    private readonly DumpAnalysisPanel dumps = new();
    private readonly TroubleshootingPanel guidance = new();
    private readonly RecoveryPanel recovery = new();
    /// <summary>Every navigable tool, as listed in Find a tool.</summary>
    internal IReadOnlyList<ToolLauncher.Route> Routes { get; private set; } = [];
    private ToolPage[] ExtraPages => [activation, updateHealth, batteryStartup, diagnosticHistory, fullScan, duplicates, startupFolders, longPerformance, tuning, networkTools, defenderTools, dumps, guidance, recovery, scanner];

    public HankiForm()
    {
        Text = "Hanki Tools • " + AppInfo.Version;
        Size = new Size(1320, 880);
        MinimumSize = new Size(1120, 740);
        Font = new Font("Segoe UI", 10.5f);
        BackColor = HankiTheme.Canvas;
        ForeColor = HankiTheme.Text;
        StartPosition = FormStartPosition.CenterScreen;
        void Navigate(string name) { var page = tabs.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == name); if (page is not null) tabs.SelectedTab = page; }
        Page("Home").Controls.Add(new Dashboard(Navigate, () => { Navigate("Full system scan"); fullScan.Start(); }));
        Page("Full system scan").Controls.Add(fullScan);
        Page("Diagnostic history").Controls.Add(diagnosticHistory);
        var shield = Page("Shield · experimental");
        shield.Controls.Add(defender);
        var net = Page("Connect"); net.Controls.Add(connection);
        var history = Page("Scan history"); var historyText = Report(); history.Controls.Add(historyText);
        tabs.SelectedIndexChanged += (_, _) => {
            if (tabs.SelectedTab == history) {
                try {
                    var entries = new HistoryStore().GetEntries();
                    historyText.Text = entries.Count == 0 ? "No scans recorded yet. Run a file or folder scan in Shield to create a summary." : string.Join("\r\n\r\n", entries.Select(x =>
                        $"{x.FinishedAt.ToLocalTime():g} | {x.Target}\r\n{x.FilesScanned} files, {x.DetectionCount} findings, {x.Skipped} skipped, {x.Errors} errors"));
                } catch (IOException ex) { historyText.Text = ex.Message; }
            }
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
        Page("Maintain").Controls.Add(maintenance);
        Page("Performance").Controls.Add(performance);
        var assistantPage = Page("Assistant"); assistantPage.Controls.Add(assistant);
        AttachDetail(assistantPage, "Prepare / redact", "In-app AI (optional)", ai);
        void SelectInner(Control control) { if (control.Parent is TabPage page && page.Parent is TabControl inner) inner.SelectedTab = page; }
        assistant.AiRequested += text => { if (ai.LoadDraft(text)) SelectInner(ai); };
        void Prepare(string text) { if (assistant.LoadReport(text)) { tabs.SelectedTab = assistantPage; SelectInner(assistant); } }
        // Guided checks lead: people start from a symptom, then open the tool each step points to.
        var diagnosePage = Page("Diagnose"); diagnosePage.Controls.Add(guidance);
        AttachDetail(diagnosePage, "Guided checks", "Crash timeline", timeline);
        timeline.PrepareRequested += Prepare;
        var diagnoseTabs = diagnosePage.Controls.OfType<TabControl>().Single();
        AddTab(diagnoseTabs, "Recent Event Logs", diagnose); AddTab(diagnoseTabs, "Windows Activation", activation); AddTab(diagnoseTabs, "Windows Update", updateHealth); AddTab(diagnoseTabs, "Dump analysis", dumps);
        // Microsoft Defender is the real protection; Hanki's experimental scanner comes last.
        AttachDetail(shield, "Defender audit", "Defender controls / alerts", defenderTools);
        AddTab(shield.Controls.OfType<TabControl>().Single(), "File scanner (experimental)", scanner);
        AttachDetail(net, "Basic checks", "Wi-Fi / latency", networkDeep);
        AddTab(net.Controls.OfType<TabControl>().Single(), "Advanced / DNS repair", networkTools);
        var performancePage = performance.Parent as TabPage;
        if (performancePage is not null) {
            AttachDetail(performancePage, "Snapshot / pagefile", "30-second sample", sampling);
            var performanceTabs = performancePage.Controls.OfType<TabControl>().Single();
            AddTab(performanceTabs, "Long monitoring / saved runs", longPerformance); AddTab(performanceTabs, "Power tuning", tuning); AddTab(performanceTabs, "Battery & startup", batteryStartup);
        }
        Page("Recovery").Controls.Add(recovery);
        Page("Help & community").Controls.Add(new SupportPanel());
        Page("Hanki Pro").Controls.Add(new LicensePanel());
        foreach (var extra in ExtraPages) extra.PrepareRequested += Prepare;
        diagnose.PrepareRequested += Prepare; defender.PrepareRequested += Prepare;
        networkDeep.PrepareRequested += Prepare; sampling.PrepareRequested += Prepare;
        performance.PrepareRequested += Prepare;
        connection.PrepareRequested += Prepare;
        var sidebar = new FlowLayoutPanel { Tag = "pine", Dock = DockStyle.Left, Width = 240, FlowDirection = FlowDirection.TopDown,
            AutoScroll = true, WrapContents = false, Padding = new Padding(12, 6, 12, 12) };
        sidebar.Paint += (_, e) => {
            if (SystemInformation.HighContrast) return;
            using var edge = new Pen(HankiTheme.Border); e.Graphics.DrawLine(edge, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
        };
        sidebar.Controls.Add(new BrandHeader { Margin = new Padding(0, 0, 0, 10) });
        var navigation = new List<(HankiButton Button, TabPage Page)>();
        Label Group(string text) => new() { Text = text, AutoSize = true, Tag = "intro", Font = new Font("Segoe UI", 8.25f, FontStyle.Bold), Margin = new Padding(14, 18, 0, 6) };
        foreach (var name in new[] { "Home", "Full system scan", "Diagnose", "Performance", "Maintain", "Connect", "Shield · experimental", "Assistant", "Diagnostic history", "Scan history", "Recovery", "Help & community", "Hanki Pro" }) {
            if (name == "Diagnose") sidebar.Controls.Add(Group("TOOLS"));
            if (name == "Diagnostic history") sidebar.Controls.Add(Group("RECORDS & SUPPORT"));
            var page = tabs.TabPages.Cast<TabPage>().Single(p => p.Text == name);
            var button = new HankiButton { Text = name == "Shield · experimental" ? "Shield" : name,
                Width = 214, Height = 38, Margin = new Padding(0, 1, 0, 1), AccessibleName = "Open " + name,
                IconKind = name, Appearance = HankiButtonStyle.Navigation, Font = new Font("Segoe UI", 10.25f) };
            button.Click += (_, _) => tabs.SelectedTab = page;
            navigation.Add((button, page)); sidebar.Controls.Add(button);
        }
        var quick = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false, Margin = Padding.Empty };
        var quickToggle = new HankiButton { Text = "›  Quick access", Width = 214, Height = 34, Appearance = HankiButtonStyle.Navigation,
            Margin = new Padding(0, 18, 0, 2), AccessibleName = "Expand quick access", Font = new Font("Segoe UI", 9.75f) };
        quickToggle.Click += (_, _) => {
            quick.Visible = !quick.Visible; quickToggle.Text = quick.Visible ? "⌄  Quick access" : "›  Quick access";
            quickToggle.AccessibleName = quick.Visible ? "Collapse quick access" : "Expand quick access";
        };
        sidebar.Controls.Add(quickToggle); sidebar.Controls.Add(quick);
        foreach (var item in new[] { ("PowerShell (Admin)", "powershell"), ("CMD (Admin)", "cmd"), ("File Explorer", "explorer"), ("Task Manager", "task-manager"), ("Windows Settings", "settings"), ("Event Viewer", "event-viewer") }) {
            var shortcut = new HankiButton { Text = item.Item1, Width = 204, Height = 30, Appearance = HankiButtonStyle.Navigation, Font = new Font("Segoe UI", 9.25f), Margin = new Padding(10, 0, 0, 0) };
            shortcut.Click += (_, _) => DesktopShortcuts.Open(this, item.Item2); quick.Controls.Add(shortcut);
        }
        quick.Controls.Add(new Label { Text = "Admin shortcuts use Windows UAC.", AutoSize = true, Tag = "intro", Font = new Font("Segoe UI", 8.25f), Margin = new Padding(20, 4, 0, 4) });
        var about = new HankiButton { Text = "About & privacy", Appearance = HankiButtonStyle.Navigation, Width = 214, Height = 34, Font = new Font("Segoe UI", 9.75f), Margin = new Padding(0, 1, 0, 1) };
        about.Click += (_, _) => {
            using var dialog = new Form { Text = "About Hanki Tools", Size = new Size(720, 520), MinimumSize = new Size(500, 350), StartPosition = FormStartPosition.CenterParent, Font = Font, Padding = new Padding(20) };
            var body = Report();
            body.Text = $"Hanki Tools {AppInfo.Version}\r\nWindows toolkit • MIT license\r\nRuntime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\r\n\r\n" +
                "No app telemetry, background updater or automatic report upload is implemented. Diagnostics may contain names, paths, network identifiers and application data. Review before sharing.\r\n\r\n" +
                "Network tools contact the targets shown before running. Optional AI sends only the request you review to OpenAI; API billing and provider retention apply. API keys are not intentionally saved to disk.\r\n\r\n" +
                "Local history and recovery backups: " + SecurityPaths.Root + "\r\nKeep this folder until supported changes are undone. The portable app folder is separate from your saved data.\r\n\r\n" +
                "Defender runs independently and may remediate according to Windows policy. The standalone scanner uses a test signature and simple heuristics; it is not a replacement antivirus.\r\n\r\n" +
                "See PRIVACY.md and README-PORTABLE.md included with the download for details.";
            var close = new HankiButton { Text = "Close", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.Cancel };
            dialog.Controls.Add(body); dialog.Controls.Add(close); dialog.CancelButton = close; HankiTheme.Apply(dialog); dialog.ShowDialog(this);
        };
        sidebar.Controls.Add(about);
        sidebar.Controls.Add(new Label { Text = "v" + AppInfo.Version + "  ·  hanki.tools", AutoSize = true, Tag = "intro", Font = new Font("Segoe UI", 8.25f), Margin = new Padding(14, 8, 0, 4) });
        var title = new Label { Text = "Overview", Dock = DockStyle.Top, Height = 58, Font = new Font("Segoe UI Semibold", 19f), Padding = new Padding(22, 16, 0, 0), AutoEllipsis = true };
        var routes = new List<ToolLauncher.Route>();
        void AddRoutes(TabControl group, string prefix = "") {
            foreach (TabPage page in group.TabPages) {
                var destination = page;
                var name = prefix + page.Text;
                routes.Add(new ToolLauncher.Route(name, () => {
                    for (Control? current = destination; current is not null; current = current.Parent)
                        if (current is TabPage selected && selected.Parent is TabControl owner) owner.SelectedTab = selected;
                }));
                foreach (var child in page.Controls.OfType<TabControl>()) AddRoutes(child, name + "  /  ");
            }
        }
        AddRoutes(tabs);
        Routes = routes;
        guidance.OpenRequested += name => routes.FirstOrDefault(r => r.Name == name)?.Open();
        foreach (var shortcut in new[] { ("Windows / Task Manager", "task-manager"), ("Windows / Event Viewer", "event-viewer"), ("Windows / Settings", "settings"), ("Windows / File Explorer", "explorer") }) {
            var item = shortcut;
            routes.Add(new ToolLauncher.Route(item.Item1, () => DesktopShortcuts.Open(this, item.Item2)));
        }
        routes.Add(new ToolLauncher.Route("Quick Assist: get remote help", () => QuickAssist.Open(this, gettingHelp: true)));
        routes.Add(new ToolLauncher.Route("Quick Assist: help someone remotely", () => QuickAssist.Open(this, gettingHelp: false)));
        void FindTool() { using var launcher = new ToolLauncher(routes); launcher.ShowDialog(this); }
        var search = new HankiButton { Text = "Find a tool…", Hint = "Ctrl+K", IconKind = "Search", Appearance = HankiButtonStyle.Field,
            Size = new Size(260, 38), Margin = Padding.Empty, AccessibleName = "Find a tool, Control K", Font = new Font("Segoe UI", 9.75f) };
        search.Click += (_, _) => FindTool();
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.F1) { Navigate("Help & community"); e.SuppressKeyPress = true; } if (e.Control && e.KeyCode == Keys.K) { FindTool(); e.SuppressKeyPress = true; } };
        var header = new Panel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(0, 0, 22, 0) };
        var searchHost = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 18, 0, 0), Margin = Padding.Empty };
        searchHost.Controls.Add(search);
        title.Dock = DockStyle.Fill; header.Controls.Add(title); header.Controls.Add(searchHost);
        var introduction = new Label { Dock = DockStyle.Top, AutoSize = false, Padding = new Padding(24, 0, 24, 16),
            Font = new Font("Segoe UI", 10f), Tag = "intro", AccessibleName = "About this module" };
        void FitIntroduction() {
            int desired = introduction.GetPreferredSize(new Size(Math.Max(120, introduction.Width), 0)).Height;
            if (introduction.Height != desired) introduction.Height = desired;
        }
        introduction.SizeChanged += (_, _) => FitIntroduction();
        var content = new Panel { Dock = DockStyle.Fill }; content.Controls.Add(tabs); content.Controls.Add(introduction); content.Controls.Add(header);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(16, 5, 12, 5) };
        footer.Paint += (_, e) => { if (!SystemInformation.HighContrast) { using var edge = new Pen(HankiTheme.Border); e.Graphics.DrawLine(edge, 0, 0, footer.Width, 0); } };
        status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.Font = new Font("Segoe UI", 9.25f);
        cancel.Dock = DockStyle.Right; cancel.Width = 110; cancel.Text = "Cancel task"; cancel.Font = new Font("Segoe UI", 9.25f);
        footer.Controls.Add(status); footer.Controls.Add(cancel);
        void RefreshNavigation() {
            // Before the handle exists SelectedTab can be null although Home is shown.
            var current = tabs.SelectedTab ?? (tabs.TabCount > 0 ? tabs.TabPages[0] : null);
            foreach (var item in navigation) item.Button.Selected = current == item.Page;
            title.Text = current?.Text switch { "Home" => "Welcome to Hanki Tools", "Shield · experimental" => "Shield", var text => text };
            introduction.Text = current?.Text switch {
                "Home" => "Your Windows toolbox. Inspect, maintain and troubleshoot your PC from one place.",
                "Full system scan" => "One read-only pass across Windows, storage, devices, security and performance. Select a result to see what it means and what to do next.",
                "Diagnostic history" => "Open or compare saved Full System Scan results and manage optional scheduled checks. History stays on this PC.",
                "Diagnose" => "Explore crash events, inspect dumps and follow guided checks to narrow down possible causes and choose your next troubleshooting step.",
                "Performance" => "Measure memory and system activity, compare monitoring sessions and review power settings to understand slowdowns before making changes.",
                "Maintain" => "Find large or duplicate files, review installed apps and manage startup entries to reclaim storage and reduce unnecessary startup activity.",
                "Connect" => "Check your connection, compare DNS and trace network routes to investigate slow or unreliable access and review repair options.",
                "Shield · experimental" => "Review Microsoft Defender protection, run scans and inspect findings to see what needs attention. Hanki’s separate file scanner is experimental.",
                "Assistant" => "Prepare and redact diagnostic reports, then use optional AI chat to help explain the evidence and explore next steps.",
                "Recovery" => "Review recorded changes and undo supported actions when you need to return to a previous configuration.",
                "Help & community" => "Find guides, join the community, get remote help from someone you trust, prepare a bug report and check your version.",
                "Hanki Pro" => "Add scheduled checks, automatic repairs and customer reports with a licence key. Every free tool stays free.",
                "Scan history" => "Review past file-scan summaries to see what was checked, when it ran and how many findings were reported.",
                _ => ""
            };
            FitIntroduction();
        }
        tabs.SelectedIndexChanged += (_, _) => RefreshNavigation(); RefreshNavigation();
        Shown += (_, _) => RefreshNavigation();
        // Contacts Polar only when a Technician licence is due for its weekly check; offline, the stored licence keeps working.
        Shown += async (_, _) => { try { await AppLicensing.RefreshAsync(CancellationToken.None); } catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException) { } };
        Controls.Add(content); Controls.Add(sidebar); Controls.Add(footer);
        var iconStream = typeof(HankiForm).Assembly.GetManifestResourceStream("IgezziGuard.Brand.hanki.ico");
        if (iconStream is not null) { using (iconStream) { using var branded = new Icon(iconStream); Icon = (Icon)branded.Clone(); } }
        HankiTheme.Apply(this);
        defenderTools.ProtectionAlert += message => status.Text = message;
        void CancelTasks() {
            connection.Cancel(); maintain.Cancel(); apps.Cancel(); performance.Cancel(); diagnose.Cancel(); defender.Cancel(); networkDeep.Cancel();
            sampling.Cancel(); ai.Cancel(); timeline.Cancel(); usage.Stop(); defenderTools.StopMonitoring(); foreach (var page in ExtraPages) page.Cancel();
        }
        string[] ActiveTasks() => new[] {
            (fullScan.IsBusy, "Full system scan / repair"), (scanner.IsBusy, "File scan"), (maintain.IsBusy, "Files"), (apps.IsBusy, "Apps"), (performance.IsBusy || sampling.IsBusy || longPerformance.IsBusy, "Performance"),
            (diagnose.IsBusy || timeline.IsBusy || dumps.IsBusy || activation.IsBusy, "Diagnose"), (defender.IsBusy || defenderTools.IsBusy || defenderTools.MonitoringBusy, "Defender"),
            (connection.IsBusy || networkDeep.IsBusy || networkTools.IsBusy, "Connect"), (ai.IsBusy, "AI request"), (usage.IsBusy, "App observation"),
            (duplicates.IsBusy || startupFolders.IsBusy || tuning.IsBusy || guidance.IsBusy || recovery.IsBusy || diagnosticHistory.IsBusy, "Maintenance / recovery")
        }.Where(t => t.Item1).Select(t => t.Item2).ToArray();
        var taskStatus = new Label { Dock = DockStyle.Right, Width = 360, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true, Font = new Font("Segoe UI", 9.25f), Padding = new Padding(0, 0, 10, 0) };
        footer.Controls.Add(taskStatus); footer.Controls.SetChildIndex(taskStatus, 1);
        cancel.Text = "Cancel tasks"; cancel.Width = 120;
        var taskTimer = new System.Windows.Forms.Timer { Interval = 500 };
        taskTimer.Tick += (_, _) => {
            var active = ActiveTasks(); cancel.Enabled = cancel.Visible = active.Length > 0;
            taskStatus.Text = active.Length == 0 ? "" : "Running: " + string.Join(", ", active);
        };
        HankiTheme.Apply(taskStatus);
        taskTimer.Start(); Disposed += (_, _) => taskTimer.Dispose();
        cancel.Click += (_, _) => CancelTasks();
        FormClosing += (_, e) => { usage.Stop(); defenderTools.StopMonitoring(); if (connection.IsBusy || maintain.IsBusy || apps.IsBusy || performance.IsBusy || diagnose.IsBusy || defender.IsBusy || networkDeep.IsBusy || sampling.IsBusy || ai.IsBusy || usage.IsBusy || timeline.IsBusy || ExtraPages.Any(p => p.IsBusy) || defenderTools.MonitoringBusy) { e.Cancel = true; connection.Cancel(); maintain.Cancel(); apps.Cancel(); performance.Cancel(); diagnose.Cancel(); defender.Cancel(); networkDeep.Cancel(); sampling.Cancel(); ai.Cancel(); timeline.Cancel(); foreach (var extra in ExtraPages) extra.Cancel(); status.Text = "Cancelling; close again when finished."; } };
    }
    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        if (IsHandleCreated && !Disposing) HankiTheme.Apply(this);
    }
    private static void AddTab(TabControl tabs, string title, Control content) { var page = new TabPage(title); page.Controls.Add(content); tabs.TabPages.Add(page); }
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
    private static TextBox Report()
    {
        var report = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = HankiTheme.Surface, ForeColor = HankiTheme.Text, BorderStyle = BorderStyle.None };
        NativeTheme.PadText(report); report.Select(0, 0);
        return report;
    }
}
