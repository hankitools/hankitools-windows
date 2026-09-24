using System.Globalization;
namespace IgezziGuard;

public enum GpuVendor { Nvidia, Amd, Intel, Microsoft, Other }

/// <param name="PreferenceRank">Position in Windows' high-performance GPU order (0 = the GPU Windows gives demanding apps).</param>
public sealed record GpuAdapter(string Name, GpuVendor Vendor, uint VendorId, uint DeviceId, long Luid, ulong DedicatedMemory, ulong SharedMemory,
    int PreferenceRank, string? DriverVersion, DateTime? DriverDate, bool LikelyIntegrated);

public sealed record DisplayMode(int Width, int Height, double RefreshHz);

/// <param name="Device">GDI name such as \\.\DISPLAY1, used to read and change display modes.</param>
public sealed record DisplayInfo(string Name, string Device, long AdapterLuid, DisplayMode Current, IReadOnlyList<DisplayMode> Supported,
    bool? HdrSupported, bool? HdrEnabled, bool Primary, string Connection);

/// <param name="Portable">A battery is present (laptop or tablet). Null when Windows doesn't say.</param>
public sealed record GraphicsInventory(IReadOnlyList<GpuAdapter> Adapters, IReadOnlyList<DisplayInfo> Displays, bool? Portable, bool OnAcPower,
    bool NvapiAvailable, bool AdlxPresent, IReadOnlyList<string> Notes)
{
    public bool Hybrid => Adapters.Count(a => a.LikelyIntegrated) >= 1 && Adapters.Any(a => !a.LikelyIntegrated);
    public GpuAdapter? HighPerformance => Adapters.OrderBy(a => a.PreferenceRank).FirstOrDefault();
    public GpuAdapter? AdapterFor(DisplayInfo display) => Adapters.FirstOrDefault(a => a.Luid == display.AdapterLuid);
}

/// <summary>Plain rules over what Windows reports about graphics hardware (HANKI-GPU-101). No Windows calls, so they can be tested.</summary>
public static class GraphicsFacts
{
    public const ulong GiB = 1024UL * 1024 * 1024;
    public static GpuVendor Vendor(uint pciVendor) => pciVendor switch {
        0x10DE => GpuVendor.Nvidia, 0x1002 or 0x1022 => GpuVendor.Amd, 0x8086 => GpuVendor.Intel, 0x1414 => GpuVendor.Microsoft, _ => GpuVendor.Other
    };
    public static string VendorName(GpuVendor vendor) => vendor switch {
        GpuVendor.Nvidia => "NVIDIA", GpuVendor.Amd => "AMD", GpuVendor.Intel => "Intel", GpuVendor.Microsoft => "Microsoft", _ => "Other"
    };

    /// <summary>
    /// Integrated GPUs share system memory and report little or no dedicated video memory. Intel Arc and AMD Radeon
    /// cards report several GiB, so the vendor alone doesn't decide it.
    /// </summary>
    public static bool LikelyIntegrated(GpuVendor vendor, ulong dedicatedMemory) => vendor switch {
        GpuVendor.Intel => dedicatedMemory < 2 * GiB,
        GpuVendor.Amd => dedicatedMemory <= 1 * GiB,
        GpuVendor.Nvidia => false,
        _ => dedicatedMemory < 512UL * 1024 * 1024
    };

    /// <summary>NVIDIA's own version from the Windows driver version: 32.0.16.1692 is NVIDIA 616.92.</summary>
    public static string DriverVersion(GpuVendor vendor, string? windowsVersion)
    {
        if (string.IsNullOrWhiteSpace(windowsVersion)) return "Unknown";
        var parts = windowsVersion.Split('.');
        if (vendor == GpuVendor.Nvidia && parts.Length == 4 && parts[2].Length >= 1 && parts[3].Length == 4 && parts[2].All(char.IsDigit) && parts[3].All(char.IsDigit)) {
            var digits = (parts[2] + parts[3])[^5..];
            return $"{int.Parse(digits[..3], CultureInfo.InvariantCulture)}.{digits[3..]} ({windowsVersion})";
        }
        return windowsVersion;
    }

    public static string Memory(ulong bytes) => bytes >= GiB ? $"{bytes / (double)GiB:0.#} GB" : $"{bytes / (1024d * 1024):0} MB";

