using System.Diagnostics;
using System.Text;
namespace IgezziGuard;

/// <summary>What the last gaming scan read, shared by the Gaming tabs so a game can be optimized without scanning twice.</summary>
internal sealed class GamingState
{
    internal GraphicsInventory? Graphics;
    internal WindowsGamingSettings? Windows;
    internal NvidiaProfileView? NvidiaGlobal;
    internal string? NvidiaNote;
    internal IReadOnlyList<DiagnosticResult> Findings = [];

    /// <summary>Reads hardware and settings (read-only). NVIDIA problems become a note, not a failure.</summary>
    internal void Collect()
    {
        Graphics = GraphicsProbe.Collect();
        Windows = WindowsGamingProbe.Collect();
        NvidiaGlobal = null; NvidiaNote = null;
        if (Graphics.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia)) {
            try { NvidiaGlobal = Nvidia.ReadProfiles([]).Global; }
            catch (NvidiaException ex) { NvidiaNote = ex.Message; }
        }
        Findings = GamingHealth.Evaluate(Graphics, Windows, NvidiaGlobal, DateTimeOffset.UtcNow);
    }
    internal static CardStatus Status(DiagnosticResult r) => r.Severity switch {
        FindingSeverity.Healthy => CardStatus.Good, FindingSeverity.Warning => CardStatus.Review, FindingSeverity.Critical => CardStatus.Problem, FindingSeverity.Informational => CardStatus.Info, _ => CardStatus.Unknown
    };
    internal string Hardware()
    {
        var text = new StringBuilder();
        if (Graphics is null) return "";
        foreach (var a in Graphics.Adapters.OrderBy(a => a.PreferenceRank))
            text.AppendLine($"GPU: {a.Name} ({GraphicsFacts.VendorName(a.Vendor)}, {(a.LikelyIntegrated ? "integrated" : "dedicated")}, {GraphicsFacts.Memory(a.DedicatedMemory)}), driver {GraphicsFacts.DriverVersion(a.Vendor, a.DriverVersion)}{(a.DriverDate is { } d ? $" from {d:d}" : "")}");
        foreach (var d in Graphics.Displays)
            text.AppendLine($"Display: {d.Name} on {Graphics.AdapterFor(d)?.Name ?? "unknown GPU"}, {GraphicsFacts.Describe(d.Current)} (up to {GraphicsFacts.MaxRefreshAtCurrentResolution(d):0} Hz at this resolution), {d.Connection}{(d.HdrEnabled == true ? ", HDR on" : "")}");
        text.AppendLine($"PC: {(Graphics.Portable == true ? "laptop or tablet" : Graphics.Portable == false ? "desktop" : "unknown type")}, {(Graphics.OnAcPower ? "plugged in" : "on battery")}");
        if (Windows is { } w) text.AppendLine($"Windows: Game Mode {(w.GameMode == false ? "off" : "on")}, power plan {w.PowerPlanName ?? "unknown"}{(w.PowerMode is { } m ? $", power mode {m}" : "")}, processor maximum {w.ProcessorMaximumAc?.ToString() ?? "?"}% plugged in");
        if (NvidiaGlobal is { } nv) text.AppendLine("NVIDIA global: " + string.Join(", ", nv.Values.Select(v => $"{v.Setting.Name} {v.Text}")));
        if (NvidiaNote is not null) text.AppendLine("NVIDIA: " + NvidiaNote);
        if (Graphics.AdlxPresent) text.AppendLine("AMD: the Radeon driver interface (ADLX) is installed; Hanki doesn't read Radeon settings yet.");
        foreach (var note in Graphics.Notes) text.AppendLine("Note: " + note);
        return text.ToString();
    }
}

