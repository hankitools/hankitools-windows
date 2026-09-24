using System.Globalization;
namespace IgezziGuard;

public enum NvidiaSettingKind
{
    PowerManagement, FrameRateLimit, VerticalSync, PreRenderedFrames, TextureFiltering,
    AnisotropicMode, AnisotropicLevel, AnisotropicSampleOptimization, NegativeLodBias, ThreadedOptimization, ShaderCacheSize,
    Fxaa, AmbientOcclusion, MonitorTechnology, PreferredRefreshRate, TripleBuffering
}

/// <summary>
/// The NVIDIA driver settings Hanki reads and changes (HANKI-GPU-102). Ids and values come from NVIDIA's public NVAPI SDK
/// (NvApiDriverSettings.h); the driver also returns each setting's name, which the probe compares before trusting
/// an id. Anything else in a profile is left alone.
/// </summary>
/// <param name="Values">Named values; for the editor and presets these are the only values offered, apart from the frame limit.</param>
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

/// <summary>
/// One setting of the global profile in the form Recovery stores: State is "0x…" (a value set by you or Hanki),
/// "predefined:0x…" (NVIDIA's own value) or "default" (not set, so the driver default applies). Effective is the
/// value games get, and Reset the state NVIDIA's defaults would give.
/// </summary>
public sealed record NvidiaGlobalSetting(NvidiaSetting Setting, string State, uint? Effective, string Reset)
{
    public string Text => Effective is { } v ? Setting.Describe(v) : "Not set";
    public bool IsDefault => State == Reset;
}

public static class NvidiaSettings
{
    public const uint PowerManagementId = 0x1057EB71, FrameRateLimitId = 0x10835002, VerticalSyncId = 0x00A879CF, PreRenderedFramesId = 0x007BA09E, TextureFilteringId = 0x00CE2691;
    public const uint AnisotropicModeId = 0x10D2BB16, AnisotropicLevelId = 0x101E61A9, AnisotropicSampleOptimizationId = 0x00E73211, NegativeLodBiasId = 0x0019BB68,
        ThreadedOptimizationId = 0x20C1221E, ShaderCacheSizeId = 0x00AC8497, FxaaId = 0x1074C972, AmbientOcclusionId = 0x00667329, MonitorTechnologyId = 0x10A879CF,
        PreferredRefreshRateId = 0x0064B541, TripleBufferingId = 0x20FDD1F9;
    public const uint PowerAdaptive = 0, PowerPreferMaximum = 1, PowerDriverControlled = 2, PowerConsistent = 3, PowerPreferMinimum = 4, PowerNormal = 5;
    public const uint VsyncApplication = 0x60925292, VsyncOff = 0x08416747, VsyncOn = 0x47814940, VsyncFast = 0x18888888;
    public const uint TextureHighQuality = 0xFFFFFFF6, TextureQuality = 0, TexturePerformance = 10, TextureHighPerformance = 20;
    public const uint ShaderCacheUnlimited = 0xFFFFFFFF;
    /// <summary>NVIDIA Control Panel's frame limiter range; 0 turns it off.</summary>
    public const uint FrameRateLimitMinimum = 20, FrameRateLimitMaximum = 1000;

