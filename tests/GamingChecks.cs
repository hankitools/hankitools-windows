using IgezziGuard;

// HANKI-GPU-100 and HANKI-GAME-200: hardware facts, the Gaming Health Scan, goal profiles and the game library.
internal static class GamingChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    internal static void Run() { Facts(); Health(); Profiles(); NvidiaGlobal(); WindowsGaming(); Tune(); Library(); Live(); }

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
        Check(balanced.All(c => Guardrails.Allowed(c)) && PerformanceSettings.WindowsGamingTargets.SetEquals(["game-mode", "background-recording", "windowed-optimizations", "variable-refresh", "mouse-acceleration"]),
            "settings: only the five Windows gaming settings can be changed");
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
