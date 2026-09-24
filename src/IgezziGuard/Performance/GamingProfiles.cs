namespace IgezziGuard;

public enum GamingGoal { Balanced, MaximumFps, Competitive, VisualQuality, Quiet, Laptop }
public enum ChangeSource { Windows, Display, Nvidia, Amd, Game }

/// <summary>
/// One proposed change. Kind/Target/After are the Recovery journal entry Hanki writes when it can apply the change
/// itself; otherwise Manual says where to change it. The current value is read again just before applying.
/// </summary>
public sealed record ProposedChange(string Id, ChangeSource Source, string Setting, string Current, string Recommended, string Why, bool Optional,
    string? Kind = null, string? Target = null, string? After = null, string? Manual = null, string? SettingsUri = null)
{
    public bool HankiApplies => Kind is not null;
}

/// <param name="Nvidia">The game's NVIDIA profile; null when NVIDIA has none, so the global settings apply.</param>
public sealed record GameContext(string Name, string ExecutablePath, NvidiaProfileView? Nvidia, NvidiaProfileView? NvidiaGlobal, GpuPreference? WindowsPreference);

/// <summary>
/// Gaming profiles (HANKI-GAME-204, HANKI-GPU-105): a vendor-neutral goal turned into concrete, explained changes.
/// Choosing a goal changes nothing; the person reviews the list, and only what they approve is applied. No goal
/// overclocks or touches voltages, and driver changes go into the game's own profile, never the global one.
/// </summary>
public static class GamingProfiles
{
    public static string Name(GamingGoal goal) => goal switch {
        GamingGoal.MaximumFps => "Maximum FPS", GamingGoal.Competitive => "Competitive", GamingGoal.VisualQuality => "Visual quality",
        GamingGoal.Quiet => "Quiet gaming", GamingGoal.Laptop => "Laptop gaming", _ => "Balanced"
    };
    public static string Describe(GamingGoal goal) => goal switch {
        GamingGoal.MaximumFps => "Highest frame rate: the fastest GPU and display mode, and the graphics card kept at full clocks in this game.",
        GamingGoal.Competitive => "Low input lag and steady frame times: the fastest display mode, fewer queued frames, and an optional frame cap for adaptive sync.",
        GamingGoal.VisualQuality => "Best image: fixes obvious problems and offers higher-quality texture filtering.",
        GamingGoal.Quiet => "Less heat and fan noise: no frames beyond what the display shows, and no forced maximum clocks.",
        GamingGoal.Laptop => "Uses the graphics card instead of integrated graphics for games, without forcing maximum power use.",
        _ => "Fixes obvious problems only, such as the wrong refresh rate or GPU, and leaves image quality alone."
    };