/// <summary>Gaming → Overview: the Gaming Health Scan and the goal-based review (HANKI-GAME-201/204/213, HANKI-GPU-104/111).</summary>
public sealed class GamingOverviewPanel : ToolPage
{
    private readonly ComboBox goal = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Optimization goal", Margin = new Padding(10, 6, 8, 0) };
    private readonly GamingState state;
    internal GamingOverviewPanel(GamingState state) : base("A read-only check of your gaming setup: display refresh rate, which GPU apps use, Windows Game Mode and power settings, and NVIDIA driver settings. Choosing a goal changes nothing: you review each proposed change first, and every change Hanki makes can be undone in Recovery.")
    {
        this.state = state;
        Button("Scan gaming setup", Scan);
        goal.Items.AddRange(Enum.GetValues<GamingGoal>().Select(g => (object)GamingProfiles.Name(g)).ToArray()); goal.SelectedIndex = 0;
        Bar.Controls.Add(new Label { Text = "Goal", AutoSize = true, Tag = "intro", Margin = new Padding(12, 10, 0, 0) });
        Bar.Controls.Add(goal);
        Button("Review recommendations", Review);
        Button("Windows Graphics settings", () => HealthSettings.Open(this, "ms-settings:display-advancedgraphics", "Settings → System → Display → Graphics"));
        goal.SelectedIndexChanged += (_, _) => { if (!IsBusy && state.Graphics is not null) Output.Text = GamingProfiles.Describe(Goal) + "\r\n\r\nChoose Review recommendations to see what this goal would change."; };
    }
    private GamingGoal Goal => Enum.GetValues<GamingGoal>()[Math.Max(0, goal.SelectedIndex)];