    /// <summary>Refresh rates within half a hertz are the same rate (59.94 Hz and 60 Hz, 319.9 Hz and 320 Hz).</summary>
    public static bool SameRate(double a, double b) => Math.Abs(a - b) < 0.6;

    /// <summary>The fastest refresh rate the display offers at its current resolution.</summary>
    public static double MaxRefreshAtCurrentResolution(DisplayInfo display) =>
        display.Supported.Where(m => m.Width == display.Current.Width && m.Height == display.Current.Height).Select(m => m.RefreshHz).DefaultIfEmpty(display.Current.RefreshHz).Max();

    /// <summary>
    /// A faster mode at the same resolution, when the display runs at least 10% below it (60 Hz on a 165 Hz monitor,
    /// not 144 Hz on a 144.2 Hz one). Null when the display already uses its fastest rate.
    /// </summary>
    public static DisplayMode? FasterRefresh(DisplayInfo display)
    {
        double best = MaxRefreshAtCurrentResolution(display);
        return best >= display.Current.RefreshHz * 1.1 && !SameRate(best, display.Current.RefreshHz)
            ? new DisplayMode(display.Current.Width, display.Current.Height, best) : null;
    }
    public static string Describe(DisplayMode mode) => $"{mode.Width} × {mode.Height} @ {Math.Round(mode.RefreshHz):0} Hz";
    /// <summary>A display mode as the Recovery journal stores it: "1920x1080@165".</summary>
    public static string ModeText(int width, int height, int refresh) => $"{width}x{height}@{refresh}";
}

// ---- Windows gaming settings (HANKI-GPU-108) -----------------------------------------------------------

public enum GpuPreference { LetWindowsDecide = 0, PowerSaving = 1, HighPerformance = 2 }

/// <summary>A per-app choice from Settings → System → Display → Graphics.</summary>
public sealed record AppGpuPreference(string Application, GpuPreference Preference);

/// <param name="GameMode">Null when the setting was never changed: Game Mode is then on (the Windows default).</param>
/// <param name="HardwareScheduling">Hardware-accelerated GPU scheduling; null when Windows uses its default.</param>
/// <param name="PowerMode">Settings → Power mode (Best power efficiency, Balanced, Best performance), when Windows reports it.</param>
/// <param name="ProcessorMaximum">Maximum processor state (%) of the active power plan for the current power source.</param>
public sealed record WindowsGamingSettings(bool? GameMode, bool? HardwareScheduling, IReadOnlyList<AppGpuPreference> GpuPreferences,
    bool? WindowedOptimizations, bool? AutoHdr, string? PowerMode, Guid? PowerPlan, string? PowerPlanName,
    int? ProcessorMaximumAc, int? ProcessorMaximumDc, int? ProcessorMinimumAc);

public static class WindowsGamingParsing
{
    /// <summary>Parses "GpuPreference=2;" as stored by the Windows Graphics settings page.</summary>
    public static GpuPreference? Preference(string? data)
    {
        var value = Setting(data, "GpuPreference");
        return value is "0" or "1" or "2" ? (GpuPreference)int.Parse(value, CultureInfo.InvariantCulture) : null;
    }
    /// <summary>Reads one "Name=value;" pair from a DirectX user settings string.</summary>
    public static string? Setting(string? data, string name)
    {
        if (string.IsNullOrEmpty(data)) return null;
        foreach (var part in data.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals(name, StringComparison.OrdinalIgnoreCase)) return pair[1].Trim();
        }
        return null;
    }
    public static bool? Flag(string? data, string name) => Setting(data, name) switch { "1" => true, "0" => false, _ => null };
    public static string PreferenceText(GpuPreference preference) => preference switch {
        GpuPreference.PowerSaving => "Power saving", GpuPreference.HighPerformance => "High performance", _ => "Let Windows decide"
    };
    /// <summary>Windows 11 power mode overlay GUIDs.</summary>
    public static string? PowerModeName(Guid overlay) => overlay.ToString() switch {
        "961cc777-2547-4f9d-8174-7d86181b8a7a" => "Best power efficiency",
        "00000000-0000-0000-0000-000000000000" => "Balanced",
        "ded574b5-45a0-4f42-8737-46345c09c238" => "Best performance",
        _ => null
    };
}
