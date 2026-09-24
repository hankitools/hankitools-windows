namespace IgezziGuard;

/// <summary>
/// Tune my PC (HANKI-PERF-320): what do you want today → read the current settings → a plan with before and after →
/// you review and apply. Changes go through the review dialog and Recovery; steps only you can take are listed with
/// where to make them. Building the plan changes nothing.
/// </summary>
internal sealed class TunePanel : UserControl
{
    private readonly FlowLayoutPanel scenarios = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 6), Tag = "card" };
    private readonly FlowLayoutPanel syncRow = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 0), Tag = "card", Visible = false };
    private readonly FlowLayoutPanel syncChoices = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Tag = "card" };
    private readonly HankiButton scan = new() { Text = "Scan and suggest changes", Primary = true, AutoSize = true, Margin = new Padding(0, 14, 0, 0), Font = new Font("Segoe UI Semibold", 11f) };
    private readonly Label status = new() { AutoSize = true, Tag = "intro", Margin = new Padding(0, 10, 0, 0), Font = new Font("Segoe UI", 10.5f) };
    private readonly RoundedPanel result = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(26, 20, 26, 22), Visible = false };
    private readonly FlowLayoutPanel resultStack = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Tag = "card" };
    private readonly Label syncHint;
    private const string SyncHintText = "It's on the monitor's box, in its specifications or on-screen menu: G-SYNC, G-SYNC Compatible, FreeSync or Adaptive-Sync.";
    private TunePlan? plan;
    private bool busy, detected;

    public TunePanel()
    {
        Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = Padding.Empty;
        var hero = new RoundedPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(26, 22, 26, 22) };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Tag = "card" };
        var eyebrow = new Label { Text = "TUNE MY PC", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Tag = "accent-performance", Margin = new Padding(0, 0, 0, 6) };
        var headline = new Label { Text = "How do you want to tune your PC today?", AutoSize = true, Font = new Font("Segoe UI Semibold", 20f), Margin = new Padding(0, 0, 0, 10) };
        foreach (var scenario in Enum.GetValues<TuneScenario>()) {
            var tile = new ChoiceTile(TunePlanner.Name(scenario), TunePlanner.Describe(scenario), HankiTheme.PerformanceAccent) { Value = scenario, Margin = new Padding(0, 0, 12, 12) };
            tile.Chosen += () => { syncRow.Visible = TunePlanner.IsGaming(scenario); Invalidate(true); };
            scenarios.Controls.Add(tile);
        }
        var syncQuestion = new Label { Text = "Does your display have G-SYNC or FreeSync?", AutoSize = true, Font = new Font("Segoe UI Semibold", 11.5f), Margin = new Padding(0, 10, 16, 0) };
        foreach (var (label, value) in new[] { ("Yes", AdaptiveSync.Yes), ("No", AdaptiveSync.No), ("Not sure", AdaptiveSync.NotSure) })
            syncChoices.Controls.Add(new ChoiceTile(label, label == "Not sure" ? "Hanki leaves V-Sync and frame caps alone" : label == "Yes" ? "V-Sync and caps set for adaptive sync" : "Settings for a fixed refresh rate",
                HankiTheme.PerformanceAccent, compact: true) { Value = value, Selected = value == AdaptiveSync.NotSure, Margin = new Padding(0, 0, 10, 0) });
        syncHint = new Label { Text = SyncHintText, AutoSize = true, Tag = "intro",
            Font = new Font("Segoe UI", 10f), Margin = new Padding(0, 8, 0, 0) };
        var syncStack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Tag = "card" };
        syncStack.Controls.AddRange([syncQuestion, syncChoices, syncHint]);
        syncRow.Controls.Add(syncStack);
        status.Text = "Choose one, then scan. Hanki reads your settings and shows every change before anything happens.";
        stack.Controls.AddRange([eyebrow, headline, scenarios, syncRow, scan, status]);
        stack.SizeChanged += (_, _) => {
            int width = Math.Max(240, stack.ClientSize.Width - 8);
            headline.MaximumSize = status.MaximumSize = syncHint.MaximumSize = new Size(width, 0);
            FitTiles(width);
        };
        hero.Controls.Add(stack);
        result.Controls.Add(resultStack);
        result.SizeChanged += (_, _) => Wrap(resultStack);
        scan.Click += async (_, _) => await Scan();
        VisibleChanged += async (_, _) => { if (Visible && !detected) { detected = true; await DetectSync(); } };
        var gap = new Panel { Dock = DockStyle.Top, Height = 14, Tag = "gap" };
        Controls.Add(result); Controls.Add(gap); Controls.Add(hero);
    }

    /// <summary>
    /// The choices fill the card in four, two or one columns, all as tall as the longest description needs. The row's
    /// maximum width makes it report its wrapped height, so the card below it isn't cut off.
    /// </summary>
    private void FitTiles(int width)
    {
        var tiles = scenarios.Controls.OfType<ChoiceTile>().ToArray();
        int gap = tiles[0].Margin.Right, least = LogicalToDeviceUnits(230);
        int columns = width >= 4 * (least + gap) ? 4 : width >= 2 * (least + gap) ? 2 : 1;
        int tileWidth = width / columns - gap, height = tiles.Max(t => t.HeightFor(tileWidth));
        foreach (var tile in tiles) tile.Size = new Size(tileWidth, height);
        scenarios.MaximumSize = new Size(width, 0);
    }

    /// <summary>For the UI check's screenshots only: shows a plan built from fixed example data. Nothing is read or changed.</summary>
    internal void Preview(TunePlan example)
    {
        foreach (var tile in scenarios.Controls.OfType<ChoiceTile>()) tile.Selected = Equals(tile.Value, example.Scenario);
        foreach (var tile in syncChoices.Controls.OfType<ChoiceTile>()) tile.Selected = Equals(tile.Value, example.Sync);
        syncRow.Visible = TunePlanner.IsGaming(example.Scenario);
        status.Text = "Example plan (UI check). Nothing was read or changed.";
        ShowPlan(example);
        (Parent as ScrollableControl)?.ScrollControlIntoView(result);
    }

    private TuneScenario? Scenario => scenarios.Controls.OfType<ChoiceTile>().FirstOrDefault(t => t.Selected)?.Value as TuneScenario?;
    private AdaptiveSync Sync => syncChoices.Controls.OfType<ChoiceTile>().FirstOrDefault(t => t.Selected)?.Value as AdaptiveSync? ?? AdaptiveSync.NotSure;

    /// <summary>
    /// Answers the G-SYNC question from NVIDIA's driver when it can: "Yes" when G-SYNC is on for the main display.
    /// Your own choice is never overridden. Read-only.
    /// </summary>
    private async Task DetectSync()
    {
        if (Screen.PrimaryScreen?.DeviceName is not { } display) return;
        var status = await Task.Run(() => Nvidia.AdaptiveSync(display));
        if (status is null || IsDisposed) return;
        bool untouched = Sync == AdaptiveSync.NotSure;
        if (status.On && untouched) foreach (var tile in syncChoices.Controls.OfType<ChoiceTile>()) tile.Selected = Equals(tile.Value, AdaptiveSync.Yes);
        syncHint.Text = status.On ? "NVIDIA's driver reports G-SYNC switched on for this display." :
            status.Supported ? "Your display supports G-SYNC, but it's off in the NVIDIA driver. Choose Yes if you'll switch it on; the plan shows how." : SyncHintText;
    }

    private async Task Scan()
    {
        if (busy) return;
        if (Scenario is not { } scenario) { status.Text = "Choose how you want to tune your PC first."; return; }
        busy = true; scan.Enabled = false;
        status.Text = "Reading display, Windows, graphics driver, processor, memory and storage settings. Nothing is changed…";
        try {
            var inputs = await Collect();
            plan = TunePlanner.Plan(scenario, TunePlanner.IsGaming(scenario) ? TunePlanner.Resolve(Sync, inputs.Sync) : AdaptiveSync.NotSure, inputs);
            status.Text = $"Scanned {DateTime.Now:t}. Nothing was changed.";
            ShowPlan(plan);
        } catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) {
            status.Text = "The scan couldn't finish: " + ex.Message;
        } finally { busy = false; scan.Enabled = true; }
    }

    /// <summary>Reads everything the plan needs (read-only) and saves the findings as the latest Performance check.</summary>
    private static async Task<TuneInputs> Collect()
    {
        var gaming = new GamingState();
        await Task.Run(gaming.Collect);
        var (cpu, memory, storage) = await SystemFactsProbe.Collect(CancellationToken.None);
        IReadOnlyList<NvidiaGlobalSetting>? nvidia = null;
        if (gaming.Graphics!.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia)) { try { nvidia = await Task.Run(Nvidia.ReadGlobal); } catch (NvidiaException) { } }
        IReadOnlyList<GameEntry> games; try { games = GameLibrary.Read(GameLibrary.StorePath); } catch (IOException) { games = []; }
        var now = DateTimeOffset.UtcNow; var power = GraphicsProbe.Power();
        var findings = SystemAnalyzers.Cpu(cpu, gaming.Windows!, power.Portable, power.OnAc, now)
            .Concat(SystemAnalyzers.Memory(memory, now))
            .Concat(SystemAnalyzers.Storage(storage, games, Path.GetPathRoot(Environment.SystemDirectory)?[0] ?? 'C', now)).ToArray();
        var all = gaming.Findings.Concat(findings).GroupBy(f => (f.ModuleId, f.FindingId)).Select(g => g.First()).ToArray();
        try { PerformanceStatus.History.Add(new DiagnosticScan(Guid.NewGuid(), now, DateTimeOffset.UtcNow, 4, 4, false, all)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var background = gaming.Findings.Where(f => f.Metadata.GetValueOrDefault("source") == "Background");
        // G-SYNC on the main display and your games' NVIDIA profiles, for the adaptive-sync and per-game rules.
        AdaptiveSyncStatus? sync = null; IReadOnlyList<GameContext>? contexts = null;
        if (nvidia is not null) {
            if (gaming.Graphics.Displays.FirstOrDefault(d => d.Primary) is { } primary) sync = await Task.Run(() => Nvidia.AdaptiveSync(primary.Name));
            var listed = games.Where(g => !g.Hidden).Take(TunePlanner.GameLimit).ToArray();
            if (listed.Length > 0) {
                try {
                    var profiles = await Task.Run(() => Nvidia.ReadProfiles(listed.Select(g => Path.GetFileName(g.Executable))));
                    contexts = listed.Select(g => new GameContext(g.Name, g.Executable, profiles.Applications.GetValueOrDefault(Path.GetFileName(g.Executable)), profiles.Global, null)).ToArray();
                } catch (NvidiaException) { }
            }
        }
        return new TuneInputs(gaming.Graphics, gaming.Windows!, nvidia, findings.Concat(background).ToArray(), gaming.Amd, sync, contexts);
    }

    private static string AreaName(TuneArea area) => area switch {
        TuneArea.GraphicsDriver => "Graphics driver", TuneArea.Games => "In your games", TuneArea.Background => "Running in the background", _ => area.ToString()
    };

    private void ShowPlan(TunePlan p)
    {
        resultStack.SuspendLayout();
        resultStack.Controls.Clear();
        int changes = p.HankiChanges, steps = p.Steps;
        resultStack.Controls.Add(new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 17f), Margin = new Padding(0, 0, 0, 4),
            Text = changes == 0 && steps == 0 ? "Your PC is already set up for " + TunePlanner.Name(p.Scenario) :
                $"{changes} {(changes == 1 ? "change" : "changes")} Hanki can make" + (steps > 0 ? $" · {steps} {(steps == 1 ? "step" : "steps")} for you" : "") });
        resultStack.Controls.Add(new Label { AutoSize = true, Tag = "intro", Font = new Font("Segoe UI", 10.5f), Margin = new Padding(0, 0, 0, 8),
            Text = $"For {TunePlanner.Name(p.Scenario)}. You pick which changes to apply next; each one is saved in Recovery first so you can undo it." });
        foreach (var group in p.Items.GroupBy(i => i.Area).OrderBy(g => g.Key)) {
            resultStack.Controls.Add(new Label { Text = AreaName(group.Key).ToUpperInvariant(), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Tag = "accent-performance", Margin = new Padding(0, 12, 0, 4) });
            foreach (var item in group) {
                var c = item.Change;
                string line = c.HankiApplies ? $"{c.Setting}:  {c.Current}  →  {c.Recommended}" : $"{c.Setting}:  {c.Recommended}";
                resultStack.Controls.Add(new Label { AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI", 11f), Margin = new Padding(0, 2, 0, 0),
                    Text = (c.HankiApplies ? "●  " : "○  ") + line + (c.Optional ? "   (optional)" : "") + (c.HankiApplies ? "" : "   · you do this") });
            }
        }
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 16, 0, 0), Tag = "card" };
        var apply = new HankiButton { Text = changes > 0 ? "Review and apply" : "Show the steps", Primary = true, AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Enabled = p.Items.Count > 0 };
        var goodToggle = new HankiButton { Text = $"What's already right ({p.AlreadyGood.Count})", Appearance = HankiButtonStyle.Quiet, AutoSize = true, Enabled = p.AlreadyGood.Count > 0 };
        buttons.Controls.AddRange([apply, goodToggle]);
        resultStack.Controls.Add(buttons);
        var good = new Label { AutoSize = true, Tag = "intro", Font = new Font("Segoe UI", 10.5f), Visible = false, Margin = new Padding(0, 10, 0, 0), UseMnemonic = false,
            Text = string.Join("\r\n", p.AlreadyGood.Select(g => $"✓  {AreaName(g.Area)} · {g.Setting}: {g.Current}")) };
        resultStack.Controls.Add(good);
        goodToggle.Click += (_, _) => { good.Visible = !good.Visible; goodToggle.Text = good.Visible ? "Hide what's already right" : $"What's already right ({p.AlreadyGood.Count})"; };
        apply.Click += async (_, _) => await Apply(p);
        result.Visible = true;
        HankiTheme.Apply(result);
        Wrap(resultStack);
        resultStack.ResumeLayout();
    }

    private async Task Apply(TunePlan p)
    {
        if (busy) return;
        busy = true;
        try {
            string name = TunePlanner.Name(p.Scenario);
            await ChangeReview.ReviewAndApply(this, p.Changes, $"Tune my PC: {name}", $"Tune my PC: {name}", text => {
                resultStack.Controls.Clear();
                resultStack.Controls.Add(new Label { AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI", 10.5f), Text = text + "\r\n\r\nScan again to see the new state." });
                HankiTheme.Apply(result); Wrap(resultStack);
            });
        } finally { busy = false; }
    }

    private static void Wrap(Control stack)
    {
        var width = new Size(Math.Max(240, (stack.Parent?.ClientSize.Width ?? 600) - 60), 0);
        foreach (Control c in stack.Controls) if (c is Label label) label.MaximumSize = width;
    }
}
