using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace IgezziGuard;

/// <summary>
/// The Performance changes Hanki can apply, as Recovery journal kinds (see Navigation.PerformanceChangeKinds). Each
/// has a text form of its state so the journal can record the value before the change and restore it exactly:
///   Display mode:    target \\.\DISPLAY1,           value "1920x1080@165"
///   GPU preference:  target the executable's path,   value "GpuPreference=2;" or "" (Windows decides)
///   Processor power: target "{plan guid}|ac",        value "100"
///   NVIDIA setting:  target "game.exe|0x1057EB71",   value "0x00000001", "predefined:0x…" (NVIDIA's own value) or "default"
///   NVIDIA global setting: target "0x1057EB71",      value as for NVIDIA setting, in the global profile (all games)
///   Windows gaming setting: target "game-mode", "background-recording", "windowed-optimizations" or "variable-refresh", value
///                    "on", "off" or "default" (never changed); target "mouse-acceleration", value "6,10,1"
///   AMD setting:     target "{gpu}|AntiLag",          value "on", "off:144", "on:30-60" or "mode:1" (see AmdSettings)
/// </summary>
internal static class PerformanceSettings
{
    internal const string WindowsGamingKind = "Windows gaming setting";
    internal static bool Handles(string kind) => kind is "Display mode" or "GPU preference" or "Processor power" or "NVIDIA setting" or NvidiaPresets.ChangeKind or WindowsGamingKind or AmdSettings.ChangeKind;

    internal static string Read(string kind, string target) => kind switch {
        "Display mode" => DisplayMode(target),
        "GPU preference" => GpuPreference(target),
        "Processor power" => Processor(target).ToString(),
        "NVIDIA setting" => NvidiaSetting(target),
        NvidiaPresets.ChangeKind => Nvidia.ReadProfileSetting(null, ParseNvidiaGlobalTarget(target)),
        WindowsGamingKind => WindowsGaming(target),
        AmdSettings.ChangeKind => Amd.ReadState(target),
        _ => throw new IOException("Unsupported setting.")
    };
    internal static void Write(string kind, string target, string value)
    {
        switch (kind) {
            case "Display mode": SetDisplayMode(target, value); break;
            case "GPU preference": SetGpuPreference(target, value); break;
            case "Processor power": SetProcessor(target, value); break;
            case "NVIDIA setting": SetNvidiaSetting(target, value); break;
            case WindowsGamingKind: SetWindowsGaming(target, value); break;
            case AmdSettings.ChangeKind:
                if (AmdSettings.Parse(AmdSettings.ParseTarget(target).Kind, value) is null) throw new IOException("Invalid AMD setting value.");
                Amd.WriteState(target, value);
                break;
            case NvidiaPresets.ChangeKind:
                if (!NvidiaSettings.ValidState(value)) throw new IOException("Invalid NVIDIA setting value.");
                Nvidia.WriteProfileSetting(null, ParseNvidiaGlobalTarget(target), value);
                break;
            default: throw new IOException("Unsupported setting.");
        }
    }

