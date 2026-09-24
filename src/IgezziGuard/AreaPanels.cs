namespace IgezziGuard;

/// <summary>Home: the two ways into Hanki. Fix My PC when something is wrong; Performance when it works but could be faster.</summary>
internal sealed class HomePanel : UserControl
{
    private readonly Label systemStatus = Status(), performanceStatus = Status();
    private readonly DiagnosticHistory scans = new(Path.Combine(SecurityPaths.Root, "diagnostic-history.json"));
    public HomePanel(Action<string> navigate, Action startFixMyPc)
    {
        Dock = DockStyle.Fill; AutoScroll = true; Padding = new Padding(0, 4, 8, 16);
        var cards = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var system = Area(ProductArea.System, "Find what's wrong and fix it safely", "Diagnose, repair, maintain and protect Windows.",
            systemStatus, "Problem  →  Diagnosis  →  Repair you approve  →  Verification",
            ("Fix My PC", startFixMyPc, true), ("Open System", () => navigate("System overview"), false));
        var performance = Area(ProductArea.Performance, "Understand what limits performance", "Measure, analyze and optimize gaming and hardware performance.",
            performanceStatus, "Baseline  →  Measure  →  Optimize with your approval  →  Measure again  →  Keep or revert",
            ("Open Performance", () => navigate("Performance overview"), true), ("Performance Lab", () => navigate("Performance Lab"), false));
        cards.Controls.Add(system, 0, 0); cards.Controls.Add(performance, 1, 0);
        // Side by side when there is room, stacked otherwise.
        void Fit() {
            bool narrow = ClientSize.Width < 820;
            cards.ColumnStyles[1].Width = narrow ? 0 : 50; cards.ColumnStyles[0].Width = narrow ? 100 : 50;
            cards.SetCellPosition(performance, new TableLayoutPanelCellPosition(narrow ? 0 : 1, narrow ? 1 : 0));
        }
        SizeChanged += (_, _) => Fit();

        var links = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 0) };
        foreach (var page in new[] { "Recovery", "System actions", "Performance sessions", "Help & community" }) {
            var link = new HankiButton { Text = page + "  →", AutoSize = true, Appearance = HankiButtonStyle.Quiet, Margin = new Padding(0, 0, 6, 0) };
            link.Click += (_, _) => navigate(page); links.Controls.Add(link);
        }
        Controls.Add(links); Controls.Add(cards);
        VisibleChanged += (_, _) => { if (Visible) RefreshStatus(); };
        RefreshStatus(); Fit();
    }
    private static Label Status() => new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(0, 0, 0, 10), Tag = "card" };

    private static RoundedPanel Area(ProductArea area, string headline, string description, Label status, string workflow, params (string Text, Action Action, bool Primary)[] actions)
    {
        var card = new RoundedPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(26, 22, 26, 22), Margin = new Padding(0, 0, 14, 14), MinimumSize = new Size(0, 290) };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Tag = "card", Margin = Padding.Empty };
        var eyebrow = new Label { Text = Navigation.AreaName(area), AutoSize = true, Font = new Font("Segoe UI", 8.25f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6),
            Tag = area == ProductArea.Performance ? "accent-performance" : "accent" };
        var title = new Label { Text = headline, AutoSize = true, Font = new Font("Segoe UI Semibold", 16f), Margin = new Padding(0, 0, 0, 6) };
        var body = new Label { Text = description, AutoSize = true, Tag = "intro", Margin = new Padding(0, 0, 0, 14) };
        var flow = new Label { Text = workflow, AutoSize = true, Tag = "intro", Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 0, 0, 16) };
        var buttons = new FlowLayoutPanel { AutoSize = true, Tag = "card", Margin = Padding.Empty, WrapContents = true };
        foreach (var (text, action, primary) in actions) {
            var button = new HankiButton { Text = text, Primary = primary, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            button.Click += (_, _) => action(); buttons.Controls.Add(button);
        }
        stack.Controls.AddRange([eyebrow, title, body, status, flow, buttons]);
        stack.SizeChanged += (_, _) => { var wrap = new Size(Math.Max(200, stack.ClientSize.Width - 8), 0); foreach (var label in new[] { title, body, status, flow }) label.MaximumSize = wrap; };
        card.Controls.Add(stack);
        return card;
    }

    internal void RefreshStatus()
    {
        try {
            var latest = scans.Read().OrderByDescending(s => s.Ended).FirstOrDefault();
            systemStatus.Text = latest is null ? "No scan yet. Fix My PC checks Windows in one read-only pass." : SystemStatus(latest);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { systemStatus.Text = "Saved scan history couldn't be read."; }
        performanceStatus.Text = PerformanceStatus.Describe(PerformanceStatus.Latest());
    }
    internal static string SystemStatus(DiagnosticScan scan)
    {
        int count = scan.Results.Count(r => r.Severity is FindingSeverity.Warning or FindingSeverity.Critical);
        string when = scan.Ended.ToLocalTime().ToString("g");
        return count == 0 ? $"Last scan {when}: nothing needs attention." : $"{count} system {(count == 1 ? "recommendation" : "recommendations")} from your last scan ({when}).";
    }
}

