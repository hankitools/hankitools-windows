using System.Globalization;
namespace IgezziGuard;

public enum NvidiaSettingKind { PowerManagement, FrameRateLimit, VerticalSync, PreRenderedFrames, TextureFiltering }

/// <summary>
/// The NVIDIA driver settings Hanki reads (HANKI-GPU-102). Ids and values come from NVIDIA's public NVAPI SDK
/// (NvApiDriverSettings.h); the driver also returns each setting's name, which the probe compares before trusting
/// an id. Anything else in a profile is left alone.
/// </summary>
public sealed record NvidiaSetting(uint Id, NvidiaSettingKind Kind, string Name, IReadOnlyDictionary<uint, string> Values)
{
    public string Describe(uint value) => Kind == NvidiaSettingKind.FrameRateLimit ? (value == 0 ? "Off" : $"{value} FPS")
        : Values.TryGetValue(value, out var name) ? name : $"Value 0x{value:X8}";
}

public enum NvidiaSettingSource { ThisProfile, GlobalProfile, BaseProfile, DriverDefault, NotSet }

/// <param name="Source">Where the value comes from: set in this profile, inherited from the global/base profile, or the driver default.</param>
public sealed record NvidiaValue(NvidiaSetting Setting, uint? Value, NvidiaSettingSource Source, bool Predefined)
{
    public string Text => Value is { } v ? Setting.Describe(v) : "Not set";
}

/// <param name="Application">Executable file name, or null for the global profile.</param>
public sealed record NvidiaProfileView(string ProfileName, string? Application, bool Predefined, IReadOnlyList<NvidiaValue> Values);

public static class NvidiaSettings
{
    public const uint PowerManagementId = 0x1057EB71, FrameRateLimitId = 0x10835002, VerticalSyncId = 0x00A879CF, PreRenderedFramesId = 0x007BA09E, TextureFilteringId = 0x00CE2691;
    public const uint PowerAdaptive = 0, PowerPreferMaximum = 1, PowerDriverControlled = 2, PowerConsistent = 3, PowerPreferMinimum = 4, PowerNormal = 5;
    public const uint VsyncApplication = 0x60925292, VsyncOff = 0x08416747, VsyncOn = 0x47814940, VsyncFast = 0x18888888;

    public static readonly IReadOnlyList<NvidiaSetting> Catalog = [
        new(PowerManagementId, NvidiaSettingKind.PowerManagement, "Power management mode", new Dictionary<uint, string> {
            [PowerAdaptive] = "Adaptive", [PowerPreferMaximum] = "Prefer maximum performance", [PowerDriverControlled] = "Driver controlled",
            [PowerConsistent] = "Prefer consistent performance", [PowerPreferMinimum] = "Prefer maximum power savings", [PowerNormal] = "Normal" }),
        new(FrameRateLimitId, NvidiaSettingKind.FrameRateLimit, "Max frame rate", new Dictionary<uint, string>()),
        new(VerticalSyncId, NvidiaSettingKind.VerticalSync, "Vertical sync", new Dictionary<uint, string> {
            [VsyncApplication] = "Use the 3D application setting", [VsyncOff] = "Off", [VsyncOn] = "On", [VsyncFast] = "Fast",
            [0x32610244] = "1/2 refresh rate", [0x71271021] = "1/3 refresh rate", [0x13245256] = "1/4 refresh rate" }),
        new(PreRenderedFramesId, NvidiaSettingKind.PreRenderedFrames, "Max pre-rendered frames (Low Latency Mode)", new Dictionary<uint, string> {
            [0] = "Use the 3D application setting", [1] = "1 frame (Low Latency Mode on)", [2] = "2 frames", [3] = "3 frames", [4] = "4 frames" }),
        new(TextureFilteringId, NvidiaSettingKind.TextureFiltering, "Texture filtering - Quality", new Dictionary<uint, string> {
            [0xFFFFFFF6] = "High quality", [0] = "Quality", [10] = "Performance", [20] = "High performance" }),
    ];
    public static NvidiaSetting Get(uint id) => Catalog.Single(s => s.Id == id);

    /// <summary>
    /// The driver's own setting name must resemble Hanki's before an id is trusted, so a changed or unexpected id is
    /// reported as unknown instead of being misread. Empty names (older drivers) are accepted.
    /// </summary>
    public static bool NameMatches(NvidiaSetting setting, string driverName)
    {
        if (string.IsNullOrWhiteSpace(driverName)) return true;
        string n = driverName.ToLowerInvariant();
        return setting.Kind switch {
            NvidiaSettingKind.PowerManagement => n.Contains("power"),
            NvidiaSettingKind.FrameRateLimit => n.Contains("frame") && (n.Contains("rate") || n.Contains("limit")),
            NvidiaSettingKind.VerticalSync => n.Contains("sync"),
            NvidiaSettingKind.PreRenderedFrames => n.Contains("pre-rendered") || n.Contains("prerender") || n.Contains("latency"),
            NvidiaSettingKind.TextureFiltering => n.Contains("texture") && n.Contains("quality"),
            _ => false
        };
    }
    public static NvidiaSettingSource Source(uint location) => location switch {
        0 => NvidiaSettingSource.ThisProfile, 1 => NvidiaSettingSource.GlobalProfile, 2 => NvidiaSettingSource.BaseProfile, 3 => NvidiaSettingSource.DriverDefault, _ => NvidiaSettingSource.NotSet
    };
    public static string SourceText(NvidiaSettingSource source) => source switch {
        NvidiaSettingSource.ThisProfile => "set in this profile", NvidiaSettingSource.GlobalProfile or NvidiaSettingSource.BaseProfile => "from the global settings",
        NvidiaSettingSource.DriverDefault => "driver default", _ => "not set"
    };
    public static string Hex(uint value) => "0x" + value.ToString("X8", CultureInfo.InvariantCulture);
}
