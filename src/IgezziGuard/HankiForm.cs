namespace IgezziGuard;

public sealed class HankiForm : Form
{
    private readonly WorkspacePages tabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox scan = Report();
    private readonly TextBox network = Report();
    private readonly HankiButton file = new() { Text = "Scan file", AutoSize = true, Primary = true };
    private readonly HankiButton folder = new() { Text = "Scan folder", AutoSize = true };
    private readonly HankiButton connect = new() { Text = "Run network checks", AutoSize = true, Primary = true };
    private readonly HankiButton cancel = new() { Text = "Cancel", Dock = DockStyle.Bottom, Enabled = false };
    private readonly Label status = new() { Text = "Ready — no checks run", Dock = DockStyle.Bottom, Height = 32 };
    private CancellationTokenSource? running;
    private readonly MaintainPanel maintain = new();
    private readonly AppsPanel apps = new();
    private readonly PerformancePanel performance = new();
    private readonly AssistantPanel assistant = new();
    private readonly AiChatPanel ai = new();
    private readonly UsagePanel usage = new();
    private readonly StartupPanel startup = new();
    private readonly DiagnosticPanel diagnose = new("Read recent Event Logs", "Reads up to 50 recent warning/error/critical events per System and Application log (7 days). Reports can contain private data. No logs cleared or settings changed.", ReadOnlyDiagnostics.CrashLogs);
    private readonly DiagnosticPanel defender = new("Audit Defender settings", "Reads Defender status and configured exclusions using built-in PowerShell. No changes or auto-elevation. Exclusion paths may contain private data.", ReadOnlyDiagnostics.Defender, ResultPresentation.Defender);
    private readonly DiagnosticPanel networkDeep = new("Run Wi-Fi / latency checks", "Reads Wi-Fi interfaces and sends 10 ICMP probes each to 1.1.1.1 and up to four active IPv4 gateways. Remote endpoint sees your source IP. Reports can contain SSID/BSSID/MAC and IP addresses. No settings changes.", ReadOnlyDiagnostics.Network);
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
    private ToolPage[] ExtraPages => [duplicates, startupFolders, longPerformance, tuning, networkTools, defenderTools, dumps, guidance, recovery];