/// <summary>The latest Hanki Performance check, kept apart from system scan history.</summary>
internal static class PerformanceStatus
{
    internal static DiagnosticHistory History => new(Path.Combine(SecurityPaths.Root, "performance-checks.json"));
    internal static DiagnosticScan? Latest() { try { return History.Read().OrderByDescending(s => s.Ended).FirstOrDefault(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; } }
    internal static string Describe(DiagnosticScan? latest)
    {
        if (latest is null) return "Not checked yet. Start with a gaming check or a monitoring run.";
        int count = latest.Results.Count(r => r.Severity is FindingSeverity.Warning or FindingSeverity.Critical);
        return count == 0 ? $"Last check {latest.Ended.ToLocalTime():g}: no optimization opportunities found."
            : $"{count} optimization {(count == 1 ? "opportunity" : "opportunities")} from your last check ({latest.Ended.ToLocalTime():g}).";
    }
}

/// <summary>Performance overview: what each Performance section is for, and how Hanki optimizes safely.</summary>
internal sealed class PerformanceOverviewPanel : UserControl
{
    public PerformanceOverviewPanel(Action<string> navigate)
    {
        Dock = DockStyle.Fill; AutoScroll = true; Padding = new Padding(0, 4, 8, 16);
        var hero = new RoundedPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(26, 22, 26, 22), MinimumSize = new Size(0, 170) };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Tag = "card" };
        var eyebrow = new Label { Text = "HOW HANKI OPTIMIZES", AutoSize = true, Font = new Font("Segoe UI", 8.25f, FontStyle.Bold), Tag = "accent-performance", Margin = new Padding(0, 0, 0, 6) };
        var headline = new Label { Text = "Measure first. Change one thing. Measure again.", AutoSize = true, Font = new Font("Segoe UI Semibold", 16f), Margin = new Padding(0, 0, 0, 8) };
        var text = new Label { AutoSize = true, Tag = "intro", Margin = Padding.Empty, Text =
            "Performance findings are observations and opportunities, not faults: a healthy PC can still have room to optimize. " +
            "Hanki records a baseline, saves your current settings, applies only changes you approve, measures again, and lets you keep or revert. " +
            "It never overclocks from here, never turns off security features, and doesn't apply internet “FPS tweaks” without evidence." };
        // HANKI-PERF-313: one read-only check across every area, summarised per area.
        var check = new HankiButton { Text = "Run performance check", Primary = true, AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        var status = new Label { AutoSize = true, Margin = new Padding(0, 10, 0, 0), Text = PerformanceStatus.Describe(PerformanceStatus.Latest()), AccessibleName = "Performance check results" };
        check.Click += async (_, _) => {
            check.Enabled = false; status.Text = "Checking gaming setup, processor, memory and storage (read-only)…";
            try { status.Text = await PerformanceCheck.RunAsync(CancellationToken.None); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { status.Text = "The check couldn't finish: " + ex.Message; }
            finally { check.Enabled = true; }
        };
        stack.Controls.AddRange([eyebrow, headline, text, check, status]);
        stack.SizeChanged += (_, _) => { var wrap = new Size(Math.Max(200, stack.ClientSize.Width - 8), 0); headline.MaximumSize = text.MaximumSize = status.MaximumSize = wrap; };
        hero.Controls.Add(stack);

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        foreach (var page in new[] { "Gaming", "GPU", "CPU", "Memory", "Storage", "Performance Lab" }) {
            var item = Navigation.Find(page)!;
            cards.Controls.Add(new HankiCard(item.Icon, item.Label, item.Introduction, () => navigate(page), HankiTheme.PerformanceAccent) { Margin = new Padding(0, 0, 14, 14) });
        }
        void FitCards() {
            int width = Math.Max(260, ClientSize.Width - Padding.Horizontal - (VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth));
            int columns = width >= 900 ? 3 : width >= 560 ? 2 : 1;
            foreach (Control card in cards.Controls) card.Width = Math.Max(220, (width - 14 * columns) / columns);
        }
        SizeChanged += (_, _) => FitCards();
        var section = new Label { Text = "PERFORMANCE AREAS", Dock = DockStyle.Top, AutoSize = false, Height = 44, Tag = "intro",
            Font = new Font("Segoe UI", 8.25f, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(2, 0, 0, 8) };
        // HANKI-GAME-211 / HANKI-PERF-314: what Hanki deliberately won't do, and why.
        var refused = new RoundedPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(26, 18, 26, 18) };
        var refusedText = new Label { AutoSize = true, Tag = "intro", Dock = DockStyle.Top, Text = string.Join("\r\n", Guardrails.NotRecommended.Select(g => $"•  {g.Tweak}: {g.Why}")) };
        refused.Controls.Add(refusedText);
        refused.SizeChanged += (_, _) => refusedText.MaximumSize = new Size(Math.Max(200, refused.ClientSize.Width - refused.Padding.Horizontal), 0);
        var refusedTitle = new Label { Text = "TWEAKS HANKI WON'T MAKE", Dock = DockStyle.Top, AutoSize = false, Height = 44, Tag = "intro",
            Font = new Font("Segoe UI", 8.25f, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(2, 0, 0, 8) };
        Controls.Add(refused); Controls.Add(refusedTitle);
        Controls.Add(cards); Controls.Add(section); Controls.Add(hero);
        FitCards();
    }
}

/// <summary>The overview's read-only check across gaming, CPU, memory and storage, saved as the latest Performance check.</summary>
internal static class PerformanceCheck
{
    internal static async Task<string> RunAsync(CancellationToken token)
    {
        var gaming = new GamingState();
        await Task.Run(gaming.Collect, token);
        var (cpu, memory, storage) = await SystemFactsProbe.Collect(token);
        var now = DateTimeOffset.UtcNow; var power = GraphicsProbe.Power();
        IReadOnlyList<GameEntry> games; try { games = GameLibrary.Read(GameLibrary.StorePath); } catch (IOException) { games = []; }
        var areas = new (string Name, IReadOnlyList<DiagnosticResult> Findings)[] {
            ("Gaming", gaming.Findings), ("CPU", SystemAnalyzers.Cpu(cpu, gaming.Windows!, power.Portable, power.OnAc, now)), ("Memory", SystemAnalyzers.Memory(memory, now)),
            ("Storage", SystemAnalyzers.Storage(storage, games, Path.GetPathRoot(Environment.SystemDirectory)?[0] ?? 'C', now))
        };
        // Findings from different areas can share ids ("power-mode"); keep one of each for the saved check.
        var all = areas.SelectMany(a => a.Findings).GroupBy(f => (f.ModuleId, f.FindingId)).Select(g => g.First()).ToArray();
        try { PerformanceStatus.History.Add(new DiagnosticScan(Guid.NewGuid(), now, DateTimeOffset.UtcNow, areas.Length, areas.Length, false, all)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        string Line(string name, IReadOnlyList<DiagnosticResult> findings) {
            var open = findings.Where(f => f.Severity is FindingSeverity.Warning or FindingSeverity.Critical).ToArray();
            return $"{name}: " + (open.Length == 0 ? "no opportunities found" : $"{open.Length} {(open.Length == 1 ? "opportunity" : "opportunities")} ({string.Join("; ", open.Select(f => f.Title))})");
        }
        return string.Join("\r\n", areas.Select(a => Line(a.Name, a.Findings)).Prepend($"GPU: {gaming.Graphics?.HighPerformance?.Name ?? "not found"}")) + "\r\nOpen each area for details and to review changes.";
    }
}

/// <summary>A defined destination whose tools are still being built: says what will be here and that nothing runs yet.</summary>
internal sealed class PlannedPanel : UserControl
{
    public PlannedPanel(string title, string description)
    {
        Dock = DockStyle.Fill; AutoScroll = true; Padding = new Padding(0, 4, 8, 16);
        var card = new RoundedPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(26, 22, 26, 22), MinimumSize = new Size(0, 120) };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Tag = "card" };
        var eyebrow = new Label { Text = "COMING IN A LATER UPDATE", AutoSize = true, Font = new Font("Segoe UI", 8.25f, FontStyle.Bold), Tag = "accent-performance", Margin = new Padding(0, 0, 0, 6) };
        var heading = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 14f), Margin = new Padding(0, 0, 0, 8) };
        var body = new Label { Text = description, AutoSize = true, Tag = "intro", Margin = Padding.Empty };
        stack.Controls.AddRange([eyebrow, heading, body]);
        stack.SizeChanged += (_, _) => body.MaximumSize = heading.MaximumSize = new Size(Math.Max(200, stack.ClientSize.Width - 8), 0);
        card.Controls.Add(stack); Controls.Add(card);
    }
}