    private async void Scan() => await Run(async token => {
        await Task.Run(state.Collect, token);
        try { PerformanceStatus.History.Add(new DiagnosticScan(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 1, false, state.Findings)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var cards = state.Findings.Select(f => new ResultCard(f.Title, f.Explanation + (f.Severity is FindingSeverity.Warning or FindingSeverity.Critical
                ? $"\r\nNow: {f.Metadata.GetValueOrDefault("current")} · Recommended: {f.Metadata.GetValueOrDefault("recommended")}" : ""), GamingState.Status(f),
            GamingHealth.CanApply(f) ? "Review change" : f.Metadata.GetValueOrDefault("settings") is { } uri ? "Open settings" : null,
            GamingHealth.CanApply(f) ? () => BeginInvoke(() => ReviewOnly(f)) : f.Metadata.GetValueOrDefault("settings") is { } u ? () => HealthSettings.Open(this, u, f.Title) : null)).ToList();
        int opportunities = state.Findings.Count(f => f.Severity is FindingSeverity.Warning or FindingSeverity.Critical);
        var report = "Gaming Health Scan • " + DateTimeOffset.Now.ToString("g") + "\r\nRead-only; nothing was changed.\r\n\r\n" + state.Hardware() + "\r\n" +
            string.Join("\r\n\r\n", state.Findings.Select(f => $"{f.Title} · {f.Severity}\r\n{f.Explanation}\r\n{f.Evidence}"));
        return Diagnosis.From(report, cards, opportunities == 0 ? "No gaming configuration problems found" : $"{opportunities} optimization {(opportunities == 1 ? "opportunity" : "opportunities")} found");
    });

    private void ReviewOnly(DiagnosticResult finding)
    {
        if (state.Graphics is null) return;
        _ = ChangeReview.ReviewAndApply(this, GamingProfiles.Propose(GamingGoal.Balanced, state.Graphics, [finding], null), finding.Title, null, text => Output.Text = text);
    }
    private async void Review()
    {
        if (state.Graphics is null) { Output.Text = "Scan your gaming setup first; recommendations come from what the scan finds."; return; }
        var changes = GamingProfiles.Propose(Goal, state.Graphics, state.Findings, null);
        await ChangeReview.ReviewAndApply(this, changes, GamingProfiles.Name(Goal) + " for this PC", "Gaming setup: " + GamingProfiles.Name(Goal), text => Output.Text = text);
    }
}

/// <summary>Gaming → Games: the local game list, per-game settings, conflicts and Optimize This Game (HANKI-GAME-205/208/210, HANKI-GPU-107).</summary>
public sealed class GamesPanel : ToolPage
{
    private readonly ComboBox games = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Game", Margin = new Padding(0, 6, 8, 0) };
    private readonly ComboBox goal = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Goal for this game", Margin = new Padding(0, 6, 8, 0) };
    private readonly GamingState state;
    private IReadOnlyList<GameEntry> list = [];
    internal GamesPanel(GamingState state) : base("Games Hanki found in Steam, Epic, GOG and other launchers' records on this PC, plus any you add. Nothing is looked up online. Settings are changed for one game at a time, in its own driver profile, and only after you review them.")
    {
        this.state = state;
        Bar.Controls.Add(games);
        goal.Items.AddRange(Enum.GetValues<GamingGoal>().Select(g => (object)GamingProfiles.Name(g)).ToArray()); goal.SelectedIndex = 0;
        Bar.Controls.Add(goal);
        Button("Optimize this game", Optimize);
        Button("Find installed games", Find);
        Button("Add a game…", Add);
        Button("Remove from list", Remove);
        Button("Remove Hanki's NVIDIA profile", RemoveProfile);
        games.SelectedIndexChanged += (_, _) => { if (!IsBusy) ShowGame(); };
        goal.SelectedIndexChanged += (_, _) => SaveGoal();
        VisibleChanged += (_, _) => { if (Visible && list.Count == 0) LoadList(); };
    }
    private GameEntry? Selected => games.SelectedIndex >= 0 && games.SelectedIndex < VisibleGames().Count ? VisibleGames()[games.SelectedIndex] : null;
    private IReadOnlyList<GameEntry> VisibleGames() => list.Where(g => !g.Hidden).ToArray();

    private void LoadList(Guid? select = null)
    {
        try { list = GameLibrary.Read(GameLibrary.StorePath); } catch (IOException ex) { Output.Text = ex.Message; return; }
        games.Items.Clear();
        foreach (var g in VisibleGames()) games.Items.Add($"{g.Name}{(File.Exists(g.Executable) ? "" : " (not found)")}");
        if (games.Items.Count == 0) { Output.Text = "No games yet. Choose Find installed games, or Add a game… to pick its .exe."; return; }
        int index = select is { } id ? VisibleGames().ToList().FindIndex(g => g.Id == id) : 0;
        games.SelectedIndex = Math.Max(0, index);
    }
    private void Save(IReadOnlyList<GameEntry> updated, Guid? select = null)
    {
        try { GameLibrary.Write(GameLibrary.StorePath, updated); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Output.Text = "The game list couldn't be saved: " + ex.Message; return; }
        LoadList(select);
    }
    private async void Find()
    {
        await Run(async token => {
            var found = await Task.Run(() => {
                if (!Nvidia.Available) return GameLibrary.Detect(null, token);
                // NVIDIA's profile database knows game executables, which picks the right .exe among helpers.
                using var session = new Nvidia.Session();
                return GameLibrary.Detect(exe => session.FindApplication(exe) != IntPtr.Zero, token);
            }, token);
            var merged = GameLibrary.Merge(GameLibrary.Read(GameLibrary.StorePath), found);
            GameLibrary.Write(GameLibrary.StorePath, merged);
            return $"Found {found.Count} installed {(found.Count == 1 ? "game" : "games")}. The list now has {merged.Count(g => !g.Hidden)}.\r\nIf a game is missing, use Add a game… and pick its .exe.";
        });
        var message = Output.Text;
        LoadList();
        Output.Text = message;
    }

    private void Add()
    {
        using var picker = new OpenFileDialog { Title = "Pick the game's .exe", Filter = "Programs (*.exe)|*.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var existing = list.FirstOrDefault(g => g.Executable.Equals(picker.FileName, StringComparison.OrdinalIgnoreCase));
        var entry = existing is null ? new GameEntry(Guid.NewGuid(), Path.GetFileNameWithoutExtension(picker.FileName), picker.FileName, "Added by you", GamingGoal.Balanced) : existing with { Hidden = false };
        Save(list.Where(g => g.Id != entry.Id).Append(entry).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), entry.Id);
    }
    private void Remove()
    {
        if (Selected is not { } game || !Review($"Remove {game.Name} from Hanki's game list? Its settings aren't changed, and Find installed games won't add it back.")) return;
        Save(list.Select(g => g.Id == game.Id ? g with { Hidden = true } : g).ToArray());
    }
    private void SaveGoal()
    {
        if (Selected is not { } game) return;
        var chosen = Enum.GetValues<GamingGoal>()[Math.Max(0, goal.SelectedIndex)];
        if (game.Goal == chosen) return;
        try { GameLibrary.Write(GameLibrary.StorePath, list = list.Select(g => g.Id == game.Id ? g with { Goal = chosen } : g).ToArray()); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private async void RemoveProfile()
    {
        if (Selected is not { } game) return;
        string exe = Path.GetFileName(game.Executable);
        if (!Review($"Remove the NVIDIA profile Hanki created for {exe}? The game then uses your global NVIDIA settings. NVIDIA's own game profiles are never removed.")) return;
        await Run(async token => await Task.Run(() => {
            try { return Nvidia.RemoveHankiProfile(exe) ? $"Removed Hanki's NVIDIA profile for {exe}." : $"{exe} has no NVIDIA profile."; }
            catch (NvidiaException ex) { return ex.Message; }
        }, token));
    }

    private GameContext Context(GameEntry game)
    {
        NvidiaProfileView? profile = null, global = state.NvidiaGlobal;
        if (Nvidia.Available) {
            try { var read = Nvidia.ReadProfiles([Path.GetFileName(game.Executable)]); global = read.Global; profile = read.Applications.Values.FirstOrDefault(); }
            catch (NvidiaException) { }
        }
        var windows = state.Windows ?? WindowsGamingProbe.Collect();
        var preference = windows.GpuPreferences.FirstOrDefault(p => p.Application.Equals(game.Executable, StringComparison.OrdinalIgnoreCase))?.Preference;
        return new GameContext(game.Name, game.Executable, profile, global, preference);
    }
    private async void ShowGame()
    {
        if (Selected is not { } game) return;
        goal.SelectedIndex = (int)game.Goal;
        await Run(async token => await Task.Run(() => {
            if (state.Graphics is null) state.Collect();
            var context = Context(game);
            var text = new StringBuilder($"{game.Name}\r\n{game.Executable}{(File.Exists(game.Executable) ? "" : "\r\nThis file isn't there any more; the game may have been moved or uninstalled.")}\r\nFound in: {game.Source}\r\n\r\n");
            text.AppendLine($"Windows GPU choice: {(context.WindowsPreference is { } p ? WindowsGamingParsing.PreferenceText(p) : "Let Windows decide")}");
            if (context.NvidiaGlobal is not null) {
                text.AppendLine(context.Nvidia is null ? "NVIDIA profile: none, so your global NVIDIA settings apply." : $"NVIDIA profile: {context.Nvidia.ProfileName}{(context.Nvidia.Predefined ? " (made by NVIDIA)" : "")}");
                foreach (var v in context.Nvidia?.Values ?? context.NvidiaGlobal.Values)
                    text.AppendLine($"  {v.Setting.Name}: {v.Text} ({(context.Nvidia is null ? "global" : NvidiaSettings.SourceText(v.Source))})");
            }
            double refresh = state.Graphics!.Displays.Where(d => d.Primary).Select(d => d.Current.RefreshHz).FirstOrDefault();
            var conflicts = GamingProfiles.Conflicts(context, refresh);
            if (conflicts.Count > 0) text.AppendLine("\r\nFrame-rate limits and sync:\r\n" + string.Join("\r\n", conflicts.Select(c => "• " + c)));
            text.AppendLine($"\r\nGoal: {GamingProfiles.Name(game.Goal)}. {GamingProfiles.Describe(game.Goal)}\r\nChoose Optimize this game to review what would change.");
            return text.ToString();
        }, token));
    }

    private async void Optimize()
    {
        if (Selected is not { } game) { Output.Text = "Choose a game first, or add one."; return; }
        if (!File.Exists(game.Executable)) { Output.Text = "The game's .exe isn't there any more. Add it again with Add a game…"; return; }
        var chosen = Enum.GetValues<GamingGoal>()[Math.Max(0, goal.SelectedIndex)];
        IReadOnlyList<ProposedChange> changes = [];
        await Run(async token => await Task.Run(() => {
            state.Collect();
            changes = GamingProfiles.Propose(chosen, state.Graphics!, state.Findings, Context(game));
            return $"Analyzed {game.Name} for {GamingProfiles.Name(chosen)}.";
        }, token));
        await ChangeReview.ReviewAndApply(this, changes, $"{game.Name}: {GamingProfiles.Name(chosen)}", $"Optimize {game.Name} ({GamingProfiles.Name(chosen)})", text => Output.Text = text);
    }
}

/// <summary>Reviews proposed changes, applies the approved ones through Recovery, and records a Performance session.</summary>
internal static class ChangeReview
{
    internal static async Task ReviewAndApply(Control owner, IReadOnlyList<ProposedChange> changes, string title, string? sessionName, Action<string> report)
    {
        // Guardrail: only documented change kinds with a Recovery entry are ever applied.
        changes = changes.Where(Guardrails.Allowed).ToArray();
        if (changes.Count == 0) {
            report("No change recommended. Hanki found nothing on this PC that this goal would improve, so it leaves your settings as they are.");
            return;
        }
        var approved = ChangeReviewDialog.Show(owner, title, changes);
        if (approved is null) { report("Nothing was changed."); return; }
        if (approved.Count == 0) { report("Nothing was selected, so nothing was changed."); return; }
        var journal = WindowsSettings.Journal(); var backend = new WindowsSettings();
        var lines = new List<string>(); var applied = new List<Guid>();
        foreach (var change in approved) {
            report($"Applying: {change.Setting}…");
            try {
                var before = await backend.Read(change.Kind!, change.Target!, CancellationToken.None);
                if (before == change.After) { lines.Add($"• {change.Setting}: already {change.Recommended}."); continue; }
                // An earlier Hanki change to this setting is replaced rather than stacked, so Recovery keeps one entry whose
                // "before" is your original value. Only a change still in place is replaced; anything else is left for you.
                var open = journal.Read().LastOrDefault(e => e.Kind == change.Kind && e.Target == change.Target && e.Status is not ("Undone" or ChangeJournal.NotApplied));
                if (open is not null) {
                    if (open.Status != "Applied" || open.After != before) { lines.Add($"✗ {change.Setting}: not changed. An earlier change to it is pending or was changed outside Hanki; check it in Recovery first."); continue; }
                    await journal.Undo(open.Id, CancellationToken.None);
                    before = open.Before;
                    if (before == change.After) { lines.Add($"✓ {change.Setting}: back to your original value, {change.Recommended}."); continue; }
                }
                await journal.Apply(change.Kind!, change.Target!, before, change.After!, CancellationToken.None);
                var entry = journal.Read().Last(e => e.Kind == change.Kind && e.Target == change.Target && e.Status == "Applied");
                if (change.Kind == "Display mode" && !KeepDisplayDialog.Keep(owner, change.Recommended)) {
                    await journal.Undo(entry.Id, CancellationToken.None);
                    lines.Add($"• {change.Setting}: switched back to {change.Current}.");
                    continue;
                }
                applied.Add(entry.Id);
                lines.Add($"✓ {change.Setting}: {change.Current} → {change.Recommended}");
            } catch (IOException ex) { lines.Add($"✗ {change.Setting}: not changed. {ex.Message}"); }
        }
        if (applied.Count > 0 && sessionName is not null) {
            var none = new PerformanceMeasurement(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, double>(), "No measurement");
            try { PerformanceSessionsPanel.Store.Add(new PerformanceSession(Guid.NewGuid(), sessionName.Length > 120 ? sessionName[..120] : sessionName, DateTimeOffset.UtcNow, none, applied, null, SessionOutcome.Measured, string.Join("\r\n", lines))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        report((applied.Count > 0 ? $"Changed {applied.Count} {(applied.Count == 1 ? "setting" : "settings")}.\r\n\r\n" : "Nothing was changed.\r\n\r\n") + string.Join("\r\n", lines) +
            (applied.Count > 0 ? "\r\n\r\nEach change is in Recovery, where you can undo it, and in History → Performance sessions. To see whether it helped, measure the same game before and after in Performance Lab." : ""));
    }
}

/// <summary>"Keep this display mode?" with a 15-second automatic revert, as Windows does.</summary>
internal static class KeepDisplayDialog
{
    internal static bool Keep(IWin32Window owner, string mode)
    {
        using var dialog = new Form { Text = "Keep this display mode?", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20), TopMost = true };
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        int seconds = 15;
        var text = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Text = $"The display now uses {mode}.\r\nIf the picture looks wrong, do nothing: it switches back in {seconds} seconds." };
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 16, 0, 0) };
        var keep = new HankiButton { Text = "Keep changes", Primary = true, AutoSize = true, DialogResult = DialogResult.OK };
        var revert = new HankiButton { Text = "Revert", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([keep, revert]); layout.Controls.AddRange([text, buttons]); dialog.Controls.Add(layout);
        dialog.AcceptButton = keep; dialog.CancelButton = revert;
        using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) => {
            if (--seconds <= 0) { timer.Stop(); dialog.DialogResult = DialogResult.Cancel; return; }
            text.Text = $"The display now uses {mode}.\r\nIf the picture looks wrong, do nothing: it switches back in {seconds} seconds.";
        };
        HankiTheme.Apply(dialog); timer.Start();
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
}

/// <summary>The list of proposed changes: current and recommended values, where each change is made and why. Nothing runs until approved.</summary>
internal static class ChangeReviewDialog
{
    /// <returns>The approved changes Hanki applies, or null when cancelled.</returns>
    internal static IReadOnlyList<ProposedChange>? Show(IWin32Window owner, string title, IReadOnlyList<ProposedChange> changes)
    {
        var (dialog, approved) = Build(title, changes);
        using (dialog) return dialog.ShowDialog(owner) == DialogResult.OK ? approved() : null;
    }
    internal static (Form Dialog, Func<IReadOnlyList<ProposedChange>> Approved) Build(string title, IReadOnlyList<ProposedChange> changes)
    {
        const int width = 600;
        var dialog = new Form { Text = "Review changes", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20) };
        var bold = new Font(dialog.Font, FontStyle.Bold); dialog.Disposed += (_, _) => bold.Dispose();
        var scroll = new Panel { AutoScroll = true, Width = width + 30, Height = Math.Min(520, 120 + changes.Count * 120), Margin = Padding.Empty };
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Location = Point.Empty };
        Label Text(string text, string? tag = null, int top = 3) => new() { Text = text, AutoSize = true, MaximumSize = new Size(width - 30, 0), Tag = tag, Margin = new Padding(0, top, 0, 0), UseMnemonic = false };
        var header = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Top };
        header.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(dialog.Font.FontFamily, 13f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) });
        header.Controls.Add(new Label { Text = "Hanki saves the current value of each change first, so you can undo it in Recovery. Optional changes aren't ticked; changes marked “you change it” are for you to make.",
            AutoSize = true, MaximumSize = new Size(width, 0), Tag = "intro", Margin = new Padding(0, 0, 0, 8) });
        var choices = new List<(CheckBox Box, ProposedChange Change)>();
        foreach (var change in changes) {
            var card = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Tag = "card", MinimumSize = new Size(width, 0), Padding = new Padding(14, 10, 14, 12), Margin = new Padding(0, 10, 0, 0) };
            string source = change.Source switch { ChangeSource.Nvidia => "NVIDIA", ChangeSource.Amd => "AMD", ChangeSource.Display => "Display", ChangeSource.Game => "Game", _ => "Windows" };
            if (change.HankiApplies) {
                var box = new CheckBox { Text = $"{change.Setting}  ·  {source}", Font = bold, AutoSize = true, Checked = !change.Optional, Margin = new Padding(0, 0, 0, 2) };
                card.Controls.Add(box); choices.Add((box, change));
            } else card.Controls.Add(new Label { Text = $"{change.Setting}  ·  {source}  ·  you change it", Font = bold, AutoSize = true, UseMnemonic = false });
            card.Controls.Add(Text($"Now: {change.Current}   →   Recommended: {change.Recommended}{(change.Optional ? "   (optional)" : "")}"));
            card.Controls.Add(Text(change.Why, "intro", 4));
            if (!change.HankiApplies && change.Manual is { Length: > 0 } manual) card.Controls.Add(Text(manual, "status-review", 4));
            if (change.SettingsUri is { } uri) {
                var open = new HankiButton { Text = "Open settings", AutoSize = true, Appearance = HankiButtonStyle.Quiet, Margin = new Padding(0, 6, 0, 0) };
                open.Click += (_, _) => { try { using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { } };
                card.Controls.Add(open);
            }
            layout.Controls.Add(card);
        }
        scroll.Controls.Add(layout);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        var apply = new HankiButton { Text = "Apply selected", Primary = true, AutoSize = true, DialogResult = DialogResult.OK, Enabled = choices.Count > 0 };
        var cancel = new HankiButton { Text = choices.Count > 0 ? "Cancel" : "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([apply, cancel]);
        var outer = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        outer.Controls.AddRange([header, scroll, buttons]);
        dialog.Controls.Add(outer); dialog.CancelButton = cancel;
        HankiTheme.Apply(dialog);
        return (dialog, () => choices.Where(c => c.Box.Checked).Select(c => c.Change).ToArray());
    }
}