    public HankiForm()
    {
        Text = "Hanki Tools • " + AppInfo.Version;
        Size = new Size(1320, 880);
        MinimumSize = new Size(1120, 740);
        Font = new Font("Segoe UI", 11);
        BackColor = Color.FromArgb(18, 27, 40);
        ForeColor = Color.WhiteSmoke;
        StartPosition = FormStartPosition.CenterScreen;
        void Navigate(string name) { var page = tabs.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == name); if (page is not null) tabs.SelectedTab = page; }
        Page("Home").Controls.Add(new Dashboard(Navigate));
        var shield = Page("Shield · experimental");
        shield.Controls.Add(scan);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48 };
        bar.Controls.AddRange([file, folder]); shield.Controls.Add(bar);
        scan.Text = "Experimental: bundled EICAR test signature and simple heuristics, not a malware signature feed.\r\nRead-only scanner. No quarantine actions. Files over 512 MB are skipped; archives are not unpacked.\r\nFindings require human review.";
        var net = Page("Connect"); net.Controls.Add(network);
        var netBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48 };
        netBar.Controls.Add(connect); net.Controls.Add(netBar);
        network.Text = "Lists local network adapters, addresses, gateways and DNS servers.\r\n\r\nRun sends DNS requests for example.com and TCP probes to example.com:443 and 1.1.1.1:443.\r\nThese endpoints can observe your source IP. No report or files are uploaded.\r\n\r\nNo settings changes, resets, speed tests or automatic repairs.";
        var history = Page("Scan history"); var historyText = Report(); history.Controls.Add(historyText);
        tabs.SelectedIndexChanged += (_, _) => {
            if (tabs.SelectedTab == history) {
                try {
                    var entries = new HistoryStore().GetEntries();
                    historyText.Text = entries.Count == 0 ? "No scans recorded yet. Run a file or folder scan in Shield to create a summary." : string.Join("\r\n\r\n", entries.Select(x =>
                        $"{x.FinishedAt:g} | {x.Target}\r\n{x.FilesScanned} files, {x.DetectionCount} findings, {x.Skipped} skipped, {x.Errors} errors"));
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
        var diagnosePage = Page("Diagnose"); diagnosePage.Controls.Add(timeline);
        AttachDetail(diagnosePage, "Crash timeline", "Recent Event Logs", diagnose);
        timeline.PrepareRequested += Prepare;
        var diagnoseTabs = diagnosePage.Controls.OfType<TabControl>().Single();
        AddTab(diagnoseTabs, "Dump analysis", dumps); AddTab(diagnoseTabs, "Guided checks", guidance);
        AttachDetail(shield, "Scanner", "Defender audit", defender);
        shield.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
        AttachDetail(net, "Basic checks", "Wi-Fi / latency", networkDeep);
        AddTab(net.Controls.OfType<TabControl>().Single(), "Advanced / DNS repair", networkTools);
        AddTab(shield.Controls.OfType<TabControl>().Single(), "Defender controls / alerts", defenderTools);
        var performancePage = performance.Parent as TabPage;
        if (performancePage is not null) {
            AttachDetail(performancePage, "Snapshot / pagefile", "30-second sample", sampling);
            var performanceTabs = performancePage.Controls.OfType<TabControl>().Single();
            AddTab(performanceTabs, "Long monitoring / saved runs", longPerformance); AddTab(performanceTabs, "Power tuning", tuning);
        }
        Page("Recovery").Controls.Add(recovery);
        Page("Help & community").Controls.Add(new SupportPanel());
        foreach (var extra in ExtraPages) extra.PrepareRequested += Prepare;
        diagnose.PrepareRequested += Prepare; defender.PrepareRequested += Prepare;
        networkDeep.PrepareRequested += Prepare; sampling.PrepareRequested += Prepare;
        performance.PrepareRequested += Prepare;
        var prepareScan = new HankiButton { Text = "Prepare for ChatGPT…", Appearance = HankiButtonStyle.Quiet, AutoSize = true };
        prepareScan.Click += (_, _) => { if (running is null) Prepare(scan.Text); };
        bar.Controls.Add(prepareScan);
        var prepareNetwork = new HankiButton { Text = "Prepare for ChatGPT…", Appearance = HankiButtonStyle.Quiet, AutoSize = true };
        prepareNetwork.Click += (_, _) => { if (running is null) Prepare(network.Text); };
        netBar.Controls.Add(prepareNetwork);
        maintain.BusyChanged += busy => { file.Enabled = folder.Enabled = connect.Enabled = !busy; };
        var sidebar = new FlowLayoutPanel { Tag = "pine", Dock = DockStyle.Left, Width = 226, FlowDirection = FlowDirection.TopDown,
            AutoScroll = true, WrapContents = false, Padding = new Padding(12, 8, 12, 12) };
        sidebar.Controls.Add(new BrandHeader());
        var navigation = new List<(HankiButton Button, TabPage Page)>();
        foreach (var name in new[] { "Home", "Diagnose", "Performance", "Maintain", "Connect", "Shield · experimental", "Assistant", "Recovery", "Scan history", "Help & community" }) {
            if (name is "Diagnose" or "Assistant") sidebar.Controls.Add(new Label {
                Text = name == "Diagnose" ? "YOUR PC" : "SUPPORT & HISTORY", AutoSize = true,
                Font = new Font("Segoe UI", 9), Margin = new Padding(12, 16, 0, 6) });
            bool support = name is "Assistant" or "Recovery" or "Scan history" or "Help & community";
            var page = tabs.TabPages.Cast<TabPage>().Single(p => p.Text == name);
            var button = new HankiButton { Text = name == "Shield · experimental" ? "Shield" : name,
                Width = 192, Height = support ? 32 : 38, Margin = new Padding(2, 1, 2, 1), AccessibleName = "Open " + name,
                IconKind = name, Appearance = support ? HankiButtonStyle.Quiet : HankiButtonStyle.Navigation,
                Font = new Font("Segoe UI", support ? 10 : 11) };
            button.Click += (_, _) => tabs.SelectedTab = page;
            navigation.Add((button, page)); sidebar.Controls.Add(button);
        }
        var quick = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false, Margin = Padding.Empty };
        var quickToggle = new HankiButton { Text = "›  Quick access", Width = 192, Height = 32, Appearance = HankiButtonStyle.Quiet,
            Margin = new Padding(2, 18, 2, 2), AccessibleName = "Expand quick access", Font = new Font("Segoe UI", 10) };
        quickToggle.Click += (_, _) => {
            quick.Visible = !quick.Visible; quickToggle.Text = quick.Visible ? "⌄  Quick access" : "›  Quick access";
            quickToggle.AccessibleName = quick.Visible ? "Collapse quick access" : "Expand quick access";
        };
        sidebar.Controls.Add(quickToggle); sidebar.Controls.Add(quick);
        foreach (var item in new[] { ("PowerShell (Admin)", "powershell"), ("CMD (Admin)", "cmd"), ("File Explorer", "explorer"), ("Task Manager", "task-manager"), ("Windows Settings", "settings"), ("Event Viewer", "event-viewer") }) {
            var shortcut = new HankiButton { Text = item.Item1, Width = 188, Height = 30, Appearance = HankiButtonStyle.Quiet, Font = new Font("Segoe UI", 9) };
            shortcut.Click += (_, _) => DesktopShortcuts.Open(this, item.Item2); quick.Controls.Add(shortcut);
        }
        sidebar.Controls.Add(new Label { Text = "hanki.tools   /   " + AppInfo.Version + "\nAdmin shortcuts use Windows UAC.", AutoSize = true, Font = new Font("Segoe UI", 9), Margin = new Padding(6, 20, 3, 3) });
        var about = new HankiButton { Text = "About & privacy", Appearance = HankiButtonStyle.Quiet, Width = 192, Height = 32, Font = new Font("Segoe UI", 10) };
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
        var title = new Label { Text = "Overview", Dock = DockStyle.Top, Height = 58, Font = new Font("Segoe UI", 21, FontStyle.Bold), Padding = new Padding(22, 12, 0, 0) };
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
        foreach (var shortcut in new[] { ("Windows / Task Manager", "task-manager"), ("Windows / Event Viewer", "event-viewer"), ("Windows / Settings", "settings"), ("Windows / File Explorer", "explorer") }) {
            var item = shortcut;
            routes.Add(new ToolLauncher.Route(item.Item1, () => DesktopShortcuts.Open(this, item.Item2)));
        }
        void FindTool() { using var launcher = new ToolLauncher(routes); launcher.ShowDialog(this); }
        var search = new HankiButton { Text = "Find a tool   Ctrl+K", Dock = DockStyle.Right, Width = 205, AccessibleName = "Find a tool, Control K" };
        search.Click += (_, _) => FindTool();
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.F1) { Navigate("Help & community"); e.SuppressKeyPress = true; } if (e.Control && e.KeyCode == Keys.K) { FindTool(); e.SuppressKeyPress = true; } };
        var header = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(0, 6, 14, 8) };
        title.Dock = DockStyle.Fill; header.Controls.Add(title); header.Controls.Add(search);
        var introduction = new Label { Dock = DockStyle.Top, AutoSize = false, Padding = new Padding(22, 0, 22, 14),
            Font = new Font("Segoe UI", 10.5f), Tag = "intro", AccessibleName = "About this module" };
        void FitIntroduction() {
            int desired = introduction.GetPreferredSize(new Size(Math.Max(120, introduction.Width), 0)).Height;
            if (introduction.Height != desired) introduction.Height = desired;
        }
        introduction.SizeChanged += (_, _) => FitIntroduction();
        var content = new Panel { Dock = DockStyle.Fill }; content.Controls.Add(tabs); content.Controls.Add(introduction); content.Controls.Add(header);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 4, 12, 4) };
        status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
        cancel.Dock = DockStyle.Right; cancel.Width = 110; cancel.Text = "Cancel task";
        footer.Controls.Add(status); footer.Controls.Add(cancel);
        void RefreshNavigation() {
            foreach (var item in navigation) item.Button.Selected = tabs.SelectedTab == item.Page;
            title.Text = tabs.SelectedTab?.Text == "Home" ? "Welcome to Hanki Tools" : tabs.SelectedTab?.Text;
            introduction.Text = tabs.SelectedTab?.Text switch {
                "Home" => "Your Windows toolbox. Inspect, maintain and troubleshoot your PC from one place.",
                "Diagnose" => "Explore crash events, inspect dumps and follow guided checks to narrow down possible causes and choose your next troubleshooting step.",
                "Performance" => "Measure memory and system activity, compare monitoring sessions and review power settings to understand slowdowns before making changes.",
                "Maintain" => "Find large or duplicate files, review installed apps and manage startup entries to reclaim storage and reduce unnecessary startup activity.",
                "Connect" => "Check your connection, compare DNS and trace network routes to investigate slow or unreliable access and review repair options.",
                "Shield · experimental" => "Review Microsoft Defender protection, run scans and inspect findings to see what needs attention. Hanki’s separate file scanner is experimental.",
                "Assistant" => "Prepare and redact diagnostic reports, then use optional AI chat to help explain the evidence and explore next steps.",
                "Recovery" => "Review recorded changes and undo supported actions when you need to return to a previous configuration.",
                "Help & community" => "Find guides, join the community, prepare a bug report and check which version you are running.",
                "Scan history" => "Review past file-scan summaries to see what was checked, when it ran and how many findings were reported.",
                _ => ""
            };
            FitIntroduction();
        }
        tabs.SelectedIndexChanged += (_, _) => RefreshNavigation(); RefreshNavigation();
        Controls.Add(content); Controls.Add(sidebar); Controls.Add(footer);
        var iconStream = typeof(HankiForm).Assembly.GetManifestResourceStream("IgezziGuard.Brand.hanki.ico");
        if (iconStream is not null) { using (iconStream) { using var branded = new Icon(iconStream); Icon = (Icon)branded.Clone(); } }
        HankiTheme.Apply(this);
        defenderTools.ProtectionAlert += message => status.Text = message;
        void CancelTasks() {
            running?.Cancel(); maintain.Cancel(); apps.Cancel(); performance.Cancel(); diagnose.Cancel(); defender.Cancel(); networkDeep.Cancel();
            sampling.Cancel(); ai.Cancel(); timeline.Cancel(); usage.Stop(); defenderTools.StopMonitoring(); foreach (var page in ExtraPages) page.Cancel();
        }
        string[] ActiveTasks() => new[] {
            (running is not null, "Scan / network check"), (maintain.IsBusy, "Files"), (apps.IsBusy, "Apps"), (performance.IsBusy || sampling.IsBusy || longPerformance.IsBusy, "Performance"),
            (diagnose.IsBusy || timeline.IsBusy || dumps.IsBusy, "Diagnose"), (defender.IsBusy || defenderTools.IsBusy || defenderTools.MonitoringBusy, "Defender"),
            (networkDeep.IsBusy || networkTools.IsBusy, "Connect"), (ai.IsBusy, "AI request"), (usage.IsBusy, "App observation"),
            (duplicates.IsBusy || startupFolders.IsBusy || tuning.IsBusy || guidance.IsBusy || recovery.IsBusy, "Maintenance / recovery")
        }.Where(t => t.Item1).Select(t => t.Item2).ToArray();
        var taskStatus = new Label { Dock = DockStyle.Right, Width = 310, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        footer.Controls.Add(taskStatus); footer.Controls.SetChildIndex(taskStatus, 1);
        cancel.Text = "Cancel tasks"; cancel.Width = 130;
        var taskTimer = new System.Windows.Forms.Timer { Interval = 500 };
        taskTimer.Tick += (_, _) => {
            var active = ActiveTasks(); cancel.Enabled = active.Length > 0;
            taskStatus.Text = active.Length == 0 ? "" : "Running: " + string.Join(", ", active);
        };
        HankiTheme.Apply(taskStatus);
        taskTimer.Start(); Disposed += (_, _) => taskTimer.Dispose();
        cancel.Click += (_, _) => CancelTasks();
        file.Click += async (_, _) => { using var d = new OpenFileDialog(); if (d.ShowDialog(this) == DialogResult.OK) await Scan(d.FileName); };
        folder.Click += async (_, _) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) await Scan(d.SelectedPath); };
        connect.Click += async (_, _) => await Run(async token => {
            network.Clear();
            var progress = new Progress<string>(line => network.AppendText(line + "\r\n"));
            await Task.Run(() => NetworkDiagnostics.Run(progress, token), token);
        });
        FormClosing += (_, e) => { usage.Stop(); defenderTools.StopMonitoring(); if (running is not null || maintain.IsBusy || apps.IsBusy || performance.IsBusy || diagnose.IsBusy || defender.IsBusy || networkDeep.IsBusy || sampling.IsBusy || ai.IsBusy || usage.IsBusy || timeline.IsBusy || ExtraPages.Any(p => p.IsBusy) || defenderTools.MonitoringBusy) { e.Cancel = true; running?.Cancel(); maintain.Cancel(); apps.Cancel(); performance.Cancel(); diagnose.Cancel(); defender.Cancel(); networkDeep.Cancel(); sampling.Cancel(); ai.Cancel(); timeline.Cancel(); foreach (var extra in ExtraPages) extra.Cancel(); status.Text = "Cancelling; close again when finished."; } };
    }
    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        if (IsHandleCreated && !Disposing) HankiTheme.Apply(this);
    }
    private static void AddTab(TabControl tabs, string title, Control content) { var page = new TabPage(title); page.Controls.Add(content); tabs.TabPages.Add(page); }
    private Task Scan(string path) => Run(async token => {
        scan.Clear();
        var progress = new Progress<ScanProgress>(p => status.Text = $"{p.FilesScanned:N0} scanned · {p.Detections} findings · {p.Errors} errors");
        var summary = await Task.Run(() => new ScannerService(SignatureDatabase.Load()).ScanAsync(path, progress, token), token);
        scan.Text = $"Finished: {summary.FilesScanned} files; {summary.Skipped} skipped; {summary.Errors} errors.\r\nNo findings does not prove safety.\r\n\r\n" +
            string.Join("\r\n\r\n", summary.Findings.Select(f => $"{f.Severity}: {f.DetectionName}\r\n{f.FilePath}\r\n{f.Details}\r\nSHA-256: {f.Sha256}"));
        try { new HistoryStore().Add(summary); }
        catch (IOException ex) { scan.AppendText("\r\n\r\nScan completed, but its history could not be saved: " + ex.Message); }
    });
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (running is not null || maintain.IsBusy) return;
        using var cts = new CancellationTokenSource(); running = cts;
        file.Enabled = folder.Enabled = connect.Enabled = maintain.Enabled = false; cancel.Enabled = true;
        status.Text = "Running…";
        try { await action(cts.Token); status.Text = "Finished — review results and limitations."; }
        catch (OperationCanceledException) { status.Text = "Cancelled — results incomplete; no safety conclusion."; }
        catch (Exception ex) { status.Text = "Check failed — results incomplete."; MessageBox.Show(this, ex.Message, "Hanki Tools"); }
        finally { running = null; file.Enabled = folder.Enabled = connect.Enabled = maintain.Enabled = true; cancel.Enabled = false; }
    }
    private TabPage Page(string title) { var page = new TabPage(title) { BackColor = BackColor, Padding = new Padding(14) }; tabs.TabPages.Add(page); return page; }
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
    private static TextBox Report() => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BackColor = Color.FromArgb(24, 36, 52), ForeColor = Color.FromArgb(227, 238, 248), BorderStyle = BorderStyle.None };
}
