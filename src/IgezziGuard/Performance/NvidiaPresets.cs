namespace IgezziGuard;

/// <param name="Value">The value to set, or null for NVIDIA's default.</param>
public sealed record NvidiaPresetValue(uint Id, uint? Value, string Why, bool Optional = false);
public sealed record NvidiaPreset(string Name, string Description, IReadOnlyList<NvidiaPresetValue> Values, bool BuiltIn = true);

/// <param name="Values">Setting id ("0x1057EB71") → "0x…" (a value) or "default" (NVIDIA's default).</param>
public sealed record NvidiaUserPreset(string Name, IReadOnlyDictionary<string, string> Values);
public sealed record NvidiaPresetDocument(int Version, IReadOnlyList<NvidiaUserPreset> Presets);

/// <summary>
/// NVIDIA global settings presets and editing (HANKI-GPU-113): ready-made sets, your own saved sets and single
/// settings, all turned into reviewed changes to the global profile (every game without its own value). Hanki only
/// offers values from NVIDIA's public SDK, saves each current value in Recovery first, and never touches clocks,
/// voltages or undocumented settings. A preset changes nothing until you approve its changes.
/// </summary>
public static class NvidiaPresets
{
    public const string ChangeKind = "NVIDIA global setting";
    public const int UserLimit = 50, NameLimit = 60;

    public static IReadOnlyList<NvidiaPreset> BuiltIn(double refreshHz)
    {
        uint display = (uint)Math.Round(refreshHz);
        var competitive = new List<NvidiaPresetValue> {
            new(NvidiaSettings.PreRenderedFramesId, 1, "Low Latency Mode: the driver queues one frame instead of letting the game queue several, which lowers input lag when the graphics card is the limit. Games with NVIDIA Reflex use Reflex instead."),
            new(NvidiaSettings.VerticalSyncId, NvidiaSettings.VsyncApplication, "Vertical sync forced on adds input lag; letting each game decide is usual for competitive play."),
            new(NvidiaSettings.PreferredRefreshRateId, 1, "Games that don't choose a refresh rate get your display's highest, so they aren't stuck at 60 Hz."),
            new(NvidiaSettings.PowerManagementId, NvidiaSettings.PowerPreferMaximum, "Keeps the graphics card at full clocks in games, for steadier frame times. Set for all games it also keeps the card clocked up in other 3D apps, which can add 15–25 W; Gaming → Games sets it for one game instead.", Optional: true),
            new(NvidiaSettings.TextureFilteringId, NvidiaSettings.TexturePerformance, "“Performance” texture filtering trades a little texture sharpness for a small frame-rate gain.", Optional: true),
        };
        if (display > 63) competitive.Add(new(NvidiaSettings.FrameRateLimitId, display - 3,
            $"With G-SYNC or FreeSync, capping a few frames below the {display} Hz refresh rate keeps games inside the adaptive-sync range and avoids latency spikes. Hanki can't tell whether adaptive sync is on, so this is optional.", Optional: true));
        var quiet = new List<NvidiaPresetValue> {
            new(NvidiaSettings.PowerManagementId, null, "NVIDIA's default power mode lets the card clock down when a game doesn't need full speed."),
            new(NvidiaSettings.VerticalSyncId, NvidiaSettings.VsyncApplication, "Leaves vertical sync to each game."),
        };
        if (display > 30) quiet.Add(new(NvidiaSettings.FrameRateLimitId, display,
            $"Frames above the display's {display} Hz aren't shown, so capping there saves power, heat and fan noise in every game."));
        return [
            new("Competitive (low latency)", "Lower input lag and steadier frame times: Low Latency Mode, vertical sync left to the game, the highest refresh rate, and optionally full GPU clocks and a frame cap for G-SYNC or FreeSync.", competitive),
            new("Maximum FPS", "The most frames: faster texture filtering, no driver frame cap and no vertical sync, and optionally full GPU clocks. Image quality drops a little.", [
                new(NvidiaSettings.PowerManagementId, NvidiaSettings.PowerPreferMaximum, "Keeps the graphics card at full clocks in games, avoiding brief slow-downs while it ramps up. Set for all games it also keeps the card clocked up in other 3D apps, which can add 15–25 W; Gaming → Games sets it for one game instead.", Optional: true),
                new(NvidiaSettings.TextureFilteringId, NvidiaSettings.TextureHighPerformance, "“High performance” texture filtering gives the most frames, with slightly blurrier textures at angles."),
                new(NvidiaSettings.AnisotropicSampleOptimizationId, 1, "Uses fewer texture samples for anisotropic filtering: a small speed-up with a small quality cost."),
                new(NvidiaSettings.VerticalSyncId, NvidiaSettings.VsyncOff, "No vertical sync, so frames aren't held back to the refresh rate. You may see tearing."),
                new(NvidiaSettings.FrameRateLimitId, 0, "No driver frame cap."),
                new(NvidiaSettings.PreferredRefreshRateId, 1, "Games that don't choose a refresh rate get your display's highest."),
            ]),
            new("Visual quality", "The sharpest image: high-quality texture filtering and 16x anisotropic filtering in every game. Costs a little performance.", [
                new(NvidiaSettings.TextureFilteringId, NvidiaSettings.TextureHighQuality, "“High quality” texture filtering turns off the driver's texture shortcuts."),
                new(NvidiaSettings.AnisotropicModeId, 1, "Lets the driver set anisotropic filtering instead of each game."),
                new(NvidiaSettings.AnisotropicLevelId, 16, "16x anisotropic filtering keeps textures sharp at steep angles, such as floors and roads; modern cards handle it cheaply."),
                new(NvidiaSettings.AnisotropicSampleOptimizationId, 0, "Full texture sampling for anisotropic filtering."),
            ]),
            new("Quiet and cool", "Less heat and fan noise: no frames beyond what the display shows, and the card allowed to clock down.", quiet),
            new("NVIDIA defaults", "Puts every setting Hanki manages back to NVIDIA's defaults, as “Restore” in NVIDIA Control Panel does for these settings. Settings Hanki doesn't manage are left alone.",
                NvidiaSettings.Catalog.Select(s => new NvidiaPresetValue(s.Id, null, "Back to NVIDIA's default.")).ToArray()),
        ];
    }

