using IgezziGuard;

// HANKI-GPU-100 and HANKI-GAME-200: hardware facts, the Gaming Health Scan, goal profiles and the game library.
internal static class GamingChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    private static bool Throws(Action action) { try { action(); return false; } catch (IOException) { return true; } }
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    internal static void Run() { Facts(); Health(); Profiles(); NvidiaGlobal(); WindowsGaming(); Tune(); TuneSteps(); Radeon(); Background(); Launch(); Library(); GpuConnection(); Live(); }

    private static GpuAdapter Gpu(GpuVendor vendor, string name, ulong memory, int rank, long luid) =>
        new(name, vendor, 0, 0, luid, memory, 8 * GraphicsFacts.GiB, rank, "32.0.16.1692", new DateTime(2026, 9, 4), GraphicsFacts.LikelyIntegrated(vendor, memory));
    private static DisplayInfo Display(double current, long luid = 1, params double[] rates) =>
        new("Monitor", @"\\.\DISPLAY1", luid, new(2560, 1440, current), rates.Select(r => new DisplayMode(2560, 1440, r)).Append(new DisplayMode(1920, 1080, 240)).ToArray(), null, null, true, "DisplayPort");
    private static GraphicsInventory Desktop(DisplayInfo display, params GpuAdapter[] gpus) => new(gpus.Length == 0 ? [Gpu(GpuVendor.Nvidia, "RTX 5070", 12 * GraphicsFacts.GiB, 0, 1)] : gpus, [display], false, true, true, false, []);
    private static WindowsGamingSettings Windows(bool? gameMode = null, string? powerMode = "Balanced", int? maxAc = 100, params AppGpuPreference[] apps) =>
        new(gameMode, null, apps, null, null, powerMode, Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), "Balanced", maxAc, 100, 5);
    private static NvidiaProfileView Global(uint power = NvidiaSettings.PowerNormal, uint fps = 0) => new("Base Profile", null, true, [
        new(NvidiaSettings.Get(NvidiaSettings.PowerManagementId), power, NvidiaSettingSource.DriverDefault, false),
        new(NvidiaSettings.Get(NvidiaSettings.FrameRateLimitId), fps, NvidiaSettingSource.DriverDefault, false),
        new(NvidiaSettings.Get(NvidiaSettings.VerticalSyncId), NvidiaSettings.VsyncApplication, NvidiaSettingSource.DriverDefault, false),
        new(NvidiaSettings.Get(NvidiaSettings.PreRenderedFramesId), 0, NvidiaSettingSource.DriverDefault, false),
        new(NvidiaSettings.Get(NvidiaSettings.TextureFilteringId), 0, NvidiaSettingSource.DriverDefault, false)]);

    private static void Facts()
    {
        Check(GraphicsFacts.Vendor(0x10DE) == GpuVendor.Nvidia && GraphicsFacts.Vendor(0x1002) == GpuVendor.Amd && GraphicsFacts.Vendor(0x8086) == GpuVendor.Intel, "graphics: PCI vendor ids");
        Check(GraphicsFacts.LikelyIntegrated(GpuVendor.Intel, 128UL << 20) && !GraphicsFacts.LikelyIntegrated(GpuVendor.Intel, 8 * GraphicsFacts.GiB)
            && GraphicsFacts.LikelyIntegrated(GpuVendor.Amd, 512UL << 20) && !GraphicsFacts.LikelyIntegrated(GpuVendor.Amd, 16 * GraphicsFacts.GiB) && !GraphicsFacts.LikelyIntegrated(GpuVendor.Nvidia, 0),
            "graphics: integrated vs dedicated uses memory as well as vendor (Intel Arc, Radeon APUs)");
        Check(GraphicsFacts.DriverVersion(GpuVendor.Nvidia, "32.0.16.1692").StartsWith("616.92") && GraphicsFacts.DriverVersion(GpuVendor.Nvidia, "31.0.15.5186").StartsWith("551.86")
            && GraphicsFacts.DriverVersion(GpuVendor.Amd, "31.0.21001.45002") == "31.0.21001.45002", "graphics: NVIDIA driver versions are shown the way NVIDIA names them");
        Check(GraphicsFacts.FasterRefresh(Display(60, 1, 60, 144, 165)) is { RefreshHz: 165 } && GraphicsFacts.FasterRefresh(Display(319.975, 1, 60, 240, 320)) is null
            && GraphicsFacts.FasterRefresh(Display(144, 1, 60, 144)) is null && GraphicsFacts.FasterRefresh(Display(120, 1, 120, 144)) is { RefreshHz: 144 },
            "graphics: a faster refresh rate is found at the same resolution, ignoring rounding such as 319.975 vs 320 Hz");
        Check(WindowsGamingParsing.Preference("GpuPreference=2;") == GpuPreference.HighPerformance && WindowsGamingParsing.Preference("SwapEffectUpgradeEnable=1;GpuPreference=1;") == GpuPreference.PowerSaving
            && WindowsGamingParsing.Preference("nonsense") is null && WindowsGamingParsing.Flag("VRROptimizeEnable=0;AutoHDREnable=1;", "AutoHDREnable") == true,
            "windows gaming: per-app GPU choices and DirectX settings are parsed as Settings stores them");
        Check(WindowsGamingParsing.PowerModeName(Guid.Parse("961cc777-2547-4f9d-8174-7d86181b8a7a")) == "Best power efficiency" && WindowsGamingParsing.PowerModeName(Guid.Empty) == "Balanced", "windows gaming: power modes");
        Check(new[] { ("Power management mode", NvidiaSettings.PowerManagementId), ("Frame Rate Limiter", NvidiaSettings.FrameRateLimitId), ("Vertical Sync", NvidiaSettings.VerticalSyncId),
            ("Maximum pre-rendered frames", NvidiaSettings.PreRenderedFramesId), ("Texture filtering - Quality", NvidiaSettings.TextureFilteringId) }.All(p => NvidiaSettings.NameMatches(NvidiaSettings.Get(p.Item2), p.Item1)),
            "nvidia: the driver's names for the five settings are recognised (as read from a 616.92 driver)");
        Check(!NvidiaSettings.NameMatches(NvidiaSettings.Get(NvidiaSettings.FrameRateLimitId), "Vertical Sync") && !NvidiaSettings.NameMatches(NvidiaSettings.Get(NvidiaSettings.PowerManagementId), "Anisotropic filtering"),
            "nvidia: an id the driver names differently isn't trusted");
        Check(NvidiaSettings.Get(NvidiaSettings.PowerManagementId).Describe(5) == "Normal" && NvidiaSettings.Get(NvidiaSettings.FrameRateLimitId).Describe(0) == "Off"
            && NvidiaSettings.Get(NvidiaSettings.FrameRateLimitId).Describe(162) == "162 FPS" && NvidiaSettings.Get(NvidiaSettings.VerticalSyncId).Describe(0x12345678).StartsWith("Value 0x"),
            "nvidia: values have plain names, and unknown values are shown raw instead of guessed");
    }

    // HANKI-GPU-113: how the graphics card is connected.
    private static void GpuConnection()
    {
        GpuAdapter Card(string name, GpuBusInfo bus, GpuVendor vendor = GpuVendor.Nvidia) => Gpu(vendor, name, 12 * GraphicsFacts.GiB, 0, 1) with { Bus = bus };
        IReadOnlyList<DiagnosticResult> Scan(GpuAdapter card, bool laptop = false) =>
            GamingHealth.Evaluate(Desktop(Display(165, 1, 165), card) with { Portable = laptop }, Windows(), Global(), Now);
        DiagnosticResult Lanes(IReadOnlyList<DiagnosticResult> r) => r.Single(x => x.FindingId.StartsWith("gpu-lanes-", StringComparison.Ordinal));
        DiagnosticResult? Bar(IReadOnlyList<DiagnosticResult> r) => r.SingleOrDefault(x => x.FindingId.StartsWith("resizable-bar-", StringComparison.Ordinal));
        const ulong Classic = 256UL << 20, Full = 16UL << 30;

        var slot = Scan(Card("NVIDIA GeForce RTX 4070", new(4, 16, 4, 4, Classic)));
        Check(Lanes(slot) is { Severity: FindingSeverity.Warning } x4 && GamingHealth.BelongsInFixMyPc(x4) && x4.Metadata["current"] == "PCIe 4.0 x4" && x4.Recommendation!.Contains("top full-length slot"),
            "gpu link: a desktop card on x4 of x16 lanes is flagged, and Fix my PC shows it");
        Check(Bar(slot) is { Severity: FindingSeverity.Informational } off && !GamingHealth.BelongsInFixMyPc(off) && off.Recommendation!.Contains("Above 4G Decoding"),
            "resizable bar: off on a supported NVIDIA card is an optional BIOS step, not a Fix my PC item");
        var shared = Lanes(Scan(Card("NVIDIA GeForce RTX 4070", new(8, 16, 4, 4, Full))));
        Check(shared.Severity == FindingSeverity.Informational && shared.Explanation.Contains("costs games little"), "gpu link: x8 of x16 is explained as a small cost, not a problem");
        var yours = Scan(Card("NVIDIA GeForce RTX 5070", new(16, 16, 4, 5, Full)));
        Check(Lanes(yours) is { Severity: FindingSeverity.Healthy } fine && fine.Explanation.Contains("all its lanes") && fine.Explanation.Contains("supports PCIe 5"),
            "gpu link: a full-width card on a slower PCIe generation looks OK and says why the speed matters little");
        Check(Bar(yours) is { Severity: FindingSeverity.Healthy } on && on.Explanation.Contains("16 GB window"), "resizable bar: a window as large as video memory means it's on");
        Check(Lanes(Scan(Card("NVIDIA GeForce RTX 4070 Laptop GPU", new(8, 16, 4, 4, Full)), laptop: true)) is { Severity: FindingSeverity.Healthy } built && built.Explanation.Contains("as this laptop is built"),
            "gpu link: a laptop's fixed lane count is not flagged or called full width");
        Check(Bar(Scan(Card("Intel(R) Arc(TM) A770 Graphics", new(16, 16, 4, 4, Classic), GpuVendor.Intel))) is { Severity: FindingSeverity.Warning } arc && GamingHealth.BelongsInFixMyPc(arc),
            "resizable bar: Intel Arc without it is a Fix my PC item, since Arc needs it");
        Check(Bar(Scan(Card("NVIDIA GeForce GTX 1060", new(16, 16, 3, 3, Classic)))) is null && !GpuBus.SupportsResizableBar(Card("AMD Radeon RX 580", new(16, 16, 3, 3, Classic), GpuVendor.Amd))
            && GpuBus.SupportsResizableBar(Card("AMD Radeon RX 7800 XT", new(16, 16, 4, 4, Full), GpuVendor.Amd)),
            "resizable bar: cards whose drivers don't use it get no finding");
        Check(!Scan(Card("NVIDIA GeForce RTX 5070", new(null, null, null, null, null))).Any(x => x.FindingId.StartsWith("gpu-lanes-", StringComparison.Ordinal) || x.FindingId.StartsWith("resizable-bar-", StringComparison.Ordinal)),
            "gpu link: nothing is claimed when Windows doesn't report the link");
    }

    private static void Health()
    {
        var healthy = GamingHealth.Evaluate(Desktop(Display(165, 1, 60, 165)), Windows(), Global(), Now);
        Check(!healthy.Any(r => r.Severity is FindingSeverity.Warning or FindingSeverity.Critical) && healthy.Any(r => r.FindingId == "refresh-1" && r.Severity == FindingSeverity.Healthy)
            && healthy.Any(r => r.FindingId == "game-mode" && r.Severity == FindingSeverity.Healthy), "gaming scan: a well-configured PC shows looks-OK results, no invented warnings");
        Check(healthy.Single(r => r.FindingId == "hags").Explanation.Contains("No change recommended"), "gaming scan: commonly suggested tweaks get an explicit 'no change recommended'");
        Check(healthy.All(r => r.Category == DiagnosticCategory.Performance && r.ModuleId == GamingHealth.ModuleId && r.Metadata.ContainsKey("current") && r.Metadata.ContainsKey("recommended") && r.Metadata.ContainsKey("remedy")),
            "gaming scan: findings use the common result model with current state, recommended state and remedy");

        var slow = GamingHealth.Evaluate(Desktop(Display(60, 1, 60, 144, 165)), Windows(), Global(), Now).Single(r => r.FindingId == "refresh-1");
        Check(slow.Severity == FindingSeverity.Warning && slow.Metadata["refresh"] == "165" && slow.Metadata["impact"] == "High" && GamingHealth.CanApply(slow) && GamingHealth.BelongsInFixMyPc(slow),
            "gaming scan: 60 Hz on a 165 Hz monitor is a high-impact opportunity Hanki can change");

        var hybrid = new GraphicsInventory([Gpu(GpuVendor.Nvidia, "RTX 5070 Laptop GPU", 8 * GraphicsFacts.GiB, 0, 2), Gpu(GpuVendor.Intel, "Intel Graphics", 128UL << 20, 1, 1)],
            [Display(165, 1, 60, 165) with { Connection = "Built-in (DisplayPort)" }], true, true, true, false, []);
        var app = new AppGpuPreference(@"C:\Games\WoW\Wow.exe", GpuPreference.PowerSaving);
        var laptop = GamingHealth.Evaluate(hybrid, Windows(apps: app), null, Now);
        Check(laptop.Any(r => r.FindingId == "gpu-preference:wow.exe" && r.Severity == FindingSeverity.Warning && r.Metadata["application"] == app.Application), "gaming scan: a game set to the power-saving GPU on a hybrid laptop");
        Check(!laptop.Any(r => r.FindingId.StartsWith("display-port")), "gaming scan: a laptop's built-in panel on integrated graphics is normal");
        Check(!GamingHealth.Evaluate(Desktop(Display(165, 1, 165)), Windows(apps: app), null, Now).Any(r => r.FindingId.StartsWith("gpu-preference")), "gaming scan: a GPU choice doesn't matter on a single-GPU PC");

        var desktopOnIgpu = new GraphicsInventory([Gpu(GpuVendor.Nvidia, "RTX 5070", 12 * GraphicsFacts.GiB, 0, 2), Gpu(GpuVendor.Intel, "UHD 770", 128UL << 20, 1, 1)], [Display(165, 1, 165)], false, true, false, false, []);
        Check(GamingHealth.Evaluate(desktopOnIgpu, Windows(), null, Now).Any(r => r.FindingId == "display-port-1" && r.Metadata["remedy"] == GamingHealth.RemedyHardware), "gaming scan: a desktop monitor plugged into the motherboard");

        var settings = GamingHealth.Evaluate(Desktop(Display(165, 1, 165)), Windows(gameMode: false, powerMode: "Best power efficiency", maxAc: 80), Global(NvidiaSettings.PowerPreferMinimum, 60), Now);
        Check(settings.Any(r => r.FindingId == "game-mode" && r.Severity == FindingSeverity.Warning) && settings.Any(r => r.FindingId == "power-mode") && settings.Any(r => r.FindingId == "processor-maximum" && r.Metadata["plan"].Length > 0)
            && settings.Any(r => r.FindingId == "nvidia-power" && r.Severity == FindingSeverity.Warning) && settings.Any(r => r.FindingId == "nvidia-fps-cap" && r.Severity == FindingSeverity.Informational),
            "gaming scan: Game Mode off, power-saving modes on mains power, a capped processor and NVIDIA power saving are found");
        var battery = GamingHealth.Evaluate(Desktop(Display(165, 1, 165)) with { OnAcPower = false, Portable = true }, Windows(powerMode: "Best power efficiency", maxAc: 80), null, Now);
        Check(!battery.Any(r => r.FindingId is "power-mode" or "processor-maximum"), "gaming scan: power saving on battery isn't flagged");
        Check(settings.Concat(laptop).All(r => !Navigation.UsesFaultLanguage(r.Title) && !Navigation.UsesFaultLanguage(r.Explanation)), "gaming scan: opportunities aren't described as faults");
        Check(!settings.Single(r => r.FindingId == "game-mode").Metadata["remedy"].Equals(GamingHealth.RemedyDisplayMode) && !GamingHealth.CanApply(settings.Single(r => r.FindingId == "game-mode")),
            "gaming scan: Game Mode is changed in Windows Settings, not by Hanki");
    }

    private static void Profiles()
    {
        var graphics = Desktop(Display(60, 1, 60, 144, 165));
        var findings = GamingHealth.Evaluate(graphics, Windows(maxAc: 80), Global(), Now);
        var balanced = GamingProfiles.Propose(GamingGoal.Balanced, graphics, findings, null);
        Check(balanced.Any(c => c.Kind == "Display mode" && c.After == "2560x1440@165" && c.Target == @"\\.\DISPLAY1") && balanced.Any(c => c.Kind == "Processor power" && c.After == "100" && c.Target!.EndsWith("|ac")),
            "profiles: Balanced fixes the refresh rate and the processor cap, with Recovery-ready entries");
        Check(balanced.All(c => c.Kind is null or "Display mode" or "GPU preference" or "Processor power" or "NVIDIA setting" or "Windows gaming setting"), "profiles: only supported change kinds are proposed");

        var game = new GameContext("World of Warcraft", @"C:\Games\WoW\Wow.exe", null, Global(), null);
        var fixedGraphics = Desktop(Display(165, 1, 165));
        var competitive = GamingProfiles.Propose(GamingGoal.Competitive, fixedGraphics, [], game);
        var power = competitive.SingleOrDefault(c => c.Target == "Wow.exe|0x1057EB71");
        var cap = competitive.SingleOrDefault(c => c.Target == "Wow.exe|0x10835002");
        Check(power is { After: "0x00000001", Optional: false } && competitive.Any(c => c.Target == "Wow.exe|0x007BA09E" && c.After == "0x00000001") && cap is { After: "0x000000A2", Optional: true },
            "profiles: Competitive sets per-game power, low latency and an optional cap 3 below 165 Hz");
        Check(competitive.All(c => c.Kind != "NVIDIA setting" || c.Target!.StartsWith("Wow.exe|")), "profiles: driver changes go into the game's profile, never the global one");
        Check(!competitive.Any(c => c.Target?.EndsWith("0x00A879CF") == true), "profiles: vertical sync is left to the game unless it's forced on");

        var tuned = game with { Nvidia = new NvidiaProfileView("World Of Warcraft", "Wow.exe", true, [new(NvidiaSettings.Get(NvidiaSettings.PowerManagementId), NvidiaSettings.PowerPreferMaximum, NvidiaSettingSource.ThisProfile, false)]) };
        var quiet = GamingProfiles.Propose(GamingGoal.Quiet, fixedGraphics, [], tuned);
        Check(quiet.Any(c => c.Target == "Wow.exe|0x1057EB71" && c.After == "default") && quiet.Any(c => c.Target == "Wow.exe|0x10835002" && c.After == "0x000000A5"),
            "profiles: Quiet removes a forced maximum and caps at the refresh rate");
        var predefined = game with { Nvidia = new NvidiaProfileView("World Of Warcraft", "Wow.exe", true, [new(NvidiaSettings.Get(NvidiaSettings.PowerManagementId), NvidiaSettings.PowerPreferMaximum, NvidiaSettingSource.ThisProfile, true)]) };
        Check(!GamingProfiles.Propose(GamingGoal.Quiet, fixedGraphics, [], predefined).Any(c => c.Target == "Wow.exe|0x1057EB71"), "profiles: NVIDIA's own per-game values are left alone");
        Check(!GamingProfiles.Propose(GamingGoal.Balanced, fixedGraphics, [], game).Any(), "profiles: Balanced on a well-configured PC proposes nothing (no change recommended)");

        var hybrid = new GraphicsInventory([Gpu(GpuVendor.Nvidia, "RTX", 8 * GraphicsFacts.GiB, 0, 2), Gpu(GpuVendor.Intel, "Intel", 128UL << 20, 1, 1)], [Display(165, 1, 165)], true, true, true, false, []);
        Check(GamingProfiles.Propose(GamingGoal.Laptop, hybrid, [], game).Any(c => c.Kind == "GPU preference" && c.After == "GpuPreference=2;" && c.Target == game.ExecutablePath), "profiles: Laptop gaming sets the game to the graphics card");

        var limits = game with { Nvidia = new NvidiaProfileView("WoW", "Wow.exe", true, [new(NvidiaSettings.Get(NvidiaSettings.FrameRateLimitId), 141, NvidiaSettingSource.ThisProfile, false)]), NvidiaGlobal = Global(fps: 144) };
        Check(GamingProfiles.Conflicts(limits, 165).Any(n => n.Contains("Two NVIDIA frame limits")) && GamingProfiles.Conflicts(limits, 165).Last().Contains("isn't visible"), "limiters: two NVIDIA limits are reported, with what Hanki can't see");
        Check(GamingProfiles.Conflicts(game, 165).Count == 0, "limiters: no conflicts, no notes");
    }

    // The driver's own names from NVIDIA's public SDK (NvApiDriverSettings.h), one per catalog setting.
    private static readonly Dictionary<uint, string> SdkNames = new() {
        [NvidiaSettings.PowerManagementId] = "Power management mode", [NvidiaSettings.FrameRateLimitId] = "Frame Rate Limiter", [NvidiaSettings.VerticalSyncId] = "Vertical Sync",
        [NvidiaSettings.PreRenderedFramesId] = "Maximum pre-rendered frames", [NvidiaSettings.TextureFilteringId] = "Texture filtering - Quality",
        [NvidiaSettings.AnisotropicModeId] = "Anisotropic filtering mode", [NvidiaSettings.AnisotropicLevelId] = "Anisotropic filtering setting",
        [NvidiaSettings.AnisotropicSampleOptimizationId] = "Texture filtering - Anisotropic sample optimization", [NvidiaSettings.NegativeLodBiasId] = "Texture filtering - Negative LOD bias",
        [NvidiaSettings.ThreadedOptimizationId] = "Threaded optimization", [NvidiaSettings.ShaderCacheSizeId] = "Shader disk cache maximum size", [NvidiaSettings.FxaaId] = "Enable FXAA",
        [NvidiaSettings.AmbientOcclusionId] = "Ambient Occlusion", [NvidiaSettings.MonitorTechnologyId] = "G-SYNC", [NvidiaSettings.PreferredRefreshRateId] = "Preferred refresh rate",
        [NvidiaSettings.TripleBufferingId] = "Triple buffering",
    };
    /// <summary>Global settings at the driver defaults (values from the SDK's *_DEFAULT entries).</summary>
    private static List<NvidiaGlobalSetting> Defaults() => NvidiaSettings.Catalog.Select(s => new NvidiaGlobalSetting(s, "default", s.Id switch {
        NvidiaSettings.PowerManagementId => NvidiaSettings.PowerNormal, NvidiaSettings.VerticalSyncId => NvidiaSettings.VsyncApplication,
        NvidiaSettings.AnisotropicLevelId => 1u, NvidiaSettings.ShaderCacheSizeId => 0x4000u, _ => 0u }, "default")).ToList();
    private static List<NvidiaGlobalSetting> With(List<NvidiaGlobalSetting> list, uint id, string state, uint? effective, string reset = "default") =>
        list.Select(s => s.Setting.Id == id ? new NvidiaGlobalSetting(s.Setting, state, effective, reset) : s).ToList();

    private static void NvidiaGlobal()
    {
        Check(NvidiaSettings.Catalog.Select(s => s.Id).Distinct().Count() == NvidiaSettings.Catalog.Count && SdkNames.Count == NvidiaSettings.Catalog.Count, "nvidia: every catalog id is unique and has its SDK name");
        Check(NvidiaSettings.Catalog.All(s => NvidiaSettings.NameMatches(s, SdkNames[s.Id])), "nvidia: each setting accepts the driver's own SDK name");
        var confusable = NvidiaSettings.Catalog.SelectMany(s => SdkNames.Where(n => n.Key != s.Id && NvidiaSettings.NameMatches(s, n.Value)).Select(n => $"{s.Name} accepts \"{n.Value}\"")).ToArray();
        Check(confusable.Length == 0, "nvidia: no setting accepts another setting's name" + (confusable.Length > 0 ? ": " + string.Join("; ", confusable) : ""));
        var frl = NvidiaSettings.Get(NvidiaSettings.FrameRateLimitId);
        Check(NvidiaSettings.Allowed(frl, 0) && !NvidiaSettings.Allowed(frl, 19) && NvidiaSettings.Allowed(frl, 20) && NvidiaSettings.Allowed(frl, 1000) && !NvidiaSettings.Allowed(frl, 1001)
            && !NvidiaSettings.Allowed(NvidiaSettings.Get(NvidiaSettings.PowerManagementId), 7) && NvidiaSettings.Allowed(NvidiaSettings.Get(NvidiaSettings.AnisotropicLevelId), 16), "nvidia: only SDK values and the Control Panel frame-limit range are offered");
        Check(NvidiaSettings.State(1, NvidiaSettingSource.ThisProfile, false) == "0x00000001" && NvidiaSettings.State(1, NvidiaSettingSource.ThisProfile, true) == "predefined:0x00000001"
            && NvidiaSettings.State(1, NvidiaSettingSource.BaseProfile, false) == "default" && NvidiaSettings.ResetState(5) == "predefined:0x00000005" && NvidiaSettings.ResetState(null) == "default"
            && NvidiaSettings.StateValue("predefined:0x0000000A") == 10 && NvidiaSettings.StateValue("default") is null && !NvidiaSettings.ValidState("predefined:") && !NvidiaSettings.ValidState("0xZZ"),
            "nvidia: setting states round-trip as Recovery stores them");

        var presets = NvidiaPresets.BuiltIn(165);
        Check(presets.All(p => p.Values.All(v => v.Value is null || NvidiaSettings.Allowed(NvidiaSettings.Get(v.Id), v.Value.Value))), "presets: built-in presets only use offered values");
        Check(presets.All(p => !Navigation.UsesFaultLanguage(p.Description) && p.Values.All(v => !Navigation.UsesFaultLanguage(v.Why))), "presets: explanations aren't written as faults");
        var defaults = Defaults();
        var competitive = NvidiaPresets.Propose(presets.Single(p => p.Name.StartsWith("Competitive")), defaults);
        string? After(IReadOnlyList<ProposedChange> changes, uint id) => changes.SingleOrDefault(c => c.Target == NvidiaSettings.Hex(id))?.After;
        Check(After(competitive, NvidiaSettings.PreRenderedFramesId) == "0x00000001" && After(competitive, NvidiaSettings.PreferredRefreshRateId) == "0x00000001"
            && competitive.Single(c => c.Target == NvidiaSettings.Hex(NvidiaSettings.FrameRateLimitId)) is { After: "0x000000A2", Optional: true }
            && competitive.Single(c => c.Target == NvidiaSettings.Hex(NvidiaSettings.PowerManagementId)).Optional && After(competitive, NvidiaSettings.VerticalSyncId) is null,
            "presets: Competitive turns on low latency and the highest refresh rate, offers full clocks and a cap 3 below 165 Hz, and skips what's already set");
        Check(competitive.All(c => c.Kind == NvidiaPresets.ChangeKind && c.Target!.Length == 10 && Guardrails.Allowed(c) && Navigation.IsPerformanceChange(c.Kind!)), "presets: changes are Recovery-backed global NVIDIA changes");
        Check(NvidiaPresets.Propose(NvidiaPresets.BuiltIn(60).Single(p => p.Name.StartsWith("Competitive")), defaults).All(c => c.Target != NvidiaSettings.Hex(NvidiaSettings.FrameRateLimitId)), "presets: no adaptive-sync cap at 60 Hz");
        Check(After(NvidiaPresets.Propose(presets.Single(p => p.Name == "Maximum FPS"), defaults), NvidiaSettings.FrameRateLimitId) is null, "presets: a value games already get isn't proposed again");
        var reset = presets.Single(p => p.Name == "NVIDIA defaults");
        Check(NvidiaPresets.Propose(reset, defaults).Count == 0, "presets: NVIDIA defaults on default settings proposes nothing");
        var tuned = With(With(defaults, NvidiaSettings.PowerManagementId, "0x00000001", 1), NvidiaSettings.TextureFilteringId, "0x00000014", 20, "predefined:0x00000000");
        var back = NvidiaPresets.Propose(reset, tuned);
        Check(back.Count == 2 && After(back, NvidiaSettings.PowerManagementId) == "default" && After(back, NvidiaSettings.TextureFilteringId) == "predefined:0x00000000",
            "presets: NVIDIA defaults removes your values, or restores NVIDIA's own predefined value");

        var choices = NvidiaPresets.Choices(frl, 165);
        Check(choices[0] == ("NVIDIA default", null) && choices.Any(c => c.Value == 162) && choices.Any(c => c.Value == 165) && choices.Any(c => c.Value == 0)
            && choices.Skip(1).All(c => NvidiaSettings.Allowed(frl, c.Value!.Value)), "editor: frame-limit choices suit the display and stay in range");
        var odd = new NvidiaGlobalSetting(frl, "0x00000025", 37, "default");
        Check(NvidiaPresets.Choices(frl, 165, odd).Any(c => c.Value == 37), "editor: a limit you set elsewhere stays selectable");
        Check(NvidiaPresets.Change(odd, 37, "same") is null && NvidiaPresets.Change(odd, 5000, "out of range") is null && NvidiaPresets.Change(odd, null, "reset") is { After: "default" },
            "editor: unchanged or unsupported values make no change");

        var mine = NvidiaPresets.Capture("  My esports  ", With(tuned, NvidiaSettings.VerticalSyncId, "predefined:0x08416747", NvidiaSettings.VsyncOff, "predefined:0x08416747"));
        Check(mine.Name == "My esports" && mine.Values.Count == defaults.Count && mine.Values[NvidiaSettings.Hex(NvidiaSettings.PowerManagementId)] == "0x00000001"
            && mine.Values[NvidiaSettings.Hex(NvidiaSettings.VerticalSyncId)] == "default" && mine.Values[NvidiaSettings.Hex(NvidiaSettings.FrameRateLimitId)] == "default",
            "my presets: your own values are kept and NVIDIA's are saved as defaults");
        var fromUser = NvidiaPresets.FromUser(mine);
        Check(!fromUser.BuiltIn && fromUser.Values.Single(v => v.Id == NvidiaSettings.PowerManagementId).Value == 1 && NvidiaPresets.Propose(fromUser, tuned).Count == 0
            && NvidiaPresets.Propose(fromUser, defaults).Any(c => c.Target == NvidiaSettings.Hex(NvidiaSettings.PowerManagementId) && c.After == "0x00000001"), "my presets: applying a saved preset restores its values");
        Check(NvidiaPresets.NameProblem("", []) is not null && NvidiaPresets.NameProblem("maximum fps", []) is not null && NvidiaPresets.NameProblem("Mine", ["mine"]) is not null
            && NvidiaPresets.NameProblem(new string('x', 61), []) is not null && NvidiaPresets.NameProblem("Evening", ["Mine"]) is null, "my presets: names are checked");
        var file = Path.Combine(Path.GetTempPath(), "HankiChecks-presets-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            NvidiaPresets.Write(file, [mine]);
            Check(NvidiaPresets.Read(file).Single().Values.SequenceEqual(mine.Values), "my presets: saved presets read back unchanged");
            File.WriteAllText(file, "{\"Version\":1,\"Presets\":[{\"Name\":\"x\",\"Values\":{\"power\":\"0x1\"}}]}");
            bool damaged = false;
            try { NvidiaPresets.Read(file); } catch (IOException) { damaged = true; }
            Check(damaged && File.Exists(file), "my presets: a damaged file is reported and kept");
        } finally { foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(file) + "*")) File.Delete(f); }

        bool refused = false;
        foreach (var target in new[] { "0x12345678", "1057EB71", "Wow.exe|0x1057EB71", "0x1057EB7" }) { try { PerformanceSettings.ParseNvidiaGlobalTarget(target); } catch (IOException) { refused = true; continue; } refused = false; break; }
        Check(refused && PerformanceSettings.ParseNvidiaGlobalTarget("0x1057EB71") == NvidiaSettings.PowerManagementId, "settings: global NVIDIA targets must be a known setting id");
    }

    private static void WindowsGaming()
    {
        Check(WindowsGamingParsing.WithFlag("SwapEffectUpgradeEnable=0;AutoHDREnable=1;Foo=bar;", "SwapEffectUpgradeEnable", true) == "AutoHDREnable=1;Foo=bar;SwapEffectUpgradeEnable=1;"
            && WindowsGamingParsing.WithFlag(null, "VRROptimizeEnable", true) == "VRROptimizeEnable=1;" && WindowsGamingParsing.WithFlag("VRROptimizeEnable=1;", "vrroptimizeenable", null) == "",
            "windows gaming: one DirectX flag changes and every other pair is kept");
        bool Rejects(string text) { try { WindowsGamingParsing.ParseMouse(text); return false; } catch (FormatException) { return true; } }
        Check(WindowsGamingParsing.ParseMouse("6,10,1").SequenceEqual([6, 10, 1]) && WindowsGamingParsing.MouseText([0, 0, 0]) == "0,0,0" && Rejects("6,10") && Rejects("6,10,3") && Rejects("-1,0,0") && Rejects("6,10,x"),
            "windows gaming: mouse parameters are validated before use");
        Check(new bool?[] { true, false, null }.All(v => WindowsGamingParsing.ParseFlagState(WindowsGamingParsing.FlagState(v)) == v), "windows gaming: on/off/default states round-trip");

        var graphics = Desktop(Display(165, 1, 165));
        var windows = Windows() with { BackgroundRecording = true, WindowedOptimizations = false, Mouse = [6, 10, 1] };
        var findings = GamingHealth.Evaluate(graphics, windows, Global(), Now);
        var recording = findings.Single(f => f.FindingId == "background-recording");
        var windowed = findings.Single(f => f.FindingId == "windowed-optimizations");
        var mouse = findings.Single(f => f.FindingId == "mouse-acceleration");
        Check(recording.Severity == FindingSeverity.Warning && recording.Metadata["target"] == "background-recording" && GamingHealth.CanApply(recording)
            && windowed.Severity == FindingSeverity.Warning && windowed.Metadata["after"] == "on" && mouse.Severity == FindingSeverity.Informational && mouse.Metadata["mouse"] == "6,10,1",
            "gaming scan: background recording and windowed-game optimizations are opportunities; mouse acceleration is an observation");
        Check(!findings.Any(GamingHealth.BelongsInFixMyPc) && findings.All(f => !Navigation.UsesFaultLanguage(f.Title) && !Navigation.UsesFaultLanguage(f.Explanation)),
            "gaming scan: these preferences stay out of Fix My PC and aren't described as faults");
        Check(GamingHealth.Evaluate(graphics, windows with { AppCapture = false }, Global(), Now).Single(f => f.FindingId == "background-recording").Severity == FindingSeverity.Healthy,
            "gaming scan: background recording is off when Game Bar captures are off");
        var balanced = GamingProfiles.Propose(GamingGoal.Balanced, graphics, findings, null);
        Check(balanced.Single(c => c.Target == "background-recording") is { Kind: "Windows gaming setting", After: "off", Optional: true }
            && balanced.Single(c => c.Target == "windowed-optimizations") is { After: "on", Optional: false } && !balanced.Any(c => c.Target == "mouse-acceleration"),
            "profiles: Balanced offers the Windows opportunities and leaves mouse acceleration alone");
        Check(GamingProfiles.Propose(GamingGoal.Competitive, graphics, findings, null).Single(c => c.Target == "mouse-acceleration") is { After: "0,0,0", Optional: true },
            "profiles: Competitive offers to turn mouse acceleration off");
        Check(balanced.All(c => Guardrails.Allowed(c)) && PerformanceSettings.WindowsGamingTargets.SetEquals(["game-mode", "background-recording", "windowed-optimizations", "variable-refresh", "mouse-acceleration", "power-mode"]),
            "settings: only the six Windows gaming settings can be changed");
    }

    private static DiagnosticResult Finding(string module, string id, FindingSeverity severity, string recommendation) =>
        new(module, id, DiagnosticCategory.Performance, CollectionOutcome.Completed, severity, id, "Fixture explanation.", Now, Now, recommendation: recommendation,
            metadata: new Dictionary<string, string> { ["current"] = "4800 MT/s", ["recommended"] = "6000 MT/s", ["remedy"] = GamingHealth.RemedyHardware });

    private static void Tune()
    {
        var rtx = Gpu(GpuVendor.Nvidia, "NVIDIA GeForce RTX 4070", 12 * GraphicsFacts.GiB, 0, 1);
        var desktop = Desktop(Display(60, 1, 60, 144, 165), rtx);
        var untuned = Windows(gameMode: false, maxAc: 80) with { WindowedOptimizations = false, BackgroundRecording = true, Mouse = [6, 10, 1], HardwareScheduling = false };
        var findings = new[] { Finding("perf-memory", "memory-speed", FindingSeverity.Warning, "Enable XMP or EXPO in the BIOS."), Finding("perf-storage", "trim", FindingSeverity.Healthy, "") };
        var inputs = new TuneInputs(desktop, untuned, Defaults(), findings);
        ProposedChange? At(TunePlan plan, string kind, string target) => plan.Changes.SingleOrDefault(c => c.Kind == kind && c.Target == target);
        string? Nv(TunePlan plan, uint id) => At(plan, NvidiaPresets.ChangeKind, NvidiaSettings.Hex(id))?.After;
        bool Has(TunePlan plan, string id) => plan.Changes.Any(c => c.Id == id);
        const string Wg = "Windows gaming setting";

        var competitive = TunePlanner.Plan(TuneScenario.GamingPerformance, AdaptiveSync.Yes, inputs);
        Check(At(competitive, "Display mode", @"\\.\DISPLAY1")?.After == "2560x1440@165" && At(competitive, Wg, "mouse-acceleration") is { After: "0,0,0", Optional: false }
            && At(competitive, Wg, "game-mode")?.After == "on" && At(competitive, Wg, "windowed-optimizations")?.After == "on" && At(competitive, Wg, "variable-refresh")?.After == "on"
            && At(competitive, Wg, "background-recording") is { After: "off", Optional: false } && At(competitive, "Processor power", "381b4222-f694-41f0-9685-ff5bb260df2e|ac")?.After == "100",
            "tune: Gaming + Performance sets the highest refresh rate, Game Mode, windowed optimizations, VRR, no background recording, full processor and no mouse acceleration");
        Check(Nv(competitive, NvidiaSettings.VerticalSyncId) == NvidiaSettings.Hex(NvidiaSettings.VsyncOn) && Nv(competitive, NvidiaSettings.FrameRateLimitId) == "0x000000A2"
            && Nv(competitive, NvidiaSettings.PreRenderedFramesId) == "0x00000001" && Nv(competitive, NvidiaSettings.PreferredRefreshRateId) == "0x00000001",
            "tune: with G-SYNC, V-Sync is on in the driver and the cap is 3 below the new 165 Hz, with Low Latency Mode and the highest refresh rate");
        Check(Nv(competitive, NvidiaSettings.PowerManagementId) is null && Has(competitive, "tune-per-game-clocks"), "tune: full GPU clocks are suggested per game, never globally");
        Check(Has(competitive, "tune-hags") && Has(competitive, "tune-adaptive-sync") && Has(competitive, "tune-in-game") && Has(competitive, "memory-speed")
            && competitive.Changes.Single(c => c.Id == "memory-speed").Manual == "Enable XMP or EXPO in the BIOS." && competitive.AlreadyGood.Any(g => g.Area == TuneArea.Storage),
            "tune: GPU scheduling for RTX 40, the adaptive-sync switch, in-game settings and memory speed are steps for you; healthy checks are listed");
        Check(competitive.Changes.Where(c => c.HankiApplies).All(c => Guardrails.Allowed(c) && Navigation.IsPerformanceChange(c.Kind!))
            && competitive.Changes.Where(c => c.HankiApplies).GroupBy(c => (c.Kind, c.Target)).All(g => g.Count() == 1), "tune: every change is a Recovery-backed Performance change, one per setting");
        Check(competitive.Changes.All(c => !Navigation.UsesFaultLanguage(c.Why) && !Navigation.UsesFaultLanguage(c.Setting)), "tune: explanations aren't written as faults");

        var noSync = TunePlanner.Plan(TuneScenario.GamingPerformance, AdaptiveSync.No, inputs);
        Check(Nv(noSync, NvidiaSettings.VerticalSyncId) == NvidiaSettings.Hex(NvidiaSettings.VsyncOff) && Nv(noSync, NvidiaSettings.FrameRateLimitId) is null && At(noSync, Wg, "variable-refresh") is null,
            "tune: without adaptive sync, competitive play turns V-Sync off and adds no cap");
        var unsure = TunePlanner.Plan(TuneScenario.GamingPerformance, AdaptiveSync.NotSure, inputs);
        Check(Nv(unsure, NvidiaSettings.VerticalSyncId) is null && Nv(unsure, NvidiaSettings.FrameRateLimitId) is null && Has(unsure, "tune-sync-unknown"),
            "tune: when you're not sure about adaptive sync, V-Sync and caps are left alone and you're told how to check");
        var quality = TunePlanner.Plan(TuneScenario.GamingQuality, AdaptiveSync.No, inputs);
        Check(Nv(quality, NvidiaSettings.VerticalSyncId) == NvidiaSettings.Hex(NvidiaSettings.VsyncOn) && Nv(quality, NvidiaSettings.TextureFilteringId) == NvidiaSettings.Hex(NvidiaSettings.TextureHighQuality)
            && At(quality, Wg, "background-recording")!.Optional && Nv(quality, NvidiaSettings.PreRenderedFramesId) is null, "tune: Gaming + Quality keeps V-Sync on and sharp textures");
        var creative = TunePlanner.Plan(TuneScenario.Creative, AdaptiveSync.NotSure, inputs);
        Check(!creative.Changes.Any(c => c.Kind == NvidiaPresets.ChangeKind) && At(creative, Wg, "mouse-acceleration") is null && At(creative, Wg, "game-mode") is null
            && At(creative, "Processor power", "381b4222-f694-41f0-9685-ff5bb260df2e|ac") is not null && Has(creative, "memory-speed") && !Has(creative, "tune-in-game"),
            "tune: Creative work leaves game settings alone but gives the processor full speed");

        var laptop = new GraphicsInventory([rtx], [Display(165, 1, 60, 165)], true, false, true, false, []);
        var lowPower = TunePlanner.Plan(TuneScenario.LowPower, AdaptiveSync.NotSure,
            new TuneInputs(laptop, untuned, With(Defaults(), NvidiaSettings.PowerManagementId, "0x00000001", 1), findings));
        Check(At(lowPower, "Display mode", @"\\.\DISPLAY1") is { After: "2560x1440@60", Optional: true } && Nv(lowPower, NvidiaSettings.FrameRateLimitId) == "0x0000003C"
            && Nv(lowPower, NvidiaSettings.PowerManagementId) == "default" && At(lowPower, "Processor power", "381b4222-f694-41f0-9685-ff5bb260df2e|ac") is null && !Has(lowPower, "memory-speed"),
            "tune: Low power on a laptop offers 60 Hz, caps at 60 FPS, undoes global maximum GPU clocks and leaves the processor and memory alone");

        var tuned = Windows() with { WindowedOptimizations = true, Mouse = [0, 0, 0] };
        var done = TunePlanner.Plan(TuneScenario.GamingPerformance, AdaptiveSync.No, new TuneInputs(Desktop(Display(165, 1, 165), rtx), tuned,
            With(With(With(Defaults(), NvidiaSettings.PreRenderedFramesId, "0x00000001", 1), NvidiaSettings.VerticalSyncId, "0x08416747", NvidiaSettings.VsyncOff), NvidiaSettings.PreferredRefreshRateId, "0x00000001", 1), []));
        Check(done.Changes.All(c => !c.HankiApplies || c.Optional) && done.AlreadyGood.Count >= 6, "tune: a PC already set up gets no required changes, and sees what was checked");

        var radeon = Desktop(Display(165, 1, 165), Gpu(GpuVendor.Amd, "AMD Radeon RX 7800 XT", 16 * GraphicsFacts.GiB, 0, 1));
        var amd = TunePlanner.Plan(TuneScenario.GamingPerformance, AdaptiveSync.Yes, new TuneInputs(radeon, tuned, null, []));
        Check(amd.Changes.Single(c => c.Id == "tune-amd").Manual!.Contains("Anti-Lag on") && amd.Changes.Single(c => c.Id == "tune-amd").Manual!.Contains("162 FPS") && !amd.Changes.Any(c => c.Kind == NvidiaPresets.ChangeKind),
            "tune: Radeon owners get the matching AMD Software settings as a step");
        Check(Enum.GetValues<TuneScenario>().All(v => TunePlanner.Name(v).Length > 0 && !Navigation.UsesFaultLanguage(TunePlanner.Describe(v))), "tune: every choice has a name and a plain description");
    }

    // Steps Tune my PC used to leave to you that it now applies or reads itself.
    private static void TuneSteps()
    {
        const string Wg = "Windows gaming setting";
        var rtx = Gpu(GpuVendor.Nvidia, "NVIDIA GeForce RTX 4070", 12 * GraphicsFacts.GiB, 0, 1);
        var desktop = Desktop(Display(165, 1, 165), rtx);
        var tuned = Windows() with { WindowedOptimizations = true, Mouse = [0, 0, 0] };
        TunePlan Plan(TuneScenario scenario, WindowsGamingSettings w, AdaptiveSync sync = AdaptiveSync.No, AdaptiveSyncStatus? status = null, IReadOnlyList<GameContext>? games = null) =>
            TunePlanner.Plan(scenario, sync, new TuneInputs(desktop, w, Defaults(), [], null, status, games));
        ProposedChange? Power(TunePlan plan) => plan.Changes.SingleOrDefault(c => c.Id == "tune-power-mode");

        Check(new[] { "Best power efficiency", "Balanced", "Best performance" }.All(n => WindowsGamingParsing.PowerModeOverlay(n) is { } g && WindowsGamingParsing.PowerModeName(g) == n)
            && WindowsGamingParsing.PowerModeOverlay("Turbo") is null, "power mode: Recovery stores the name Windows' power mode setting uses");
        Check(Power(Plan(TuneScenario.GamingPerformance, tuned)) is { Kind: Wg, Target: "power-mode", Current: "Balanced", After: "Best performance", Optional: true }
            && Power(Plan(TuneScenario.LowPower, tuned)) is { Kind: Wg, After: "Best power efficiency", Optional: false }
            && Power(Plan(TuneScenario.GamingQuality, tuned with { PowerMode = "Best power efficiency" })) is { Kind: Wg, After: "Balanced", Optional: false }
            && Power(Plan(TuneScenario.GamingQuality, tuned)) is null, "power mode: Hanki changes it on the Balanced plan, optional for Best performance");
        var highPlan = tuned with { PowerPlan = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), PowerPlanName = "High performance" };
        Check(Power(Plan(TuneScenario.GamingPerformance, highPlan)) is { Kind: null, Manual: not null } && Power(Plan(TuneScenario.GamingPerformance, tuned with { PowerMode = null })) is null,
            "power mode: with another power plan it stays a step, and an unreadable mode isn't changed");

        Check(AdaptiveSyncStatus.FromNvidiaFlags(1) == new AdaptiveSyncStatus(true, true) && AdaptiveSyncStatus.FromNvidiaFlags(2) == new AdaptiveSyncStatus(false, true)
            && AdaptiveSyncStatus.FromNvidiaFlags(0b11100) == new AdaptiveSyncStatus(false, false), "g-sync: NVIDIA's flags for enabled and possible");
        Check(TunePlanner.Resolve(AdaptiveSync.NotSure, new(true, true)) == AdaptiveSync.Yes && TunePlanner.Resolve(AdaptiveSync.No, new(true, true)) == AdaptiveSync.No
            && TunePlanner.Resolve(AdaptiveSync.NotSure, new(false, true)) == AdaptiveSync.NotSure && TunePlanner.Resolve(AdaptiveSync.NotSure, null) == AdaptiveSync.NotSure,
            "g-sync: \"not sure\" becomes yes when the driver says it's on; your answer is kept");
        var on = Plan(TuneScenario.GamingPerformance, tuned, AdaptiveSync.Yes, new(true, true));
        var off = Plan(TuneScenario.GamingPerformance, tuned, AdaptiveSync.Yes, new(false, true));
        var unknown = Plan(TuneScenario.GamingPerformance, tuned, AdaptiveSync.Yes);
        Check(!on.Changes.Any(c => c.Id == "tune-adaptive-sync") && on.AlreadyGood.Any(g => g.Setting == "G-SYNC" && g.Current == "On")
            && off.Changes.Single(c => c.Id == "tune-adaptive-sync") is { Current: "Off", Optional: false, Manual: not null }
            && unknown.Changes.Single(c => c.Id == "tune-adaptive-sync") is { Current: "Not readable", Optional: true },
            "g-sync: no step when it's on; a step when it's off, since NVIDIA doesn't let apps switch it on");

        NvidiaProfileView Own(string exe, uint power) => new("Hanki: " + exe, exe, false, [new(NvidiaSettings.Get(NvidiaSettings.PowerManagementId), power, NvidiaSettingSource.ThisProfile, false)]);
        GameContext[] games = [new("Counter-Strike 2", "C:/Games/cs2.exe", null, Global(), null), new("Apex Legends", "C:/Games/r5apex.exe", Own("r5apex.exe", NvidiaSettings.PowerPreferMaximum), Global(), null),
            new("Fortnite", "C:/Games/FortniteClient.exe", Own("FortniteClient.exe", NvidiaSettings.PowerNormal), Global(), null)];
        var clocks = Plan(TuneScenario.GamingPerformance, tuned, games: games);
        var perGame = clocks.Items.Where(i => i.Change.Id.StartsWith("tune-clocks:", StringComparison.Ordinal)).ToArray();
        Check(perGame.Length == 2 && perGame.All(i => i.Area == TuneArea.Games && i.Change is { Kind: "NVIDIA setting", Optional: true } && i.Change.After == NvidiaSettings.Hex(NvidiaSettings.PowerPreferMaximum) && Guardrails.Allowed(i.Change))
            && perGame.Any(i => i.Change.Target == $"{Path.GetFileName("C:/Games/cs2.exe")}|{NvidiaSettings.Hex(NvidiaSettings.PowerManagementId)}" && i.Change.Setting == "Full GPU clocks: Counter-Strike 2")
            && clocks.AlreadyGood.Any(g => g.Setting == "Full GPU clocks" && g.Current == "On for 1 game") && !clocks.Changes.Any(c => c.Id == "tune-per-game-clocks"),
            "full clocks: offered for each of your games that doesn't have them, instead of a step");
        Check(Plan(TuneScenario.GamingPerformance, tuned, games: []).Changes.Single(c => c.Id == "tune-per-game-clocks").Current == "No games listed"
            && !Plan(TuneScenario.GamingQuality, tuned, games: games).Items.Any(i => i.Change.Id.StartsWith("tune-clocks:", StringComparison.Ordinal))
            && Plan(TuneScenario.GamingPerformance, tuned, games: Enumerable.Range(0, 20).Select(i => new GameContext($"Game {i}", $"C:/Games/g{i}.exe", null, Global(), null)).ToArray())
                .Items.Count(i => i.Change.Id.StartsWith("tune-clocks:", StringComparison.Ordinal)) == TunePlanner.GameLimit,
            "full clocks: a step without games, only for Gaming + Performance, and at most 12 games at once");
        var byGoal = GamingProfiles.Propose(GamingGoal.Competitive, desktop, [], games[0]);
        Check(byGoal.Single(c => c.Id == $"nvidia-{NvidiaSettings.PowerManagementId:X8}") is { Kind: "NVIDIA setting", Optional: false } && GamingProfiles.Propose(GamingGoal.Competitive, desktop, [], games[1]).All(c => c.Id != $"nvidia-{NvidiaSettings.PowerManagementId:X8}"),
            "profiles: Optimize this game still sets full clocks the same way");
    }

    private static void Radeon()
    {
        Check(AmdSettings.State(AmdSettingKind.AntiLag, true, null) == "on" && AmdSettings.State(AmdSettingKind.Chill, true, 30, 60) == "on:30-60"
            && AmdSettings.State(AmdSettingKind.FrameRateTargetControl, false, 144) == "off:144" && AmdSettings.State(AmdSettingKind.WaitForVerticalRefresh, true, 2) == "mode:2",
            "amd: states keep the value even when a feature is off");
        Check(AmdSettings.Parse(AmdSettingKind.Chill, "off:40-72") == (false, 40, 72) && AmdSettings.Parse(AmdSettingKind.WaitForVerticalRefresh, "mode:3") == (true, 3, null)
            && AmdSettings.Parse(AmdSettingKind.AntiLag, "on:5") is null && AmdSettings.Parse(AmdSettingKind.WaitForVerticalRefresh, "mode:4") is null
            && AmdSettings.Parse(AmdSettingKind.Chill, "on:60-30") is null && AmdSettings.Parse(AmdSettingKind.FrameRateTargetControl, "on:-1") is null
            && AmdSettings.Parse(AmdSettingKind.Boost, "maybe") is null && AmdSettings.Parse(AmdSettingKind.ImageSharpening, "on:1e3") is null,
            "amd: only well-formed states are accepted");
        Check(AmdSettings.Describe(AmdSettingKind.Chill, "on:30-60") == "On, 30–60 FPS" && AmdSettings.Describe(AmdSettingKind.FrameRateTargetControl, "off:144") == "Off"
            && AmdSettings.Describe(AmdSettingKind.WaitForVerticalRefresh, "mode:0") == "Always off" && AmdSettings.Describe(AmdSettingKind.AnisotropicFiltering, "on:16") == "On, 16x",
            "amd: settings are described the way AMD Software names them");
        Check(AmdSettings.ParseTarget(AmdSettings.Target(-5, AmdSettingKind.Chill)) == (-5, AmdSettingKind.Chill)
            && new[] { "x|Chill", "1|Nope", "1|7", "1|Chill|2", "1" }.All(t => Throws(() => AmdSettings.ParseTarget(t))), "amd: Recovery targets name the GPU and the setting, nothing else");

        var gpu = new AmdGpuSettings(7, "AMD Radeon RX 7800 XT", [
            new(AmdSettingKind.AntiLag, false, null), new(AmdSettingKind.Chill, true, 30, 60, 30, 300), new(AmdSettingKind.Boost, true, 50, null, 50, 85),
            new(AmdSettingKind.FrameRateTargetControl, false, 144, null, 30, 300), new(AmdSettingKind.WaitForVerticalRefresh, true, AmdSettings.WfvrOffUnlessApp),
            new(AmdSettingKind.AnisotropicFiltering, false, 16, null, 2, 16)]);
        var antiLag = AmdSettings.Change(gpu, AmdSettingKind.AntiLag, true, null, "why");
        Check(antiLag is { Kind: AmdSettings.ChangeKind, Target: "7|AntiLag", After: "on", Current: "Off", Recommended: "On", Source: ChangeSource.Amd } && Guardrails.Allowed(antiLag)
            && PerformanceSettings.Handles(AmdSettings.ChangeKind), "amd: a change is a Recovery-backed Performance change");
        Check(AmdSettings.Change(gpu, AmdSettingKind.AntiLag, false, null, "why") is null && AmdSettings.Change(gpu, AmdSettingKind.EnhancedSync, true, null, "why") is null
            && AmdSettings.Change(gpu, AmdSettingKind.FrameRateTargetControl, true, 400, "why") is null && AmdSettings.Change(gpu, AmdSettingKind.Chill, true, 10, "why", value2: 60) is null,
            "amd: nothing is proposed when it's already set, unsupported, or outside the driver's range");
        Check(AmdSettings.Change(gpu, AmdSettingKind.Chill, false, null, "why")?.After == "off:30-60" && AmdSettings.Change(gpu, AmdSettingKind.FrameRateTargetControl, true, 162, "why")?.After == "on:162"
            && AmdSettings.ChangeTo(gpu, AmdSettingKind.Boost, "off:50", "why")?.After == "off:50" && AmdSettings.ChangeTo(gpu, AmdSettingKind.Boost, "sideways", "why") is null,
            "amd: turning a feature off keeps its value, so an undo restores both");

        var frtc = AmdSettings.Choices(gpu.Settings.Single(s => s.Kind == AmdSettingKind.FrameRateTargetControl), 165);
        Check(frtc[0] == ("Current: Off", "off:144") && frtc.Select(c => c.State).SequenceEqual(["off:144", "on:60", "on:162", "on:165"]),
            "amd: editor choices start with the current value and include a cap just below the refresh rate");
        Check(gpu.Settings.All(s => AmdSettings.Choices(s, 165).All(c => AmdSettings.Parse(s.Kind, c.State) is not null) && AmdSettings.Choices(s, 165).Select(c => c.State).Distinct().Count() == AmdSettings.Choices(s, 165).Count)
            && AmdSettings.Choices(gpu.Settings.Single(s => s.Kind == AmdSettingKind.WaitForVerticalRefresh), 60).Count == 4, "amd: every choice is a valid state, listed once");

        string? After(IReadOnlyList<ProposedChange> changes, AmdSettingKind kind) => changes.SingleOrDefault(c => c.Target == AmdSettings.Target(7, kind))?.After;
        var competitive = AmdSettings.ForScenario(TuneScenario.GamingPerformance, AdaptiveSync.Yes, gpu, 165);
        Check(After(competitive, AmdSettingKind.AntiLag) == "on" && After(competitive, AmdSettingKind.Chill) == "off:30-60" && After(competitive, AmdSettingKind.Boost) == "off:50"
            && After(competitive, AmdSettingKind.FrameRateTargetControl) == "on:162", "amd: Gaming + Performance turns on Anti-Lag, turns off Chill and Boost, and caps 3 below 165 Hz with FreeSync");
        Check(After(AmdSettings.ForScenario(TuneScenario.GamingPerformance, AdaptiveSync.No, gpu, 165), AmdSettingKind.FrameRateTargetControl) is null
            && After(AmdSettings.ForScenario(TuneScenario.GamingQuality, AdaptiveSync.No, gpu, 165), AmdSettingKind.WaitForVerticalRefresh) == "mode:2"
            && After(AmdSettings.ForScenario(TuneScenario.LowPower, AdaptiveSync.NotSure, gpu, 144), AmdSettingKind.Chill) is null
            && After(AmdSettings.ForScenario(TuneScenario.LowPower, AdaptiveSync.NotSure, gpu with { Settings = [new(AmdSettingKind.Chill, false, 40, 144, 30, 300)] }, 144), AmdSettingKind.Chill) == "on:30-60"
            && AmdSettings.ForScenario(TuneScenario.Creative, AdaptiveSync.Yes, gpu, 165).Count == 0,
            "amd: no cap without FreeSync, vertical sync for quality, Chill for low power and nothing for creative work");

        var radeon = Desktop(Display(165, 1, 165), Gpu(GpuVendor.Amd, "AMD Radeon RX 7800 XT", 16 * GraphicsFacts.GiB, 0, 1));
        var plan = TunePlanner.Plan(TuneScenario.GamingPerformance, AdaptiveSync.Yes, new TuneInputs(radeon, Windows() with { WindowedOptimizations = true, Mouse = [0, 0, 0] }, null, [], gpu));
        var applied = plan.Changes.Where(c => c.Kind == AmdSettings.ChangeKind).ToArray();
        Check(applied.Length == 4 && applied.All(c => c.Id.StartsWith("tune-amd-", StringComparison.Ordinal) && Guardrails.Allowed(c) && !Navigation.UsesFaultLanguage(c.Why))
            && !plan.Changes.Any(c => c.Id == "tune-amd") && plan.Items.Where(i => i.Change.Kind == AmdSettings.ChangeKind).All(i => i.Area == TuneArea.GraphicsDriver),
            "tune: when Radeon settings can be read, Hanki applies them instead of listing a step");
    }

    private static void Background()
    {
        Check(GamingBackground.RtssLimit("[OSD]\r\nLimit=5\r\n[Framerate]\r\nLimit=5994\r\nLimitDenominator=100\r\n") is { } fractional && Math.Abs(fractional - 59.94) < 0.001
            && GamingBackground.RtssLimit("[Framerate]\nLimit=141\n") == 141 && GamingBackground.RtssLimit("[Framerate]\nLimit=0\n") == 0 && GamingBackground.RtssLimit("[OSD]\nLimit=60\n") is null,
            "background: RivaTuner's frame limit is read from its [Framerate] section, including fractional limits");
        var snapshot = new BackgroundSnapshot([new("RTSS", 1, 0.5, 20), new("Discord", 6, 1, 600), new("obs64", 1, 6, 400), new("chrome", 24, 23, 3100), new("svchost", 80, 30, 900), new("cs2", 1, 60, 5000)], 141);
        var findings = GamingBackground.Evaluate(snapshot, Now, ["cs2"]);
        Check(findings.Single(f => f.FindingId == "limiter:rtss") is { Severity: FindingSeverity.Warning } rtss && rtss.Title.Contains("141") && findings.Single(f => f.FindingId == "overlay:discord").Severity == FindingSeverity.Informational
            && findings.Single(f => f.FindingId == "recorder:obs64").Severity == FindingSeverity.Warning, "background: RivaTuner's cap, overlays and recorders are recognised");
        Check(findings.Any(f => f.FindingId == "busy:chrome") && !findings.Any(f => f.FindingId == "busy:cs2") && !findings.Any(f => f.FindingId == "busy:svchost") && !findings.Any(f => f.FindingId == "busy:obs64")
            && GamingBackground.Evaluate(snapshot, Now).Any(f => f.FindingId == "busy:cs2"),
            "background: busy programs are reported; Windows' own processes, your games and known programs aren't reported as busy");
        Check(findings.All(f => f.Metadata["source"] == "Background" && !Navigation.UsesFaultLanguage(f.Explanation) && !GamingHealth.BelongsInFixMyPc(f) && !GamingHealth.CanApply(f)),
            "background: observations only; nothing is closed and nothing reaches Fix My PC");
        Check(GamingBackground.Evaluate(new BackgroundSnapshot([new("svchost", 50, 2, 800)], null), Now).Single().Severity == FindingSeverity.Healthy, "background: a quiet PC says so");
        var game = new GameContext("CS2", @"C:\Games\cs2.exe", null, Global(fps: 144), null);
        Check(GamingProfiles.Conflicts(game, 165, 141).Any(n => n.Contains("RivaTuner also caps")) && GamingProfiles.Conflicts(game with { NvidiaGlobal = null }, 165, 60).Single().Contains("RivaTuner caps this game at 60"),
            "limiters: RivaTuner's cap is part of the per-game conflicts");
        var plan = TunePlanner.Plan(TuneScenario.GamingPerformance, AdaptiveSync.No, new TuneInputs(Desktop(Display(165, 1, 165)), Windows(), null, findings));
        Check(plan.Items.Any(i => i.Area == TuneArea.Background && i.Change.Id == "busy:chrome" && i.Change.Optional && !i.Change.HankiApplies)
            && !TunePlanner.Plan(TuneScenario.Creative, AdaptiveSync.No, new TuneInputs(Desktop(Display(165, 1, 165)), Windows(), null, findings)).Items.Any(i => i.Area == TuneArea.Background),
            "tune: busy programs and a second limiter become optional steps when gaming");
    }

    private static void Launch()
    {
        var t = new DateTime(2026, 9, 24, 12, 0, 0);
        Check(LaunchMeasure.PickProcess([(10, "cs2", t, false), (11, "CS2", t.AddSeconds(5), true), (12, "cs2", t.AddSeconds(1), true), (13, "steam", t.AddSeconds(9), true)], "cs2") == 11
            && LaunchMeasure.PickProcess([(10, "cs2", t, false)], "cs2") is null, "launch: the newest window of the game's executable is measured, not its launcher");
        PerformanceMeasurement Run(DateTimeOffset start, double fps) => new(start, start.AddSeconds(120), new Dictionary<string, double> { [PerformanceMetrics.FpsAverage] = fps, [PerformanceMetrics.FpsLow1] = fps * 0.7 }, "Performance Lab monitor");
        var first = LaunchMeasure.Session("CS2", Run(Now, 200), null, [], "GPU-limited");
        Check(first is { Outcome: SessionOutcome.Measured, After: null } && first.Name == LaunchMeasure.SessionName("CS2"), "launch: the first run of a game is saved as a measurement");
        var changes = new[] {
            new SettingChange(Guid.NewGuid(), Now.AddMinutes(10), "NVIDIA global setting", "0x007BA09E", "default", "0x00000001", "Applied"),
            new SettingChange(Guid.NewGuid(), Now.AddMinutes(11), "IPv4 DNS", "x", "", "1.1.1.1", "Applied"),
            new SettingChange(Guid.NewGuid(), Now.AddMinutes(-30), "Display mode", @"\\.\DISPLAY1", "a", "b", "Applied") };
        var second = LaunchMeasure.Session("CS2", Run(Now.AddMinutes(20), 220), first, changes, "GPU-limited");
        Check(second.Outcome == SessionOutcome.Improved && second.Baseline == first.Baseline && second.ChangesTested.SequenceEqual([changes[0].Id]),
            "launch: the next run is compared with the last one, with only the Performance changes made in between");
        Check(LaunchMeasure.Previous([first, second, first with { Name = LaunchMeasure.SessionName("Other") }], "CS2") == (second.Created >= first.Created ? second : first), "launch: the most recent run of the same game is the comparison");
    }

    private static void Library()
    {
        const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}";
        Check(GameLibrary.SteamLibraries(vdf).SequenceEqual([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"]), "games: Steam library folders are read from libraryfolders.vdf");
        const string acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"name\"\t\t\"Counter-Strike 2\"\n\t\"installdir\"\t\t\"Counter-Strike Global Offensive\"\n}";
        Check(GameLibrary.AcfValue(acf, "name") == "Counter-Strike 2" && GameLibrary.AcfValue(acf, "installdir") == "Counter-Strike Global Offensive" && GameLibrary.AcfValue(acf, "missing") is null, "games: Steam app manifests");
        Check(GameLibrary.LooksLikeGame("Wow.exe") && !GameLibrary.LooksLikeGame("World of Warcraft Launcher.exe") && !GameLibrary.LooksLikeGame("unins000.exe")
            && !GameLibrary.LooksLikeGame("UnityCrashHandler64.exe") && !GameLibrary.LooksLikeGame("EasyAntiCheat_EOS_Setup.exe") && GameLibrary.LooksLikeGame("cs2.exe"), "games: installers, launchers and crash reporters aren't games");
        var candidates = new[] { (@"C:\G\engine-tool.exe", 900L), (@"C:\G\Game.exe", 500L), (@"C:\G\unins000.exe", 2000L) };
        Check(GameLibrary.MainExecutable(candidates) == @"C:\G\engine-tool.exe" && GameLibrary.MainExecutable(candidates, exe => exe == "Game.exe") == @"C:\G\Game.exe",
            "games: the executable the driver knows wins, otherwise the largest");
        var existing = new[] { new GameEntry(Guid.NewGuid(), "WoW", @"C:\WoW\Wow.exe", "Added by you", GamingGoal.Competitive), new GameEntry(Guid.NewGuid(), "Old", @"C:\Old\old.exe", "Steam", GamingGoal.Balanced, Hidden: true) };
        var merged = GameLibrary.Merge(existing, [new(Guid.NewGuid(), "World of Warcraft", @"c:\wow\wow.exe", "Blizzard Entertainment", GamingGoal.Balanced), new(Guid.NewGuid(), "Old", @"C:\Old\old.exe", "Steam", GamingGoal.Balanced), new(Guid.NewGuid(), "New", @"C:\New\new.exe", "Epic Games", GamingGoal.Balanced)]);
        Check(merged.Count == 3 && merged.Single(g => g.Name == "WoW").Goal == GamingGoal.Competitive && merged.Single(g => g.Name == "Old").Hidden, "games: rescans don't duplicate, keep your goal, and don't bring back removed games");
    }

    /// <summary>Runs the real read-only probes: they must not throw on any PC, with or without NVIDIA or displays (for example a CI runner).</summary>
    private static void Live()
    {
        var graphics = GraphicsProbe.Collect();
        Check(graphics.Adapters.All(a => a.Name.Length > 0) && graphics.Displays.All(d => d.Current.Width > 0), "live: graphics hardware is read without errors");
        var windows = WindowsGamingProbe.Collect();
        Check(windows.ProcessorMaximumAc is null or (>= 0 and <= 100), "live: Windows gaming settings are read without errors");
        Check(GamingHealth.Evaluate(graphics, windows, null, DateTimeOffset.UtcNow).Count > 0, "live: the gaming scan runs on this PC");
        bool refused = false;
        try { PerformanceSettings.ParseNvidiaTarget(@"..\evil.exe|0x1057EB71"); } catch (IOException) { refused = true; }
        Check(refused && PerformanceSettings.ParseNvidiaTarget("Wow.exe|0x1057EB71") == ("Wow.exe", NvidiaSettings.PowerManagementId), "settings: NVIDIA targets must be a plain .exe name and a known setting");
        bool badMode = false;
        try { PerformanceSettings.ParseMode("1920x1080@9999"); } catch (IOException) { badMode = true; }
        Check(badMode && PerformanceSettings.ParseMode("1920x1080@165") == (1920, 1080, 165), "settings: display modes are validated before use");
    }
}
