using System.Globalization;
namespace IgezziGuard;

public enum AmdSettingKind { AntiLag, Chill, Boost, ImageSharpening, EnhancedSync, WaitForVerticalRefresh, FrameRateTargetControl, AnisotropicFiltering }

/// <summary>
/// One Radeon 3D setting as AMD's ADLX reports it for a GPU (HANKI-GPU-103, HANKI-GAME-203). Value is the setting's
/// number (frame rate, sharpness, resolution %, vertical refresh mode or anisotropic level); Chill has a minimum and
/// maximum frame rate (Value, Value2). Range is what the driver accepts.
/// </summary>
public sealed record AmdSetting(AmdSettingKind Kind, bool Enabled, int? Value, int? Value2 = null, int? RangeMin = null, int? RangeMax = null)
{
    public string State => AmdSettings.State(Kind, Enabled, Value, Value2);
    public string Text => AmdSettings.Describe(Kind, State);
}

/// <param name="Gpu">ADLX's unique id for the GPU, used in Recovery targets.</param>
public sealed record AmdGpuSettings(int Gpu, string GpuName, IReadOnlyList<AmdSetting> Settings);

/// <summary>
/// Radeon settings as Recovery stores them. The state keeps the value even when the feature is off ("off:144"), so an
/// undo restores both. Kind "AMD setting", target "{gpu}|{kind}", for example "12345|AntiLag".
/// Not yet tested on AMD hardware; every write is checked by reading the setting back.
/// </summary>
public static class AmdSettings
{
    public const string ChangeKind = "AMD setting";
    public const int WfvrAlwaysOff = 0, WfvrOffUnlessApp = 1, WfvrOnUnlessApp = 2, WfvrAlwaysOn = 3;

    public static string Name(AmdSettingKind kind) => kind switch {
        AmdSettingKind.AntiLag => "Radeon Anti-Lag", AmdSettingKind.Chill => "Radeon Chill", AmdSettingKind.Boost => "Radeon Boost",
        AmdSettingKind.ImageSharpening => "Radeon Image Sharpening", AmdSettingKind.EnhancedSync => "Radeon Enhanced Sync",
        AmdSettingKind.WaitForVerticalRefresh => "Wait for Vertical Refresh", AmdSettingKind.FrameRateTargetControl => "Frame Rate Target Control",
        _ => "Anisotropic Filtering"
    };
    /// <summary>Settings that hold a value besides on/off.</summary>
    public static bool HasValue(AmdSettingKind kind) => kind is not (AmdSettingKind.AntiLag or AmdSettingKind.EnhancedSync);

    public static string State(AmdSettingKind kind, bool enabled, int? value, int? value2 = null)
    {
        if (kind == AmdSettingKind.WaitForVerticalRefresh) return "mode:" + (value ?? WfvrOffUnlessApp).ToString(CultureInfo.InvariantCulture);
        string on = enabled ? "on" : "off";
        if (!HasValue(kind) || value is null) return on;
        return kind == AmdSettingKind.Chill && value2 is { } max ? $"{on}:{value}-{max}" : $"{on}:{value}";
    }