    /// <summary>The reviewed changes a preset makes to the current global settings; values already in place are skipped.</summary>
    public static IReadOnlyList<ProposedChange> Propose(NvidiaPreset preset, IReadOnlyList<NvidiaGlobalSetting> current)
    {
        var changes = new List<ProposedChange>();
        foreach (var value in preset.Values)
            if (current.FirstOrDefault(c => c.Setting.Id == value.Id) is { } setting && Change(setting, value.Value, value.Why, value.Optional) is { } change)
                changes.Add(change);
        return changes;
    }

    /// <summary>
    /// One setting change, or null when nothing would change or Hanki doesn't offer the value. Only settings the
    /// driver itself named as expected appear in the current list, so an unverified id is never written.
    /// </summary>
    public static ProposedChange? Change(NvidiaGlobalSetting current, uint? value, string why, bool optional = false)
    {
        var setting = current.Setting;
        if (value is { } v) {
            if (!NvidiaSettings.Allowed(setting, v) || current.Effective == v) return null;
        } else if (current.IsDefault) return null;
        string after = value is { } w ? NvidiaSettings.Hex(w) : current.Reset;
        return new($"nvidia-global-{setting.Id:X8}", ChangeSource.Nvidia, setting.Name, current.Text + (current.IsDefault ? " (NVIDIA default)" : ""),
            value is { } x ? setting.Describe(x) : "NVIDIA default" + (NvidiaSettings.StateValue(current.Reset) is { } r ? $" ({setting.Describe(r)})" : ""),
            why, optional, ChangeKind, NvidiaSettings.Hex(setting.Id), after);
    }