/// <summary>System actions: scans, repairs and recorded Windows changes from Hanki System, newest first.</summary>
public sealed class SystemActionsPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    public SystemActionsPanel() : base("Everything Hanki System has done on this PC: Full System Scans, repairs and recorded Windows changes, newest first. Undo supported changes in Recovery. Performance tests are listed separately under Performance sessions.")
    {
        Button("Refresh", ShowTimeline);
        VisibleChanged += (_, _) => { if (Visible && !IsBusy) ShowTimeline(); };
    }
    private void ShowTimeline()
    {
        try {
            var scans = new DiagnosticHistory(Path.Combine(SecurityPaths.Root, "diagnostic-history.json")).Read();
            var repairs = new RepairAudit(Path.Combine(SecurityPaths.Root, "repair-audit.json")).Read();
            var changes = WindowsSettings.Journal().Read();
            Output.Text = SystemActions.Format(SystemActions.Timeline(scans, repairs, changes));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) {
            Output.Text = "System history couldn't be read: " + ex.Message + "\r\nOriginal files were preserved.";
        }
    }
}

/// <summary>Performance sessions: measurements and optimization tests with their baseline, changes tested and outcome.</summary>
public sealed class PerformanceSessionsPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    private readonly ComboBox list = new() { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Performance session" };
    private IReadOnlyList<PerformanceSession> sessions = [];
    internal static PerformanceSessionStore Store => new(Path.Combine(SecurityPaths.Root, "performance-sessions.json"));
    public PerformanceSessionsPanel() : base("Each session keeps a baseline measurement, the changes tested and the measurement afterwards. Hanki only calls a change an improvement when the runs are comparable and the difference is larger than normal variation. Sessions stay on this PC.")
    {
        list.Margin = new Padding(0, 6, 8, 0); Bar.Controls.Add(list);
        Button("Open session", Open);
        Button("Restore settings", async () => {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= sessions.Count) return;
            var session = sessions[list.SelectedIndex];
            if (session.ChangesTested.Count == 0) { Output.Text = "This session didn't change any settings."; return; }
            if (!Review($"Put back the settings from before “{session.Name}”? Each change is undone through Recovery; a setting changed again since then is left alone.")) return;
            await Run(async token => {
                var journal = WindowsSettings.Journal(); var lines = new List<string>();
                foreach (var change in journal.Read().Where(c => session.ChangesTested.Contains(c.Id)).OrderByDescending(c => c.At)) {
                    if (change.Status != "Applied") { lines.Add($"• {change.Kind}: already {change.Status.ToLowerInvariant()}."); continue; }
                    try { await journal.Undo(change.Id, token); lines.Add($"✓ {change.Kind}: back to {change.Before}"); }
                    catch (IOException ex) { lines.Add($"✗ {change.Kind}: {ex.Message}"); }
                }
                return "Restoring settings\r\n\r\n" + string.Join("\r\n", lines);
            });
        });
        Button("Refresh", LoadSessions);
        Button("Remove session", () => {
            if (list.SelectedIndex < 0 || !Review("Remove this session from Performance history? Any settings it changed stay as they are; undo them in Recovery.")) return;
            try { Store.Remove(sessions[list.SelectedIndex].Id); LoadSessions(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Output.Text = "Couldn't remove it: " + ex.Message; }
        });
        list.SelectedIndexChanged += (_, _) => Open();
        VisibleChanged += (_, _) => { if (Visible && !IsBusy) LoadSessions(); };
    }
    private void LoadSessions()
    {
        try {
            sessions = Store.Read(); list.Items.Clear();
            foreach (var s in sessions) list.Items.Add($"{s.Created.ToLocalTime():g} · {s.Name}");
            if (sessions.Count > 0) list.SelectedIndex = 0;
            else Output.Text = "No performance sessions yet.\r\n\r\nIn Performance Lab → Monitor, run a measurement and choose Save to Performance sessions. Optimization tests will be saved here too, with the settings they changed.";
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Output.Text = ex.Message; }
    }
    private void Open()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= sessions.Count) return;
        var s = sessions[list.SelectedIndex];
        IReadOnlyList<SettingChange> changes = [];
        try { changes = WindowsSettings.Journal().Read().Where(c => s.ChangesTested.Contains(c.Id)).ToArray(); } catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { }
        Output.Text = $"{s.Name}\r\n{s.Created.ToLocalTime():f}\r\n\r\n{PerformanceComparison.Describe(s.Outcome)}\r\n\r\n" +
            PerformanceComparison.Table(s.Baseline, s.After) + "\r\n\r\n" +
            (s.ChangesTested.Count == 0 ? "Changes tested: none (measurement only)." : "Changes tested:\r\n" + string.Join("\r\n", changes.Select(c => $"• {c.Kind}: {c.Target}: {c.Before} → {c.After} ({c.Status})")) +
                "\r\nTo restore the previous settings, undo these changes in Recovery.") +
            (s.Summary.Length > 0 ? "\r\n\r\n" + s.Summary : "");
    }
}
