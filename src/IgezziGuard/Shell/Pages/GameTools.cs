using System.Diagnostics;
using System.Text;
using System.Windows;
using ComboBox = System.Windows.Controls.ComboBox;

namespace IgezziGuard.Shell;

/// <summary>Gaming → Games, NVIDIA and AMD Radeon as report pages: the game list with per-game settings, and the two vendors' driver settings.</summary>
internal static class GameTools
{
    private static string Lines(string text) => text.Replace("\r\n", "\n");

    // ───────────────────────── Games ─────────────────────────

    internal static ReportView Games(IShellServices shell, GamingState state)
    {
        var games = Pick.Box("Game", 300);
        var goal = Pick.Box("Goal for this game", 160);
        foreach (var g in Enum.GetValues<GamingGoal>()) goal.Items.Add(GamingProfiles.Name(g)); goal.SelectedIndex = 0;
        IReadOnlyList<GameEntry> list = [];
        ReportView view = null!;
        bool filling = false;

        IReadOnlyList<GameEntry> Visible() => list.Where(g => !g.Hidden).ToArray();
        GameEntry? Selected() => games.SelectedIndex >= 0 && games.SelectedIndex < Visible().Count ? Visible()[games.SelectedIndex] : null;

        void LoadList(Guid? select = null)
        {
            try { list = GameLibrary.Read(GameLibrary.StorePath); } catch (IOException ex) { view.ShowPlain(ex.Message); return; }
            filling = true; games.Items.Clear();
            foreach (var g in Visible()) games.Items.Add($"{g.Name}{(File.Exists(g.Executable) ? "" : " (not found)")}");
            filling = false;
            if (games.Items.Count == 0) { view.ShowPlain("No games yet. Choose Find installed games, or Add a game… to pick its .exe."); return; }
            int index = select is { } id ? Visible().ToList().FindIndex(g => g.Id == id) : 0;
            games.SelectedIndex = Math.Max(0, index);
        }
        void Save(IReadOnlyList<GameEntry> updated, Guid? select = null)
        {
            try { GameLibrary.Write(GameLibrary.StorePath, updated); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { view.ShowPlain("The game list couldn't be saved: " + ex.Message); return; }
            LoadList(select);
        }

        GameContext Context(GameEntry game)
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

        async void ShowGame()
        {
            if (filling || Selected() is not { } game) return;
            filling = true; goal.SelectedIndex = (int)game.Goal; filling = false;
            try {
                view.ShowPlain(await Task.Run(() => {
                    if (state.Graphics is null) state.Collect();
                    var context = Context(game);
                    var text = new StringBuilder($"{game.Name}\n{game.Executable}{(File.Exists(game.Executable) ? "" : "\nThis file isn't there any more; the game may have been moved or uninstalled.")}\nFound in: {game.Source}\n\n");
                    text.AppendLine(game.TacticalVision > 0 ? $"Tactical Vision: {game.TacticalVision}% Digital Vibrance while focused (keep Hanki open)." : "Tactical Vision: off. Choose Tactical Vision to boost color saturation for this game.");
                    text.AppendLine($"Windows GPU choice: {(context.WindowsPreference is { } p ? WindowsGamingParsing.PreferenceText(p) : "Let Windows decide")}");
                    if (context.NvidiaGlobal is not null) {
                        text.AppendLine(context.Nvidia is null ? "NVIDIA profile: none, so your global NVIDIA settings apply." : $"NVIDIA profile: {context.Nvidia.ProfileName}{(context.Nvidia.Predefined ? " (made by NVIDIA)" : "")}");
                        foreach (var v in context.Nvidia?.Values ?? context.NvidiaGlobal.Values)
                            text.AppendLine($"  {v.Setting.Name}: {v.Text} ({(context.Nvidia is null ? "global" : NvidiaSettings.SourceText(v.Source))})");
                    }
                    double refresh = state.Graphics!.Displays.Where(d => d.Primary).Select(d => d.Current.RefreshHz).FirstOrDefault();
                    var conflicts = GamingProfiles.Conflicts(context, refresh, state.Background?.RtssLimit);
                    if (conflicts.Count > 0) text.AppendLine("\nFrame-rate limits and sync:\n" + string.Join("\n", conflicts.Select(c => "• " + c)));
                    text.AppendLine($"\nGoal: {GamingProfiles.Name(game.Goal)}. {GamingProfiles.Describe(game.Goal)}\nChoose Optimize this game to review what would change.");
                    return text.ToString();
                }));
            } catch (Exception ex) when (ex is IOException or InvalidOperationException or NvidiaException) { view.ShowPlain("The game's settings couldn't be read: " + ex.Message); }
        }

        view = new ReportView(shell, "Games",
            "Games Hanki found in Steam, Epic, GOG and other launchers' records on this PC, plus any you add. Nothing is looked up online. Review optimization changes per game, or enable Tactical Vision to boost your NVIDIA display's color saturation only while that game is focused.", [
            new("Optimize this game", Primary: true, Text: async (_, token) => {
                if (Selected() is not { } game) return "Choose a game first, or add one.";
                if (!File.Exists(game.Executable)) return "The game's .exe isn't there any more. Add it again with Add a game…";
                var chosen = Enum.GetValues<GamingGoal>()[Math.Max(0, goal.SelectedIndex)];
                IReadOnlyList<ProposedChange> changes = await Task.Run(() => { state.Collect(); return GamingProfiles.Propose(chosen, state.Graphics!, state.Findings, Context(game)); }, token);
                string result = "";
                await ChangeReview.ReviewAndApply(shell.DialogOwner, changes, $"{game.Name}: {GamingProfiles.Name(chosen)}", $"Optimize {game.Name} ({GamingProfiles.Name(chosen)})", text => { result = Lines(text); view.Say(result); });
                return result;
            }),
            new("Tactical Vision…", Open: () => {
                if (Selected() is not { } game) { view.ShowPlain("Choose a game first, or add one."); return; }
                if (Dialogs.TacticalVision(game) is not { } strength) return;
                Save(list.Select(g => g.Id == game.Id ? g with { TacticalVision = strength } : g).ToArray(), game.Id);
            }),
            new("Launch and measure", Diagnose: async (_, token) => {
                if (Selected() is not { } game) throw new InvalidOperationException("Choose a game first, or add one.");
                if (!File.Exists(game.Executable)) throw new InvalidOperationException("The game's .exe isn't there any more. Add it again with Add a game…");
                if (!Blocks.Confirm("Start the game and measure it", $"Start {game.Name} and measure it?\n\nHanki waits for the game's window, gives it {LaunchMeasure.WarmupSeconds} seconds to load, then measures {LaunchMeasure.MeasureSeconds / 60} minutes of play. " +
                    "Play as you normally would. Closing the game or choosing Cancel stops early and keeps what was measured. Frame rates need Hanki to run as administrator.", "Start and measure")) throw new OperationCanceledException();
                var progress = new Progress<string>(text => view.Say(text)); IProgress<string> report = progress;
                try { using var started = Process.Start(new ProcessStartInfo(game.Executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(game.Executable) ?? "" }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) {
                    return Diagnosis.From($"{game.Name} couldn't be started: {ex.Message}", [new("The game didn't start", ex.Message + " Start it from its launcher, then measure it in Performance Lab → Monitor.", CardStatus.Unknown)]);
                }
                report.Report($"Starting {game.Name}. Waiting for its window, up to {LaunchMeasure.WindowTimeout.TotalMinutes:0} minutes; some games open their launcher first.");
                using var target = await LaunchMeasure.WaitForGame(Path.GetFileNameWithoutExtension(game.Executable), token);
                if (target is null)
                    return Diagnosis.From($"{game.Name} didn't open a window.", [new("No game window", $"{game.Name} didn't open a window within {LaunchMeasure.WindowTimeout.TotalMinutes:0} minutes. If it starts through a launcher, start it there, then measure it in Performance Lab → Monitor.", CardStatus.Unknown)]);
                for (int left = LaunchMeasure.WarmupSeconds; left > 0; left--) {
                    report.Report($"{game.Name} is running. Measuring starts in {left} seconds, after loading: get into the game and play as usual.");
                    await Task.Delay(1000, token);
                }
                var run = await PerformanceRecorder.Record(LaunchMeasure.MeasureSeconds, target, progress, token);
                var summary = LabMonitorPanel.Summary(run, null);
                var store = PerformanceSessionsPanel.Store;
                PerformanceSession? previous = null;
                try { previous = LaunchMeasure.Previous(store.Read(), game.Name); } catch (IOException) { }
                IReadOnlyList<SettingChange> changes = [];
                try { changes = WindowsSettings.Journal().Read(); } catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { }
                var bottleneck = BottleneckEngine.Analyze(run);
                var session = LaunchMeasure.Session(game.Name, BottleneckEngine.Measurement(run), previous, changes, bottleneck.Diagnosis, bottleneck.Limiter);
                try { store.Add(session); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                if (session.After is null)
                    return summary with { Report = "First measured run of this game; your next run is compared with it. Saved to History → Performance sessions.\r\n\r\n" + summary.Report };
                var tested = changes.Where(c => session.ChangesTested.Contains(c.Id)).Select(c => $"• {c.Kind}: {c.Target}: {c.Before} → {c.After}").ToArray();
                string outcome = PerformanceComparison.Describe(session.Outcome);
                var cards = summary.Cards.Prepend(new ResultCard("Compared with your last run", $"{outcome} Last run: {previous!.Created.ToLocalTime():g}. " +
                    (tested.Length == 0 ? "No settings were changed in between." : $"{tested.Length} {(tested.Length == 1 ? "setting was" : "settings were")} changed in between."), CardStatus.Info)).ToList();
                return Diagnosis.From("COMPARED WITH YOUR LAST RUN\r\n" + outcome + "\r\n" + PerformanceComparison.Table(session.Baseline, session.After) +
                    "\r\n\r\nChanges made in between (from Recovery):\r\n" + (tested.Length == 0 ? "none" : string.Join("\r\n", tested)) + "\r\n\r\n" + summary.Report, cards, summary.Headline);
            }),
            new("Find installed games", Text: async (_, token) => {
                var found = await Task.Run(() => {
                    if (!Nvidia.Available) return GameLibrary.Detect(null, token);
                    // NVIDIA's profile database knows game executables, which picks the right .exe among helpers.
                    using var session = new Nvidia.Session();
                    return GameLibrary.Detect(exe => session.FindApplication(exe) != IntPtr.Zero, token);
                }, token);
                var merged = GameLibrary.Merge(GameLibrary.Read(GameLibrary.StorePath), found);
                GameLibrary.Write(GameLibrary.StorePath, merged);
                LoadList();
                var message = $"Found {found.Count} installed {(found.Count == 1 ? "game" : "games")}. The list now has {merged.Count(g => !g.Hidden)}.\nIf a game is missing, use Add a game… and pick its .exe.";
                view.ShowPlain(message); return message;
            }),
            new("Add a game…", Open: () => {
                var picker = new Microsoft.Win32.OpenFileDialog { Title = "Pick the game's .exe", Filter = "Programs (*.exe)|*.exe", CheckFileExists = true };
                if (picker.ShowDialog() != true) return;
                var existing = list.FirstOrDefault(g => g.Executable.Equals(picker.FileName, StringComparison.OrdinalIgnoreCase));
                var entry = existing is null ? new GameEntry(Guid.NewGuid(), Path.GetFileNameWithoutExtension(picker.FileName), picker.FileName, "Added by you", GamingGoal.Balanced) : existing with { Hidden = false };
                Save(list.Where(g => g.Id != entry.Id).Append(entry).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), entry.Id);
            }),
            new("Remove from list", Open: () => {
                if (Selected() is not { } game || !Blocks.Confirm("Remove from the game list", $"Remove {game.Name} from Hanki's game list? Its settings aren't changed, and Find installed games won't add it back.", "Remove")) return;
                Save(list.Select(g => g.Id == game.Id ? g with { Hidden = true } : g).ToArray());
            }),
            new("Remove Hanki's NVIDIA profile", Text: async (_, token) => {
                if (Selected() is not { } game) return "Choose a game first.";
                string exe = Path.GetFileName(game.Executable);
                if (!Blocks.Confirm("Remove the NVIDIA profile", $"Remove the NVIDIA profile Hanki created for {exe}? The game then uses your global NVIDIA settings. NVIDIA's own game profiles are never removed.", "Remove profile", true)) return "Nothing was changed.";
                return await Task.Run(() => {
                    try { return Nvidia.RemoveHankiProfile(exe) ? $"Removed Hanki's NVIDIA profile for {exe}." : $"{exe} has no NVIDIA profile."; }
                    catch (NvidiaException ex) { return ex.Message; }
                }, token);
            }),
        ]);
        var visionStatus = UiKit.Text(TacticalVisionController.Status, 13.5, UiKit.Res("TextMuted"), wrap: true); visionStatus.Margin = new Thickness(0, 8, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(visionStatus, "Tactical Vision status");
        void UpdateVision() => view.Dispatcher.BeginInvoke(() => visionStatus.Text = TacticalVisionController.Status);
        view.InsertNote(visionStatus);
        view.Loaded += (_, _) => { TacticalVisionController.StatusChanged += UpdateVision; visionStatus.Text = TacticalVisionController.Status; };
        view.Unloaded += (_, _) => TacticalVisionController.StatusChanged -= UpdateVision;
        view.AddToBar(games, first: true); view.AddToBar(goal, first: false);
        games.SelectionChanged += (_, _) => ShowGame();
        goal.SelectionChanged += (_, _) => {
            if (filling || Selected() is not { } game) return;
            var chosen = Enum.GetValues<GamingGoal>()[Math.Max(0, goal.SelectedIndex)];
            if (game.Goal == chosen) return;
            try { GameLibrary.Write(GameLibrary.StorePath, list = list.Select(g => g.Id == game.Id ? g with { Goal = chosen } : g).ToArray()); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
        view.Loaded += (_, _) => { if (list.Count == 0) LoadList(); };
        return view;
    }

    // ───────────────────────── NVIDIA ─────────────────────────

    internal static ReportView NvidiaPage(IShellServices shell, GamingState state)
    {
        const string NotReady = "NVIDIA settings aren't available on this PC. They need an NVIDIA graphics card with its driver installed.";
        var preset = Pick.Box("NVIDIA preset", 240);
        IReadOnlyList<NvidiaGlobalSetting> current = [];
        IReadOnlyList<NvidiaUserPreset> saved = [];
        IReadOnlyList<NvidiaPreset> presets = [];
        ReportView view = null!;
        bool loaded = false;

        NvidiaPreset? Selected() => preset.SelectedIndex >= 0 && preset.SelectedIndex < presets.Count ? presets[preset.SelectedIndex] : null;
        double RefreshHz() => state.Graphics?.Displays.Where(d => d.Primary).Select(d => d.Current.RefreshHz).DefaultIfEmpty(60).First() ?? 60;

        void FillPresets(string? select = null)
        {
            presets = NvidiaPresets.BuiltIn(RefreshHz()).Concat(saved.Select(NvidiaPresets.FromUser)).ToArray();
            preset.Items.Clear();
            foreach (var p in presets) preset.Items.Add(p.BuiltIn ? p.Name : "My preset: " + p.Name);
            int index = select is null ? 0 : presets.ToList().FindIndex(p => !p.BuiltIn && p.Name == select);
            if (preset.Items.Count > 0) preset.SelectedIndex = Math.Max(0, index);
        }
        Diagnosis Summary(string? note)
        {
            var changed = current.Where(c => !c.IsDefault).ToArray();
            var report = new StringBuilder("NVIDIA global 3D settings • " + DateTimeOffset.Now.ToString("g") + "\r\nRead-only; nothing was changed.\r\n\r\n");
            foreach (var c in current) report.AppendLine($"{c.Setting.Name}: {c.Text}{(c.IsDefault ? " (NVIDIA default)" : " (changed from NVIDIA's default)")}");
            report.AppendLine("\r\nGames with their own NVIDIA profile value keep it. NVIDIA's “Ultra” low latency and background frame limit aren't in its public SDK, so Hanki doesn't change them.");
            if (note is not null) report.AppendLine("\r\nNote: " + note);
            var cards = new List<ResultCard> {
                new("Global settings", changed.Length == 0 ? "Every setting Hanki manages is at NVIDIA's default." :
                    $"{changed.Length} of {current.Count} settings differ from NVIDIA's defaults: " + string.Join(", ", changed.Select(c => $"{c.Setting.Name} {c.Text}")) + ".", CardStatus.Info),
                new("Presets", "Choose a preset and Review preset to see exactly what it would change. Edit settings… changes single settings; Save current as preset… keeps your setup to apply again later.", CardStatus.Info),
            };
            if (note is not null) cards.Add(new("Your presets", note, CardStatus.Unknown));
            return Diagnosis.From(report.ToString(), cards, changed.Length == 0 ? "NVIDIA settings are at their defaults" : $"{changed.Length} NVIDIA {(changed.Length == 1 ? "setting differs" : "settings differ")} from the defaults");
        }
        string Explain(NvidiaPreset p)
        {
            var changes = NvidiaPresets.Propose(p, current);
            return $"{p.Name}\n{p.Description}\n\n" + (changes.Count == 0 ? "Your global settings already match this preset." :
                $"It would change {changes.Count} {(changes.Count == 1 ? "setting" : "settings")}:\n" + string.Join("\n", changes.Select(c => $"• {c.Setting}: {c.Current} → {c.Recommended}{(c.Optional ? " (optional)" : "")}"))) +
                "\n\nChoose Review preset to pick which changes to apply.";
        }
        async Task Reload() { try { current = await Task.Run(IgezziGuard.Nvidia.ReadGlobal); } catch (NvidiaException) { current = []; } }
        async Task<string> Apply(IReadOnlyList<ProposedChange> changes, string title, string session)
        {
            string result = "";
            await ChangeReview.ReviewAndApply(shell.DialogOwner, changes, title, session, text => { result = Lines(text); view.Say(result); });
            await Reload(); return result;
        }

        var actions = new List<ReportAction> {
            new("Review preset", Primary: true, Text: async (_, _) => {
                if (current.Count == 0) return NotReady;
                if (Selected() is not { } p) return "Choose a preset first.";
                var changes = NvidiaPresets.Propose(p, current);
                if (changes.Count == 0) return $"Your global NVIDIA settings already match “{p.Name}”. Nothing to change.";
                return await Apply(changes, $"NVIDIA preset “{p.Name}” · all games", $"NVIDIA preset: {p.Name}");
            }),
            new("Edit settings…", Text: async (_, _) => {
                if (current.Count == 0) return NotReady;
                var rows = current.Select(c => {
                    var choices = NvidiaPresets.Choices(c.Setting, RefreshHz(), c).ToList();
                    int initial = c.IsDefault ? 0 : choices.FindIndex(x => x.Value == c.Effective);
                    if (initial < 0) { choices.Add(("Current: " + c.Text, c.Effective)); initial = choices.Count - 1; }
                    return (Setting: c, Choices: choices, Initial: initial);
                }).ToArray();
                var picked = Dialogs.Choices("NVIDIA global settings", "Choose a value for each setting you want to change. These apply to every game without its own value. You review the changes next; nothing is written yet.",
                    rows.Select(r => (r.Setting.Setting.Name, r.Setting.Text + (r.Setting.IsDefault ? " (NVIDIA default)" : ""), r.Choices.Select(x => x.Label).ToArray(), r.Initial)).ToArray());
                if (picked is null) return "Nothing was changed.";
                var changes = rows.Select((r, i) => picked[i] != r.Initial ? NvidiaPresets.Change(r.Setting, r.Choices[picked[i]].Value, "Chosen in the settings editor.") : null).OfType<ProposedChange>().ToArray();
                if (changes.Length == 0) return "No setting was changed in the editor.";
                return await Apply(changes, "NVIDIA global settings · all games", "NVIDIA settings edited");
            }),
            new("Save current as preset…", Text: (_, _) => {
                if (current.Count == 0) return Task.FromResult(NotReady);
                var name = Dialogs.Ask("Save as preset", "Name for a preset holding your current global NVIDIA settings:");
                if (name is null) return Task.FromResult("Nothing was saved.");
                if (NvidiaPresets.NameProblem(name, saved.Select(s => s.Name)) is { } problem) return Task.FromResult(problem);
                try {
                    var updated = saved.Append(NvidiaPresets.Capture(name, current)).ToArray();
                    NvidiaPresets.Write(NvidiaPresets.StorePath, updated); saved = updated; FillPresets(name.Trim());
                    return Task.FromResult($"Saved “{name.Trim()}”. It keeps the settings you or Hanki changed; the others are saved as NVIDIA's default. Apply it later with Review preset.");
                } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Task.FromResult("The preset couldn't be saved: " + ex.Message); }
            }),
            new("Delete my preset", Text: (_, _) => {
                if (Selected() is not { BuiltIn: false } p) return Task.FromResult("Choose one of your own presets (“My preset: …”) to delete it. Built-in presets can't be deleted.");
                if (!Blocks.Confirm("Delete preset", $"Delete your preset “{p.Name}”? Your NVIDIA settings aren't changed.", "Delete", true)) return Task.FromResult("Nothing was deleted.");
                try {
                    var updated = saved.Where(s => s.Name != p.Name).ToArray();
                    NvidiaPresets.Write(NvidiaPresets.StorePath, updated); saved = updated; FillPresets();
                    return Task.FromResult($"Deleted “{p.Name}”.");
                } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Task.FromResult("The preset couldn't be deleted: " + ex.Message); }
            }),
            new("Refresh", Diagnose: async (_, token) => {
                var diagnosis = await Task.Run(() => {
                    state.Graphics ??= GraphicsProbe.Collect();
                    try { current = IgezziGuard.Nvidia.ReadGlobal(); }
                    catch (NvidiaException ex) { current = []; return Diagnosis.From(ex.Message, [new("NVIDIA settings", NotReady + " " + ex.Message, CardStatus.Unknown)], "NVIDIA settings unavailable"); }
                    try { saved = NvidiaPresets.Read(NvidiaPresets.StorePath); } catch (IOException ex) { saved = []; return Summary(ex.Message); }
                    return Summary(null);
                }, token);
                FillPresets(); return diagnosis;
            }),
        };
        view = new ReportView(shell, "NVIDIA",
            "NVIDIA's global 3D settings: the ones NVIDIA Control Panel lists under Manage 3D settings → Global Settings. They apply to every game that has no value of its own. Choose a preset or edit single settings. You review each change first, Hanki saves the current value in Recovery so you can undo it, and only settings from NVIDIA's public SDK that your driver itself names as expected are offered.", actions);
        view.AddToBar(preset, first: true);
        preset.SelectionChanged += (_, _) => { if (Selected() is { } p && current.Count > 0) view.Say(Explain(p)); };
        view.Loaded += async (_, _) => { if (!loaded) { loaded = true; await view.RunAsync("Refresh", actions); } };
        return view;
    }

    // ───────────────────────── AMD Radeon ─────────────────────────

    internal static ReportView AmdPage(IShellServices shell, GamingState state)
    {
        const string Needs = "Radeon settings need an AMD Radeon graphics card with AMD Software: Adrenalin Edition installed.";
        const string NotReady = "Radeon settings aren't available on this PC. " + Needs;
        AmdGpuSettings? current = null;
        ReportView view = null!;
        bool loaded = false;
        uint RefreshHz() => (uint)Math.Round(state.Graphics?.Displays.Where(d => d.Primary).Select(d => d.Current.RefreshHz).DefaultIfEmpty(60).First() ?? 60);

        var actions = new List<ReportAction> {
            new("Edit settings…", Primary: true, Text: async (_, _) => {
                if (current is not { } gpu) return NotReady;
                var rows = gpu.Settings.Select(s => (Setting: s, Choices: AmdSettings.Choices(s, RefreshHz()))).ToArray();
                var picked = Dialogs.Choices("Radeon settings", "Choose a value for each setting you want to change. You review the changes next; nothing is written yet.",
                    rows.Select(r => (AmdSettings.Name(r.Setting.Kind), r.Setting.Text, r.Choices.Select(c => c.Label).ToArray(), 0)).ToArray());
                if (picked is null) return "Nothing was changed.";
                var changes = rows.Select((r, i) => picked[i] > 0 ? AmdSettings.ChangeTo(gpu, r.Setting.Kind, r.Choices[picked[i]].State, "Chosen in the settings editor.") : null).OfType<ProposedChange>().ToArray();
                if (changes.Length == 0) return "No setting was changed in the editor.";
                string result = "";
                await ChangeReview.ReviewAndApply(shell.DialogOwner, changes, $"Radeon settings · {gpu.GpuName}", "Radeon settings edited", text => { result = Lines(text); view.Say(result); });
                try { current = await Task.Run(IgezziGuard.Amd.ReadSettings); } catch (AmdException) { current = null; }
                return result;
            }),
            new("Refresh", Diagnose: (_, token) => Task.Run(() => {
                state.Graphics ??= GraphicsProbe.Collect();
                try { current = IgezziGuard.Amd.ReadSettings(); }
                catch (AmdException ex) { current = null; return Diagnosis.From(ex.Message, [new("Radeon settings", $"{ex.Message} {Needs}", CardStatus.Unknown)], "Radeon settings unavailable"); }
                var report = new StringBuilder($"Radeon settings • {current.GpuName} • {DateTimeOffset.Now:g}\r\nRead-only; nothing was changed.\r\n\r\n");
                foreach (var s in current.Settings) report.AppendLine($"{AmdSettings.Name(s.Kind)}: {s.Text}" + (s.RangeMin is { } lo && s.RangeMax is { } hi ? $" (driver range {lo}–{hi})" : ""));
                report.AppendLine("\r\nSettings your card or driver doesn't support aren't listed. Tune my PC applies the matching Radeon settings for each choice.");
                return Diagnosis.From(report.ToString(), [
                    new(current.GpuName, string.Join(" · ", current.Settings.Select(s => $"{AmdSettings.Name(s.Kind)}: {s.Text}")), CardStatus.Info),
                    new("Change them", "Edit settings… changes single settings. Tune my PC on the Performance page sets them for gaming, creative work or low power.", CardStatus.Info)
                ], $"Radeon settings for {current.GpuName}");
            }, token)),
        };
        view = new ReportView(shell, "AMD Radeon",
            "Radeon settings from AMD Software: Anti-Lag, Chill, Boost, Image Sharpening, Enhanced Sync, Wait for Vertical Refresh, Frame Rate Target Control and Anisotropic Filtering. You review each change first, and Hanki saves the current value in Recovery so you can undo it. Not yet tested on AMD hardware: please report anything that looks wrong.", actions);
        view.Loaded += async (_, _) => { if (!loaded) { loaded = true; await view.RunAsync("Refresh", actions); } };
        return view;
    }
}