    /// <summary>Choices for the settings editor: null is NVIDIA's default. The frame limit offers caps that suit the display.</summary>
    public static IReadOnlyList<(string Label, uint? Value)> Choices(NvidiaSetting setting, double refreshHz, NvidiaGlobalSetting? current = null)
    {
        var choices = new List<(string, uint?)> { ("NVIDIA default", null) };
        IEnumerable<uint> values = setting.Kind == NvidiaSettingKind.FrameRateLimit
            ? new uint[] { 0, 30, 60, 90, 120, 144, 165, 240, 360 }
                .Concat(refreshHz > 63 ? new[] { (uint)Math.Round(refreshHz) - 3, (uint)Math.Round(refreshHz) } : Array.Empty<uint>())
                .Where(v => NvidiaSettings.Allowed(setting, v)).Distinct().Order()
            : setting.Values.Keys;
        choices.AddRange(values.Select(v => (setting.Describe(v), (uint?)v)));
        // A value you set elsewhere that Hanki doesn't list stays selectable, so opening the editor never changes it.
        if (current is { Effective: { } e } && NvidiaSettings.StateValue(current.State) == e && !choices.Any(c => c.Item2 == e) && NvidiaSettings.Allowed(setting, e))
            choices.Add((setting.Describe(e), e));
        return choices;
    }

    // ---- Your own presets -------------------------------------------------------------------------------------
    /// <summary>Saves the current values: ones you or Hanki set are kept, the rest become "NVIDIA default".</summary>
    public static NvidiaUserPreset Capture(string name, IReadOnlyList<NvidiaGlobalSetting> current) =>
        new(name.Trim(), current.ToDictionary(c => NvidiaSettings.Hex(c.Setting.Id),
            c => c.State.StartsWith("0x", StringComparison.Ordinal) && NvidiaSettings.StateValue(c.State) is { } v && NvidiaSettings.Allowed(c.Setting, v) ? c.State : "default"));

    public static NvidiaPreset FromUser(NvidiaUserPreset user)
    {
        var values = new List<NvidiaPresetValue>();
        foreach (var (key, state) in user.Values) {
            if (NvidiaSettings.StateValue(key) is not { } id || NvidiaSettings.Catalog.FirstOrDefault(s => s.Id == id) is not { } setting) continue;
            uint? value = state == "default" ? null : NvidiaSettings.StateValue(state);
            if (state != "default" && (value is null || !NvidiaSettings.Allowed(setting, value.Value))) continue;
            values.Add(new(id, value, value is null ? "Your preset uses NVIDIA's default here." : "Your preset's value."));
        }
        return new(user.Name, $"Your preset “{user.Name}”.", values, BuiltIn: false);
    }

    /// <summary>Why a name can't be used, or null when it can.</summary>
    public static string? NameProblem(string name, IEnumerable<string> taken)
    {
        name = name.Trim();
        if (name.Length == 0) return "Give the preset a name.";
        if (name.Length > NameLimit) return $"Use at most {NameLimit} characters.";
        if (name.Any(char.IsControl)) return "The name can't contain control characters.";
        if (BuiltIn(60).Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return "That's the name of a built-in preset.";
        return taken.Any(t => t.Equals(name, StringComparison.OrdinalIgnoreCase)) ? "A preset with that name exists; delete it first or choose another name." : null;
    }

    internal static string StorePath => Path.Combine(SecurityPaths.Root, "nvidia-presets.json");
    public static IReadOnlyList<NvidiaUserPreset> Read(string path)
    {
        var document = LocalJson.Read<NvidiaPresetDocument>(path);
        if (document is null) return [];
        if (document.Version != 1 || document.Presets is null || document.Presets.Count > UserLimit ||
            document.Presets.Any(p => p?.Name is null || p.Values is null || p.Values.Count > NvidiaSettings.Catalog.Count ||
                p.Values.Any(v => NvidiaSettings.StateValue(v.Key) is null || !NvidiaSettings.ValidState(v.Value))))
            throw new IOException("Your NVIDIA presets file is damaged or from a newer version. Original file preserved.");
        return document.Presets;
    }
    public static void Write(string path, IReadOnlyList<NvidiaUserPreset> presets)
    {
        if (presets.Count > UserLimit) throw new IOException($"You can keep up to {UserLimit} presets; delete one first.");
        using var gate = LocalJson.Lock(path);
        LocalJson.Write(path, new NvidiaPresetDocument(1, presets));
    }
}