    // ---- Display mode: ChangeDisplaySettingsEx, tested before it is applied --------------------------------
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsExW(string device, ref GraphicsProbe.DevMode mode, IntPtr window, uint flags, IntPtr parameter);
    private const uint UpdateRegistry = 1, Test = 2;
    internal static string ModeText(int width, int height, int refresh) => GraphicsFacts.ModeText(width, height, refresh);
    internal static (int Width, int Height, int Refresh) ParseMode(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"^(\d{3,5})x(\d{3,5})@(\d{2,3})$");
        return match.Success ? (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value)) : throw new IOException("Invalid display mode.");
    }
    private static void ValidateDevice(string device) { if (!System.Text.RegularExpressions.Regex.IsMatch(device, @"^\\\\\.\\DISPLAY\d{1,2}$")) throw new IOException("Invalid display name."); }
    private static string DisplayMode(string device)
    {
        ValidateDevice(device);
        var current = GraphicsProbe.NewDevMode();
        if (!GraphicsProbe.EnumDisplaySettingsW(device, -1, ref current)) throw new IOException("The display is no longer connected.");
        return ModeText((int)current.PelsWidth, (int)current.PelsHeight, (int)current.DisplayFrequency);
    }
    private static void SetDisplayMode(string device, string value)
    {
        ValidateDevice(device);
        var (width, height, refresh) = ParseMode(value);
        var mode = GraphicsProbe.NewDevMode();
        for (int i = 0; i < 2000 && GraphicsProbe.EnumDisplaySettingsW(device, i, ref mode); i++) {
            if (mode.PelsWidth != width || mode.PelsHeight != height || mode.DisplayFrequency != refresh) continue;
            mode.Fields = 0x80000 | 0x100000 | 0x400000; // DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY
            int test = ChangeDisplaySettingsExW(device, ref mode, IntPtr.Zero, Test, IntPtr.Zero);
            if (test != 0) throw new IOException($"Windows won't use this display mode (code {test}).");
            int applied = ChangeDisplaySettingsExW(device, ref mode, IntPtr.Zero, UpdateRegistry, IntPtr.Zero);
            if (applied != 0) throw new IOException($"Windows couldn't switch the display mode (code {applied}).");
            return;
        }
        throw new IOException("The display doesn't offer that mode.");
    }

    // ---- Per-app GPU preference: the value Settings → Display → Graphics stores -----------------------------
    private static void ValidateApplication(string path) { if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.Length > 400) throw new IOException("Invalid application path."); }
    private static string GpuPreference(string path)
    {
        ValidateApplication(path);
        using var key = Registry.CurrentUser.OpenSubKey(WindowsGamingProbe.GpuPreferencesKey);
        return key?.GetValue(path) as string ?? "";
    }
    private static void SetGpuPreference(string path, string value)
    {
        ValidateApplication(path);
        if (value.Length > 0 && WindowsGamingParsing.Preference(value) is null) throw new IOException("Invalid GPU preference.");
        using var key = Registry.CurrentUser.CreateSubKey(WindowsGamingProbe.GpuPreferencesKey, writable: true);
        if (value.Length == 0) key.DeleteValue(path, throwOnMissingValue: false); else key.SetValue(path, value, RegistryValueKind.String);
    }

    // ---- Processor maximum for mains power, in the active plan -----------------------------------------------
    private static readonly Guid ProcessorGroup = new("54533251-82be-4824-96c1-47b60b740d00"), ProcessorMaximum = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    private static Guid Plan(string target) => target.EndsWith("|ac", StringComparison.Ordinal) && Guid.TryParse(target[..^3], out var plan) ? plan : throw new IOException("Invalid processor power target.");
    private static uint Processor(string target)
    {
        var plan = Plan(target); var group = ProcessorGroup; var setting = ProcessorMaximum;
        uint status = PowerReadACValueIndex(IntPtr.Zero, ref plan, ref group, ref setting, out var value);
        return status == 0 ? value : throw new IOException($"The power plan couldn't be read (error {status}).");
    }
    private static void SetProcessor(string target, string value)
    {
        if (!uint.TryParse(value, out var percent) || percent is < 5 or > 100) throw new IOException("Invalid processor state.");
        var plan = Plan(target); var group = ProcessorGroup; var setting = ProcessorMaximum;
        uint status = PowerWriteACValueIndex(IntPtr.Zero, ref plan, ref group, ref setting, percent);
        if (status != 0) throw new IOException($"Windows didn't accept the change (error {status}).");
        PowerSetActiveScheme(IntPtr.Zero, ref plan); // Re-applies the plan so the new value takes effect now.
    }

    // ---- Windows gaming settings: Game Bar background recording, DirectX options and mouse acceleration -------------
    internal static readonly IReadOnlySet<string> WindowsGamingTargets = new HashSet<string>(StringComparer.Ordinal) { "game-mode", "background-recording", "windowed-optimizations", "variable-refresh", "mouse-acceleration" };
    private static string DirectXFlag(string target) => target == "windowed-optimizations" ? "SwapEffectUpgradeEnable" : "VRROptimizeEnable";
    private static string WindowsGaming(string target)
    {
        if (!WindowsGamingTargets.Contains(target)) throw new IOException("Invalid Windows gaming setting.");
        switch (target) {
            case "game-mode":
                using (var bar = Registry.CurrentUser.OpenSubKey(WindowsGamingProbe.GameBarKey))
                    return WindowsGamingParsing.FlagState(bar?.GetValue("AutoGameModeEnabled") is int g ? g != 0 : null);
            case "background-recording":
                using (var dvr = Registry.CurrentUser.OpenSubKey(WindowsGamingProbe.GameDvrKey))
                    return WindowsGamingParsing.FlagState(dvr?.GetValue("HistoricalCaptureEnabled") is int h ? h != 0 : null);
            case "mouse-acceleration":
                return WindowsGamingProbe.Mouse() is { } mouse ? WindowsGamingParsing.MouseText(mouse) : throw new IOException("Windows didn't report the mouse settings.");
            default:
                using (var key = Registry.CurrentUser.OpenSubKey(WindowsGamingProbe.GpuPreferencesKey))
                    return WindowsGamingParsing.FlagState(WindowsGamingParsing.Flag(key?.GetValue(WindowsGamingProbe.DirectXGlobalValue) as string, DirectXFlag(target)));
        }
    }
    private static void SetWindowsGaming(string target, string value)
    {
        if (!WindowsGamingTargets.Contains(target)) throw new IOException("Invalid Windows gaming setting.");
        try {
            switch (target) {
                case "game-mode" or "background-recording": {
                    var on = WindowsGamingParsing.ParseFlagState(value);
                    var (keyPath, name) = target == "game-mode" ? (WindowsGamingProbe.GameBarKey, "AutoGameModeEnabled") : (WindowsGamingProbe.GameDvrKey, "HistoricalCaptureEnabled");
                    using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
                    if (on is { } v) key.SetValue(name, v ? 1 : 0, RegistryValueKind.DWord); else key.DeleteValue(name, throwOnMissingValue: false);
                    break;
                }
                case "mouse-acceleration": WindowsGamingProbe.SetMouse(WindowsGamingParsing.ParseMouse(value)); break;
                default: {
                    var on = WindowsGamingParsing.ParseFlagState(value);
                    using var key = Registry.CurrentUser.CreateSubKey(WindowsGamingProbe.GpuPreferencesKey, writable: true);
                    var updated = WindowsGamingParsing.WithFlag(key.GetValue(WindowsGamingProbe.DirectXGlobalValue) as string, DirectXFlag(target), on);
                    if (updated.Length == 0) key.DeleteValue(WindowsGamingProbe.DirectXGlobalValue, throwOnMissingValue: false);
                    else key.SetValue(WindowsGamingProbe.DirectXGlobalValue, updated, RegistryValueKind.String);
                    break;
                }
            }
        } catch (FormatException ex) { throw new IOException(ex.Message, ex); }
    }

    // ---- NVIDIA per-application profile settings --------------------------------------------------------------
    internal static (string Executable, uint Id) ParseNvidiaTarget(string target)
    {
        var parts = target.Split('|');
        if (parts.Length != 2 || !parts[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || parts[0].IndexOfAny(['\\', '/', ':']) >= 0 ||
            !parts[1].StartsWith("0x", StringComparison.Ordinal) || !uint.TryParse(parts[1][2..], System.Globalization.NumberStyles.HexNumber, null, out var id) ||
            NvidiaSettings.Catalog.All(s => s.Id != id))
            throw new IOException("Invalid NVIDIA setting target.");
        return (parts[0], id);
    }
    /// <summary>A catalog setting id, "0x1057EB71", in the global profile.</summary>
    internal static uint ParseNvidiaGlobalTarget(string target) =>
        target.Length == 10 && target.StartsWith("0x", StringComparison.Ordinal) && uint.TryParse(target[2..], System.Globalization.NumberStyles.HexNumber, null, out var id) &&
        NvidiaSettings.Catalog.Any(s => s.Id == id) ? id : throw new IOException("Invalid NVIDIA setting target.");
    private static string NvidiaSetting(string target)
    {
        var (exe, id) = ParseNvidiaTarget(target);
        return Nvidia.ReadProfileSetting(exe, id);
    }
    private static void SetNvidiaSetting(string target, string value)
    {
        var (exe, id) = ParseNvidiaTarget(target);
        Nvidia.WriteProfileSetting(exe, id, value);
    }
}