    public static IReadOnlyList<ProposedChange> Propose(GamingGoal goal, GraphicsInventory graphics, IReadOnlyList<DiagnosticResult> findings, GameContext? game)
    {
        var changes = new List<ProposedChange>();
        // Every goal fixes what the Gaming Health Scan found, when Hanki can apply it or point to the setting.
        foreach (var f in findings.Where(f => f.Severity is FindingSeverity.Warning or FindingSeverity.Critical)) {
            var m = f.Metadata;
            string current = m.GetValueOrDefault("current", "?"), recommended = m.GetValueOrDefault("recommended", "?");
            switch (m.GetValueOrDefault("remedy")) {
                case GamingHealth.RemedyDisplayMode:
                    changes.Add(new(f.FindingId, ChangeSource.Display, "Refresh rate", current, recommended, f.Explanation, false,
                        "Display mode", m["device"], GraphicsFacts.ModeText(int.Parse(m["width"]), int.Parse(m["height"]), int.Parse(m["refresh"]))));
                    break;
                case GamingHealth.RemedyGpuPreference:
                    changes.Add(new(f.FindingId, ChangeSource.Windows, $"GPU for {Path.GetFileName(m["application"])}", current, recommended, f.Explanation, false,
                        "GPU preference", m["application"], "GpuPreference=2;"));
                    break;
                case GamingHealth.RemedyWindowsSetting when m.GetValueOrDefault("target") is { } target && PerformanceSettings.WindowsGamingTargets.Contains(target):
                    changes.Add(new(f.FindingId, ChangeSource.Windows, f.Title, current, recommended, f.Explanation, m.GetValueOrDefault("optional") == "true",
                        PerformanceSettings.WindowsGamingKind, target, m["after"]));
                    break;
                case GamingHealth.RemedyProcessor when m.GetValueOrDefault("plan") is { Length: > 0 } plan:
                    changes.Add(new(f.FindingId, ChangeSource.Windows, "Maximum processor state (plugged in)", current, recommended, f.Explanation, false, "Processor power", plan + "|ac", "100"));
                    break;
                case GamingHealth.RemedySettings or GamingHealth.RemedyHardware:
                    changes.Add(new(f.FindingId, m.GetValueOrDefault("source") == "NVIDIA" ? ChangeSource.Nvidia : ChangeSource.Windows, f.Title, current, recommended, f.Explanation, false,
                        Manual: f.Recommendation, SettingsUri: m.GetValueOrDefault("settings")));
                    break;
            }
        }
        // Competitive: consistent aim in games that use the Windows pointer (an observation elsewhere, so only offered here).
        if (goal == GamingGoal.Competitive && findings.FirstOrDefault(f => f.FindingId == "mouse-acceleration" && f.Metadata.GetValueOrDefault("current") == "On") is { } mouse)
            changes.Add(new("mouse-acceleration", ChangeSource.Windows, "Mouse acceleration (Enhance pointer precision)", "On", "Off",
                "The same hand movement then moves the pointer the same distance at any speed, which many players prefer for aiming. Games that read the mouse directly aren't affected.", true,
                PerformanceSettings.WindowsGamingKind, "mouse-acceleration", "0,0,0"));
        if (game is null) return changes;

        // The game's GPU on PCs with integrated and dedicated graphics.
        if (graphics.Hybrid && game.WindowsPreference != GpuPreference.HighPerformance && goal != GamingGoal.Quiet)
            changes.Add(new("game-gpu", ChangeSource.Windows, "GPU for this game", game.WindowsPreference is { } p ? WindowsGamingParsing.PreferenceText(p) : "Let Windows decide",
                "High performance", $"On a PC with integrated and dedicated graphics, choosing High performance makes sure {game.Name} runs on the graphics card." +
                (goal == GamingGoal.Laptop ? " On battery this uses more power; plug in for long sessions." : ""), false, "GPU preference", game.ExecutablePath, "GpuPreference=2;"));

        // NVIDIA per-game settings, only where the driver's ids were verified (the view lists only trusted settings).
        if (game.NvidiaGlobal is null || !graphics.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia)) return changes;
        string exe = Path.GetFileName(game.ExecutablePath);
        NvidiaValue? Effective(uint id) => game.Nvidia?.Values.FirstOrDefault(v => v.Setting.Id == id && v.Source == NvidiaSettingSource.ThisProfile)
            ?? game.NvidiaGlobal.Values.FirstOrDefault(v => v.Setting.Id == id);
        void Nvidia(uint id, uint? value, string why, bool optional) {
            if (!game.NvidiaGlobal.Values.Any(v => v.Setting.Id == id)) return; // Setting not verified on this driver.
            var current = Effective(id); var setting = NvidiaSettings.Get(id);
            // "default" only removes a value the user set for this game; NVIDIA's own per-game values are left alone.
            if (current?.Value == value || value is null && (current?.Source != NvidiaSettingSource.ThisProfile || current.Predefined)) return;
            changes.Add(new($"nvidia-{id:X8}", ChangeSource.Nvidia, setting.Name + " (this game)", current?.Text ?? "Driver default",
                value is { } v ? setting.Describe(v) : "Global setting", why, optional, "NVIDIA setting", $"{exe}|{NvidiaSettings.Hex(id)}", value is { } w ? NvidiaSettings.Hex(w) : "default"));
        }
        double refresh = graphics.Displays.Where(d => d.Primary).Select(d => d.Current.RefreshHz).DefaultIfEmpty(graphics.Displays.Select(d => d.Current.RefreshHz).DefaultIfEmpty(60).Max()).First();
        uint displayCap = (uint)Math.Round(refresh);
        switch (goal) {
            case GamingGoal.MaximumFps:
                Nvidia(NvidiaSettings.PowerManagementId, NvidiaSettings.PowerPreferMaximum, "Keeps the graphics card at full clocks while this game runs, avoiding brief slow-downs while it ramps up. Uses more power in this game only.", false);
                Nvidia(NvidiaSettings.TextureFilteringId, 10, "“Performance” texture filtering trades a little texture sharpness for a small frame-rate gain.", true);
                break;
            case GamingGoal.Competitive:
                Nvidia(NvidiaSettings.PowerManagementId, NvidiaSettings.PowerPreferMaximum, "Keeps the graphics card at full clocks while this game runs, for steadier frame times.", false);
                Nvidia(NvidiaSettings.PreRenderedFramesId, 1, "Low Latency Mode (1 queued frame) lowers input lag when the graphics card is the limit. Games with NVIDIA Reflex use their own setting instead.", false);
                if (displayCap > 63)
                    Nvidia(NvidiaSettings.FrameRateLimitId, displayCap - 3, $"With G-SYNC or FreeSync, capping a few frames below the {displayCap} Hz refresh rate keeps the game inside the adaptive-sync range and avoids latency spikes. Hanki can't tell whether adaptive sync is on, so this is optional.", true);
                if (Effective(NvidiaSettings.VerticalSyncId)?.Value == NvidiaSettings.VsyncOn)
                    Nvidia(NvidiaSettings.VerticalSyncId, NvidiaSettings.VsyncApplication, "Vertical sync forced on adds input lag; letting the game decide is usual for competitive play.", false);
                break;
            case GamingGoal.VisualQuality:
                Nvidia(NvidiaSettings.TextureFilteringId, 0xFFFFFFF6, "“High quality” texture filtering gives sharper textures at angles, at a small performance cost.", true);
                break;
            case GamingGoal.Quiet:
                if (displayCap > 30) Nvidia(NvidiaSettings.FrameRateLimitId, displayCap, $"Frames above the display's {displayCap} Hz aren't shown, so capping there saves power, heat and fan noise.", false);
                Nvidia(NvidiaSettings.PowerManagementId, null, "Removes a forced maximum-performance mode for this game so the card can clock down when it has headroom.", false);
                break;
        }
        return changes;
    }

    /// <summary>
    /// Frame-rate limiter and sync conflicts for one game (HANKI-GAME-208). Only configuration Hanki read directly is
    /// used; in-game limiters and external tools aren't visible, which the text says.
    /// </summary>
    public static IReadOnlyList<string> Conflicts(GameContext game, double refreshHz)
    {
        var notes = new List<string>();
        if (game.NvidiaGlobal is null) return notes;
        uint? Value(uint id, NvidiaProfileView? view) => view?.Values.FirstOrDefault(v => v.Setting.Id == id && (view == game.NvidiaGlobal || v.Source == NvidiaSettingSource.ThisProfile))?.Value;
        uint? gameCap = Value(NvidiaSettings.FrameRateLimitId, game.Nvidia), globalCap = Value(NvidiaSettings.FrameRateLimitId, game.NvidiaGlobal);
        uint? vsync = Value(NvidiaSettings.VerticalSyncId, game.Nvidia) ?? Value(NvidiaSettings.VerticalSyncId, game.NvidiaGlobal);
        uint? cap = gameCap is > 0 ? gameCap : globalCap is > 0 ? globalCap : null;
        if (gameCap is > 0 && globalCap is > 0 && gameCap != globalCap)
            notes.Add($"Two NVIDIA frame limits apply: {gameCap} FPS for this game and {globalCap} FPS globally. The game's own limit wins; use one deliberate limit.");
        if (cap is { } c && refreshHz > 0 && c > refreshHz + 1 && vsync == NvidiaSettings.VsyncOn)
            notes.Add($"The {c} FPS cap is above the {Math.Round(refreshHz):0} Hz refresh rate while vertical sync is forced on, so vertical sync limits the game first and the cap does nothing.");
        if (cap is { } low && refreshHz > 0 && low < refreshHz * 0.5)
            notes.Add($"The {low} FPS cap is less than half of the {Math.Round(refreshHz):0} Hz refresh rate. That's fine for quiet play, but it limits smoothness.");
        if (notes.Count > 0) notes.Add("Hanki reads NVIDIA's limits only; a limit set inside the game or by another tool isn't visible here.");
        return notes;
    }
}