    public static readonly IReadOnlyList<NvidiaSetting> Catalog = [
        new(PowerManagementId, NvidiaSettingKind.PowerManagement, "Power management mode", new Dictionary<uint, string> {
            [PowerAdaptive] = "Adaptive", [PowerPreferMaximum] = "Prefer maximum performance", [PowerDriverControlled] = "Driver controlled",
            [PowerConsistent] = "Prefer consistent performance", [PowerPreferMinimum] = "Prefer maximum power savings", [PowerNormal] = "Normal" }),
        new(PreRenderedFramesId, NvidiaSettingKind.PreRenderedFrames, "Max pre-rendered frames (Low Latency Mode)", new Dictionary<uint, string> {
            [0] = "Use the 3D application setting", [1] = "1 frame (Low Latency Mode on)", [2] = "2 frames", [3] = "3 frames", [4] = "4 frames" }),
        new(VerticalSyncId, NvidiaSettingKind.VerticalSync, "Vertical sync", new Dictionary<uint, string> {
            [VsyncApplication] = "Use the 3D application setting", [VsyncOff] = "Off", [VsyncOn] = "On", [VsyncFast] = "Fast",
            [0x32610244] = "1/2 refresh rate", [0x71271021] = "1/3 refresh rate", [0x13245256] = "1/4 refresh rate" }),
        new(FrameRateLimitId, NvidiaSettingKind.FrameRateLimit, "Max frame rate", new Dictionary<uint, string>()),
        new(MonitorTechnologyId, NvidiaSettingKind.MonitorTechnology, "Monitor technology (G-SYNC)", new Dictionary<uint, string> {
            [0] = "G-SYNC / G-SYNC Compatible", [4] = "Fixed refresh" }),
        new(PreferredRefreshRateId, NvidiaSettingKind.PreferredRefreshRate, "Preferred refresh rate", new Dictionary<uint, string> {
            [0] = "Application-controlled", [1] = "Highest available" }),
        new(TextureFilteringId, NvidiaSettingKind.TextureFiltering, "Texture filtering - Quality", new Dictionary<uint, string> {
            [TextureHighQuality] = "High quality", [TextureQuality] = "Quality", [TexturePerformance] = "Performance", [TextureHighPerformance] = "High performance" }),
        new(AnisotropicModeId, NvidiaSettingKind.AnisotropicMode, "Anisotropic filtering", new Dictionary<uint, string> {
            [0] = "Use the 3D application setting", [1] = "Set by the level below" }),
        new(AnisotropicLevelId, NvidiaSettingKind.AnisotropicLevel, "Anisotropic filtering level", new Dictionary<uint, string> {
            [1] = "Off", [2] = "2x", [4] = "4x", [8] = "8x", [16] = "16x" }),
        new(AnisotropicSampleOptimizationId, NvidiaSettingKind.AnisotropicSampleOptimization, "Texture filtering - Anisotropic sample optimization", new Dictionary<uint, string> {
            [0] = "Off", [1] = "On" }),
        new(NegativeLodBiasId, NvidiaSettingKind.NegativeLodBias, "Texture filtering - Negative LOD bias", new Dictionary<uint, string> {
            [0] = "Allow", [1] = "Clamp" }),
        new(ThreadedOptimizationId, NvidiaSettingKind.ThreadedOptimization, "Threaded optimization", new Dictionary<uint, string> {
            [0] = "Auto", [1] = "On", [2] = "Off" }),
        new(ShaderCacheSizeId, NvidiaSettingKind.ShaderCacheSize, "Shader cache size", new Dictionary<uint, string> {
            [ShaderCacheUnlimited] = "Unlimited" }),
        new(FxaaId, NvidiaSettingKind.Fxaa, "Antialiasing - FXAA", new Dictionary<uint, string> {
            [0] = "Off", [1] = "On" }),
        new(AmbientOcclusionId, NvidiaSettingKind.AmbientOcclusion, "Ambient occlusion", new Dictionary<uint, string> {
            [0] = "Off", [1] = "Performance", [2] = "Medium", [3] = "Quality" }),
        new(TripleBufferingId, NvidiaSettingKind.TripleBuffering, "Triple buffering (OpenGL)", new Dictionary<uint, string> {
            [0] = "Off", [1] = "On" }),
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
        bool gsync = n.Contains("g-sync") || n.Contains("gsync");
        return setting.Kind switch {
            NvidiaSettingKind.PowerManagement => n.Contains("power"),
            NvidiaSettingKind.FrameRateLimit => n.Contains("frame") && (n.Contains("rate") || n.Contains("limit")),
            NvidiaSettingKind.VerticalSync => n.Contains("sync") && !gsync,
            NvidiaSettingKind.PreRenderedFrames => n.Contains("pre-rendered") || n.Contains("prerender") || n.Contains("latency"),
            NvidiaSettingKind.TextureFiltering => n.Contains("texture") && n.Contains("quality"),
            NvidiaSettingKind.AnisotropicMode => n.Contains("anisotropic") && n.Contains("mode"),
            NvidiaSettingKind.AnisotropicLevel => n.Contains("anisotropic") && (n.Contains("setting") || n.Contains("level")) && !n.Contains("mode"),
            NvidiaSettingKind.AnisotropicSampleOptimization => n.Contains("anisotropic") && n.Contains("sample"),
            NvidiaSettingKind.NegativeLodBias => n.Contains("negative") && n.Contains("lod"),
            NvidiaSettingKind.ThreadedOptimization => n.Contains("thread"),
            NvidiaSettingKind.ShaderCacheSize => n.Contains("shader") && n.Contains("size"),
            NvidiaSettingKind.Fxaa => n.Contains("fxaa") && !n.Contains("indicator") && !n.Contains("predefined"),
            NvidiaSettingKind.AmbientOcclusion => n.Contains("ambient") && n.Contains("occlusion") && !n.Contains("predefined"),
            NvidiaSettingKind.MonitorTechnology => gsync || n.Contains("monitor technology"),
            NvidiaSettingKind.PreferredRefreshRate => n.Contains("refresh") && n.Contains("rate"),
            NvidiaSettingKind.TripleBuffering => n.Contains("triple"),
            _ => false
        };
    }

    /// <summary>Values Hanki offers for a setting; anything else is only ever restored, never chosen.</summary>
    public static bool Allowed(NvidiaSetting setting, uint value) => setting.Kind == NvidiaSettingKind.FrameRateLimit
        ? value == 0 || value is >= FrameRateLimitMinimum and <= FrameRateLimitMaximum
        : setting.Values.ContainsKey(value);

    public static NvidiaSettingSource Source(uint location) => location switch {
        0 => NvidiaSettingSource.ThisProfile, 1 => NvidiaSettingSource.GlobalProfile, 2 => NvidiaSettingSource.BaseProfile, 3 => NvidiaSettingSource.DriverDefault, _ => NvidiaSettingSource.NotSet
    };
    public static string SourceText(NvidiaSettingSource source) => source switch {
        NvidiaSettingSource.ThisProfile => "set in this profile", NvidiaSettingSource.GlobalProfile or NvidiaSettingSource.BaseProfile => "from the global settings",
        NvidiaSettingSource.DriverDefault => "driver default", _ => "not set"
    };
    public static string Hex(uint value) => "0x" + value.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>The Recovery state of a setting read from a profile: "0x…", "predefined:0x…" or "default".</summary>
    public static string State(uint value, NvidiaSettingSource source, bool predefined) =>
        source != NvidiaSettingSource.ThisProfile ? "default" : (predefined ? "predefined:" : "") + Hex(value);
    /// <summary>The state NVIDIA's defaults give: its predefined value for the profile when it has one, otherwise not set.</summary>
    public static string ResetState(uint? predefinedValue) => predefinedValue is { } v ? "predefined:" + Hex(v) : "default";
    /// <summary>The value a state stands for, or null for "default".</summary>
    public static uint? StateValue(string state)
    {
        string hex = state.StartsWith("predefined:", StringComparison.Ordinal) ? state[11..] : state;
        return hex.StartsWith("0x", StringComparison.Ordinal) && uint.TryParse(hex[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
    public static bool ValidState(string state) => state == "default" || StateValue(state) is not null;
}