    /// <summary>(enabled, value, value2) from a state, or null when the text isn't a valid state for the kind.</summary>
    public static (bool Enabled, int? Value, int? Value2)? Parse(AmdSettingKind kind, string state)
    {
        static int? Int(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v <= 100_000 ? v : null;
        if (kind == AmdSettingKind.WaitForVerticalRefresh)
            return state.StartsWith("mode:", StringComparison.Ordinal) && Int(state[5..]) is { } mode && mode <= WfvrAlwaysOn ? (true, mode, null) : null;
        var parts = state.Split(':', 2);
        if (parts[0] is not ("on" or "off")) return null;
        bool enabled = parts[0] == "on";
        if (parts.Length == 1) return (enabled, null, null);
        if (!HasValue(kind)) return null;
        if (kind == AmdSettingKind.Chill) {
            var range = parts[1].Split('-');
            return range.Length == 2 && Int(range[0]) is { } min && Int(range[1]) is { } max && min <= max ? (enabled, min, max) : null;
        }
        return Int(parts[1]) is { } value ? (enabled, value, null) : null;
    }

    public static string Describe(AmdSettingKind kind, string state)
    {
        if (Parse(kind, state) is not { } s) return state;
        if (kind == AmdSettingKind.WaitForVerticalRefresh) return s.Value switch {
            WfvrAlwaysOff => "Always off", WfvrOffUnlessApp => "Off, unless the game says otherwise", WfvrOnUnlessApp => "On, unless the game says otherwise", _ => "Always on"
        };
        if (!s.Enabled) return "Off";
        return kind switch {
            AmdSettingKind.Chill when s.Value is { } min && s.Value2 is { } max => $"On, {min}–{max} FPS",
            AmdSettingKind.FrameRateTargetControl when s.Value is { } fps => $"On, {fps} FPS",
            AmdSettingKind.ImageSharpening when s.Value is { } sharp => $"On, {sharp}% sharpness",
            AmdSettingKind.Boost when s.Value is { } res => $"On, down to {res}% resolution",
            AmdSettingKind.AnisotropicFiltering when s.Value is { } level => $"On, {level}x",
            _ => "On"
        };
    }

    /// <summary>A Recovery target "{gpu}|{kind}" back into its parts.</summary>
    public static (int Gpu, AmdSettingKind Kind) ParseTarget(string target)
    {
        var parts = target.Split('|');
        return parts.Length == 2 && int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var gpu) && Enum.TryParse<AmdSettingKind>(parts[1], false, out var kind)
            && kind.ToString() == parts[1] ? (gpu, kind) : throw new IOException("Invalid AMD setting target.");
    }
    public static string Target(int gpu, AmdSettingKind kind) => $"{gpu.ToString(CultureInfo.InvariantCulture)}|{kind}";

    /// <summary>
    /// A reviewed change to one Radeon setting, or null when it's already set, unsupported, or the value is outside
    /// the range the driver reported. The value stays when a feature is turned off.
    /// </summary>
    public static ProposedChange? Change(AmdGpuSettings gpu, AmdSettingKind kind, bool enabled, int? value, string why, bool optional = false, int? value2 = null)
    {
        if (gpu.Settings.FirstOrDefault(s => s.Kind == kind) is not { } current) return null;
        if (value is { } v && (current.RangeMin is { } lo && v < lo || current.RangeMax is { } hi && v > hi)) return null;
        if (value2 is { } w && (current.RangeMin is { } lo2 && w < lo2 || current.RangeMax is { } hi2 && w > hi2)) return null;
        string after = State(kind, enabled, value ?? current.Value, value2 ?? current.Value2);
        if (after == current.State) return null;
        return new($"amd-{kind}", ChangeSource.Amd, Name(kind), current.Text, Describe(kind, after), why, optional, ChangeKind, Target(gpu.Gpu, kind), after);
    }

    /// <summary>A change to an exact state chosen in the editor, or null when nothing changes or the state isn't valid.</summary>
    public static ProposedChange? ChangeTo(AmdGpuSettings gpu, AmdSettingKind kind, string state, string why) =>
        Parse(kind, state) is { } s ? Change(gpu, kind, s.Enabled, s.Value, why, false, s.Value2) : null;

    /// <summary>Editor choices for a setting (label, state), starting with the current one; values stay inside the driver's range.</summary>
    public static IReadOnlyList<(string Label, string State)> Choices(AmdSetting current, uint refreshHz)
    {
        var states = new List<string> { current.State };
        void Add(bool enabled, int? value = null, int? value2 = null) {
            if (value is { } v && (current.RangeMin is { } lo && v < lo || current.RangeMax is { } hi && v > hi)) return;
            states.Add(State(current.Kind, enabled, value ?? current.Value, value2 ?? current.Value2));
        }
        int refresh = (int)Math.Max(30, refreshHz);
        switch (current.Kind) {
            case AmdSettingKind.AntiLag or AmdSettingKind.EnhancedSync or AmdSettingKind.Boost: Add(true); Add(false); break;
            case AmdSettingKind.Chill: Add(false); Add(true, 30, Math.Min(60, refresh)); Add(true, 40, Math.Min(72, refresh)); Add(true, 60, refresh); break;
            case AmdSettingKind.ImageSharpening: Add(false); Add(true, 50); Add(true, 80); break;
            case AmdSettingKind.FrameRateTargetControl: Add(false); Add(true, 60); if (refresh > 63) Add(true, refresh - 3); Add(true, refresh); break;
            case AmdSettingKind.WaitForVerticalRefresh: foreach (var mode in new[] { WfvrAlwaysOff, WfvrOffUnlessApp, WfvrOnUnlessApp, WfvrAlwaysOn }) Add(true, mode); break;
            case AmdSettingKind.AnisotropicFiltering: Add(false); foreach (var level in new[] { 4, 8, 16 }) Add(true, level); break;
        }
        return states.Distinct().Select(s => (s == current.State ? "Current: " + Describe(current.Kind, s) : Describe(current.Kind, s), s)).ToArray();
    }

    /// <summary>The Radeon changes for a Tune my PC choice (docs/TUNING.md). frameCap is 3 below the refresh rate, or null without adaptive sync.</summary>
    public static IReadOnlyList<ProposedChange> ForScenario(TuneScenario scenario, AdaptiveSync sync, AmdGpuSettings gpu, uint refreshHz)
    {
        var changes = new List<ProposedChange>();
        void Add(ProposedChange? change) { if (change is not null) changes.Add(change); }
        int? cap = sync == AdaptiveSync.Yes && refreshHz > 63 ? (int)refreshHz - 3 : null;
        switch (scenario) {
            case TuneScenario.GamingPerformance:
                Add(Change(gpu, AmdSettingKind.AntiLag, true, null, "Anti-Lag lowers input lag when the graphics card is the limit. Games with AMD Anti-Lag 2 or NVIDIA Reflex-style options use their own setting."));
                Add(Change(gpu, AmdSettingKind.Chill, false, null, "Chill changes the frame rate with your input, so it drops exactly when a fight starts; off for competitive play."));
                Add(Change(gpu, AmdSettingKind.Boost, false, null, "Boost lowers resolution during fast movement; off keeps the image steady."));
                Add(Change(gpu, AmdSettingKind.EnhancedSync, false, null, "Enhanced Sync isn't a replacement for FreeSync with a frame cap and can make frame pacing uneven."));
                if (cap is { } c) Add(Change(gpu, AmdSettingKind.FrameRateTargetControl, true, c, $"With FreeSync, capping 3 FPS below the {refreshHz} Hz refresh rate keeps games inside the FreeSync range, where it adds no lag."));
                break;
            case TuneScenario.GamingQuality:
                Add(Change(gpu, AmdSettingKind.Chill, false, null, "Chill lowers the frame rate when you're not moving; off keeps single-player games smooth."));
                Add(Change(gpu, AmdSettingKind.Boost, false, null, "Boost lowers resolution during fast movement; off keeps the image sharp."));
                if (cap is { } q) Add(Change(gpu, AmdSettingKind.FrameRateTargetControl, true, q, $"With FreeSync, capping 3 FPS below the {refreshHz} Hz refresh rate keeps games inside the FreeSync range."));
                else Add(Change(gpu, AmdSettingKind.WaitForVerticalRefresh, true, WfvrOnUnlessApp, "Without adaptive sync, vertical sync removes tearing; in single-player games a clean image matters more than the small extra lag."));
                Add(Change(gpu, AmdSettingKind.AnisotropicFiltering, true, 16, "16x anisotropic filtering keeps textures sharp at steep angles; modern cards handle it cheaply.", optional: true));
                break;
            case TuneScenario.LowPower:
                Add(Change(gpu, AmdSettingKind.Chill, true, Math.Min(30, (int)refreshHz), "Chill lowers the frame rate when you're not moving and keeps it at 60 FPS or below, saving power, heat and fan noise.", value2: Math.Min(60, (int)refreshHz)));
                break;
        }
        return changes;
    }
}
