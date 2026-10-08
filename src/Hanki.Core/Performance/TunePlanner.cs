using System.Text.RegularExpressions;
namespace IgezziGuard;

public enum TuneScenario { GamingPerformance, GamingQuality, Creative, LowPower }
/// <summary>Whether the display has G-SYNC or FreeSync. Windows doesn't report it reliably, so Tune my PC asks.</summary>
public enum AdaptiveSync { NotSure, Yes, No }
/// <summary>G-SYNC as NVIDIA's driver reports it for a display: switched on, and whether the display supports it.</summary>
public sealed record AdaptiveSyncStatus(bool On, bool Supported)
{
    /// <summary>NVAPI's NV_GET_VRR_INFO flags: bit 0 enabled, bit 1 possible.</summary>
    public static AdaptiveSyncStatus FromNvidiaFlags(uint flags) => new((flags & 1) != 0, (flags & 3) != 0);
}
public enum TuneArea { Display, Windows, Mouse, GraphicsDriver, Games, Background, Processor, Memory, Storage, Network, Hardware, Streaming }
/// <summary>A game's latest Launch &amp; measure run: what limited it, and its frame rate.</summary>
public sealed record MeasuredGame(string Game, DateTimeOffset At, Limiter Limiter, string Diagnosis, double? AverageFps, double? Low1Fps);

/// <summary>
/// What the Tune my PC scan read. Nvidia is null without an NVIDIA driver; Findings are the CPU, memory and storage
/// analyzers' results, the background check's (overlays, recorders, limiters, busy programs) and the gaming check's
/// hardware findings (PCIe lanes, Resizable BAR, a monitor on the motherboard's output).
/// </summary>
/// <param name="Amd">Radeon settings from AMD's driver interface, when it could be read.</param>
/// <param name="Sync">G-SYNC on the main display as NVIDIA's driver reports it, when it could be read.</param>
/// <param name="Games">Your games (Gaming → Games) with their NVIDIA profiles, for per-game changes.</param>
/// <param name="Links">Connected network adapters and their negotiated speed, when Windows reported them.</param>
/// <param name="Now">When the scan ran, for the graphics driver's age; without it, the age isn't judged.</param>
/// <param name="Cpu">The processor, for the AMD 3D V-Cache and Intel APO helpers.</param>
/// <param name="VCacheOptimizer">AMD's 3D V-Cache Performance Optimizer driver is installed (chipset driver).</param>
/// <param name="IntelTuning">Intel Dynamic Tuning Technology or its Innovation Platform Framework is installed; APO runs inside it.</param>
/// <param name="Obs">OBS Studio's encoders per profile; empty or null without OBS.</param>
/// <param name="Measured">Your games' latest Launch &amp; measure runs, for per-game advice.</param>
public sealed record TuneInputs(GraphicsInventory Graphics, WindowsGamingSettings Windows, IReadOnlyList<NvidiaGlobalSetting>? Nvidia, IReadOnlyList<DiagnosticResult> Findings,
    AmdGpuSettings? Amd = null, AdaptiveSyncStatus? Sync = null, IReadOnlyList<GameContext>? Games = null,
    IReadOnlyList<LinkAdapter>? Links = null, DateTimeOffset? Now = null,
    CpuFacts? Cpu = null, bool? VCacheOptimizer = null, bool? IntelTuning = null, IReadOnlyList<ObsEncoder>? Obs = null, IReadOnlyList<MeasuredGame>? Measured = null);
/// <summary>One line of the plan: a change Hanki makes (Kind set) or a step for you (Manual set).</summary>
public sealed record TuneItem(TuneArea Area, ProposedChange Change);
/// <summary>A setting the scan checked that already suits the choice, listed so you can see what was looked at.</summary>
public sealed record TuneCheck(TuneArea Area, string Setting, string Current);
public sealed record TunePlan(TuneScenario Scenario, AdaptiveSync Sync, IReadOnlyList<TuneItem> Items, IReadOnlyList<TuneCheck> AlreadyGood)
{
    public IReadOnlyList<ProposedChange> Changes => Items.Select(i => i.Change).ToArray();
    public int HankiChanges => Items.Count(i => i.Change.HankiApplies);
    public int Steps => Items.Count(i => !i.Change.HankiApplies);
}

/// <summary>
/// Tune my PC (HANKI-PERF-320): turns what you want today into a plan across display, Windows, mouse, graphics
/// driver, processor, memory, storage, network and hardware. The recommendations and their sources are in docs/TUNING.md. Only
/// settings Hanki can read back are changed, each through the review dialog and Recovery; hardware and BIOS steps,
/// in-game settings and switches Hanki can't change are listed as steps for you. Nothing is changed by building a plan.
/// </summary>
public static partial class TunePlanner
{
    public static string Name(TuneScenario scenario) => scenario switch {
        TuneScenario.GamingPerformance => "Gaming + Performance", TuneScenario.GamingQuality => "Gaming + Quality",
        TuneScenario.Creative => "Creative work", _ => "Low power"
    };
    public static string Describe(TuneScenario scenario) => scenario switch {
        TuneScenario.GamingPerformance => "Lowest input lag and the most frames, for competitive games.",
        TuneScenario.GamingQuality => "Smooth, tear-free and sharp, for single-player games.",
        TuneScenario.Creative => "Full processor speed and true colors, for editing, rendering and streaming.",
        _ => "Cooler, quieter and longer on battery: frame caps and energy saving."
    };
    public static bool IsGaming(TuneScenario scenario) => scenario is TuneScenario.GamingPerformance or TuneScenario.GamingQuality;
    /// <summary>"Not sure" becomes Yes when NVIDIA's driver says G-SYNC is on; an answer you gave is kept.</summary>
    public static AdaptiveSync Resolve(AdaptiveSync answer, AdaptiveSyncStatus? detected) => answer == AdaptiveSync.NotSure && detected is { On: true } ? AdaptiveSync.Yes : answer;
    /// <summary>At most this many games get per-game changes in one plan; the rest through Gaming → Games.</summary>
    public const int GameLimit = 12;
    [GeneratedRegex(@"RTX\s*(40|50)\d{2}", RegexOptions.IgnoreCase)] private static partial Regex FrameGenerationGpu();
    /// <summary>Ryzen 9 X3D with two core groups, only one with 3D V-Cache (7900X3D, 7950X3D, 9900X3D, 9950X3D, 7945HX3D…).</summary>
    [GeneratedRegex(@"Ryzen 9 \d{4}(X|HX)3D(?!\d)", RegexOptions.IgnoreCase)] public static partial Regex DualCacheRyzen();
    /// <summary>Processors Intel lists with full Application Optimization support: 14th-gen K and HX, Core Ultra 200S K, 200HX and 300H.</summary>
    [GeneratedRegex(@"i[579]-14[679]00K[FS]?\b|i[79]-14[79]00HX\b|Ultra [579] 2\d\d(KF?|HX)\b|Ultra [79] 3\d\dH\b", RegexOptions.IgnoreCase)] public static partial Regex IntelApoProcessor();

    public static TunePlan Plan(TuneScenario scenario, AdaptiveSync sync, TuneInputs x)
    {
        var items = new List<TuneItem>(); var good = new List<TuneCheck>();
        bool gaming = IsGaming(scenario), laptop = x.Graphics.Portable == true, onAc = x.Graphics.OnAcPower, lowPower = scenario == TuneScenario.LowPower;
        var w = x.Windows;
        bool nvidia = x.Graphics.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia), amd = x.Graphics.Adapters.Any(a => a.Vendor == GpuVendor.Amd && !a.LikelyIntegrated);
        void Hanki(TuneArea area, ChangeSource source, string id, string setting, string current, string target, string why, bool optional, string kind, string key, string after) =>
            items.Add(new(area, new ProposedChange(id, source, setting, current, target, why, optional, kind, key, after)));
        void Step(TuneArea area, ChangeSource source, string id, string setting, string current, string target, string why, string manual, string? uri = null, bool optional = false) =>
            items.Add(new(area, new ProposedChange(id, source, setting, current, target, why, optional, Manual: manual, SettingsUri: uri)));
        void Good(TuneArea area, string setting, string current) => good.Add(new(area, setting, current));
        const string Wg = PerformanceSettings.WindowsGamingKind, Graphics = "ms-settings:display-advancedgraphics", Hdr = "ms-settings:display-hdr", Power = "ms-settings:powersleep";

        // ---- Display: the highest refresh rate, except a laptop saving battery -------------------------------------
        double refresh = 60; int index = 0;
        var primary = x.Graphics.Displays.FirstOrDefault(d => d.Primary) ?? x.Graphics.Displays.FirstOrDefault();
        foreach (var d in x.Graphics.Displays) {
            index++;
            double after = d.Current.RefreshHz;
            var sixty = d.Supported.FirstOrDefault(m => m.Width == d.Current.Width && m.Height == d.Current.Height && Math.Abs(m.RefreshHz - 60) < 1);
            if (lowPower && laptop && d.Current.RefreshHz > 61 && sixty is not null) {
                Hanki(TuneArea.Display, ChangeSource.Display, $"tune-refresh-{index}", $"Refresh rate · {d.Name}", GraphicsFacts.Describe(d.Current), GraphicsFacts.Describe(sixty),
                    "A lower refresh rate uses less power on a laptop screen. Where Windows offers Dynamic refresh rate, it does this for you.", true,
                    "Display mode", d.Device, GraphicsFacts.ModeText(sixty.Width, sixty.Height, (int)Math.Round(sixty.RefreshHz)));
                after = 60;
            } else if (!lowPower && GraphicsFacts.FasterRefresh(d) is { } faster) {
                Hanki(TuneArea.Display, ChangeSource.Display, $"tune-refresh-{index}", $"Refresh rate · {d.Name}", GraphicsFacts.Describe(d.Current), GraphicsFacts.Describe(faster),
                    $"The display supports {Math.Round(faster.RefreshHz):0} Hz but runs at {Math.Round(d.Current.RefreshHz):0} Hz. Games can't show more frames than the display refreshes; a higher refresh rate is smoother and lowers input lag.", false,
                    "Display mode", d.Device, GraphicsFacts.ModeText(faster.Width, faster.Height, (int)Math.Round(faster.RefreshHz)));
                after = faster.RefreshHz;
            } else Good(TuneArea.Display, $"Refresh rate · {d.Name}", $"{Math.Round(d.Current.RefreshHz):0} Hz");
            if (d == primary) refresh = after;
        }
        uint cap = (uint)Math.Round(refresh);

        // HDR: worth it on bright, local-dimming or OLED displays; basic HDR400 panels often look worse than SDR.
        if (primary?.HdrSupported == true) {
            bool on = primary.HdrEnabled == true;
            if (scenario == TuneScenario.GamingQuality && !on)
                Step(TuneArea.Display, ChangeSource.Display, "tune-hdr", "HDR", "Off", "On, on a DisplayHDR 600+ or OLED display",
                    "HDR games, and older games through Auto HDR, look much better on a display with high brightness and local dimming, or OLED. On basic HDR400 displays HDR often looks duller than SDR, so leave it off there.",
                    "Turn on Use HDR in Settings → Display, then run Microsoft's Windows HDR Calibration app once.", Hdr, optional: true);
            else if (scenario == TuneScenario.Creative && on)
                Step(TuneArea.Display, ChangeSource.Display, "tune-hdr", "HDR", "On", "Off for SDR color work",
                    "With HDR on, Windows converts SDR content for the HDR signal, so SDR colors can look washed out or shifted. For accurate SDR color, work in SDR unless your display is calibrated for HDR.",
                    "Turn off Use HDR in Settings → Display while you edit SDR photos or video.", Hdr, optional: true);
            else if (lowPower && on)
                Step(TuneArea.Display, ChangeSource.Display, "tune-hdr", "HDR", "On", "Off", "HDR raises screen brightness and power use, especially on laptops.", "Turn off Use HDR in Settings → Display.", Hdr, optional: true);
            else Good(TuneArea.Display, "HDR", on ? "On" + (gaming ? "; run the Windows HDR Calibration app once if you haven't" : "") : "Off");
        }

        // Color for creative work: the signal format Windows reports, and automatic color management (Windows 11 24H2).
        if (scenario == TuneScenario.Creative) {
            int n = 0;
            string driverColor = nvidia ? "NVIDIA Control Panel → Change resolution → Use NVIDIA color settings: Output color format RGB, Output dynamic range Full" :
                amd ? "AMD Software → Settings → Display: Pixel Format RGB 4:4:4 Full RGB" : "your graphics driver's display settings: RGB output";
            foreach (var d in x.Graphics.Displays) {
                n++;
                if (d.Encoding is 2 or 3)
                    Step(TuneArea.Display, ChangeSource.Display, $"tune-color-format-{n}", $"Color format · {d.Name}", d.Encoding == 2 ? "YCbCr 4:2:2" : "YCbCr 4:2:0", "RGB",
                        "The display gets a chroma-subsampled signal, which carries color at a lower resolution than brightness: colored text and fine edges blur, and colors shift slightly. It usually comes from running out of cable bandwidth over HDMI at a high resolution or refresh rate.",
                        $"Choose {driverColor}. If RGB isn't offered, use DisplayPort or an Ultra High Speed HDMI cable, or lower the refresh rate.");
                else if (d.BitsPerColor is < 8 && !d.Connection.StartsWith("Built-in", StringComparison.Ordinal))
                    Step(TuneArea.Display, ChangeSource.Display, $"tune-color-depth-{n}", $"Color depth · {d.Name}", $"{d.BitsPerColor} bits per color", "8 or 10 bits per color",
                        "With 6 bits per color, smooth gradients show visible bands. External displays normally take 8 bits or more; fewer usually means the cable or port is short of bandwidth.",
                        (nvidia ? "NVIDIA Control Panel → Change resolution: Output color depth 8 bpc or higher." : amd ? "AMD Software → Settings → Display: Color Depth 8 bpc or higher." : "Choose 8 bits per color or more in your graphics driver's display settings.") +
                        " If it isn't offered, use a faster cable or lower the refresh rate.");
                else if (d.Encoding is 0 or 1 && d.BitsPerColor is { } bits)
                    Good(TuneArea.Display, $"Color signal · {d.Name}", $"{(d.Encoding == 0 ? "RGB" : "YCbCr 4:4:4")}, {bits} bits per color");
                if (d.AutoColorSupported == true && d.AutoColorOn == false && d.HdrEnabled != true)
                    Step(TuneArea.Display, ChangeSource.Display, $"tune-auto-color-{n}", $"Automatic color management · {d.Name}", "Off", "On",
                        "This display can show more colors than the sRGB that most apps and web images are made for. With automatic color management, Windows maps every app's colors to the display, so sRGB content isn't oversaturated and color-managed apps stay accurate.",
                        "Settings → System → Display → Advanced display: choose the display, then turn on Automatically manage color for apps.", "ms-settings:display-advanced", optional: true);
                else if (d.AutoColorOn == true) Good(TuneArea.Display, $"Automatic color management · {d.Name}", "On");
            }
        }

        // Laptop graphics mode: with the screen on the integrated GPU, every frame is copied through it. A MUX switch or
        // NVIDIA Advanced Optimus connects the screen straight to the graphics card; hybrid mode saves battery.
        if (laptop && primary is not null && x.Graphics.AdapterFor(primary) is { } screenGpu && x.Graphics.Adapters.FirstOrDefault(a => !a.LikelyIntegrated) is { } dedicated) {
            if (gaming && screenGpu.LikelyIntegrated)
                Step(TuneArea.Hardware, ChangeSource.Display, "tune-gpu-mode", "Laptop graphics mode", "Screen on the integrated graphics", $"Screen on the {dedicated.Name}",
                    "The screen is connected through the integrated graphics, so every frame the graphics card draws is copied through it. Laptops with a MUX switch or NVIDIA Advanced Optimus can connect the screen straight to the card: around 10% more frames on average, much more in some games, and lower input lag.",
                    "In your laptop maker's app (for example Armoury Crate: GPU Mode → Ultimate; Lenovo Vantage: Hybrid mode off; MSI Center: Discrete Graphics Mode) or in NVIDIA Control Panel → Manage Display Mode, choose the dedicated GPU and restart if asked. Switch back to hybrid for battery life. Not every laptop has this switch.",
                    optional: true);
            else if (lowPower && !screenGpu.LikelyIntegrated)
                Step(TuneArea.Hardware, ChangeSource.Display, "tune-gpu-mode", "Laptop graphics mode", $"Screen on the {dedicated.Name}", "Hybrid",
                    "With the screen connected straight to the graphics card, the card stays awake all the time. Hybrid mode lets it sleep when you aren't gaming, which saves a lot of battery.",
                    "In your laptop maker's app or in NVIDIA Control Panel → Manage Display Mode, choose Hybrid (Optimus) and restart if asked.");
        }

        // Adaptive sync: removes tearing without V-Sync's lag. Hanki can't read whether the monitor has it, so it asks.
        if (gaming) {
            if (sync == AdaptiveSync.Yes) {
                if (w.VariableRefresh != true)
                    Hanki(TuneArea.Windows, ChangeSource.Windows, "tune-vrr", "Variable refresh rate (Windows)", w.VariableRefresh == false ? "Off" : "Windows default", "On",
                        "Lets DirectX 11 games that don't support G-SYNC or FreeSync themselves use your display's adaptive sync.", false, Wg, "variable-refresh", "on");
                else Good(TuneArea.Windows, "Variable refresh rate (Windows)", "On");
                if (x.Sync is { On: true }) Good(TuneArea.GraphicsDriver, "G-SYNC", "On");
                else if (x.Sync is { Supported: true })
                    Step(TuneArea.GraphicsDriver, ChangeSource.Nvidia, "tune-adaptive-sync", "G-SYNC", "Off", "On",
                        "Your display supports G-SYNC, but it's switched off in the NVIDIA driver. NVIDIA doesn't let other apps switch it on, so this one is yours.",
                        "NVIDIA Control Panel → Set up G-SYNC: tick Enable G-SYNC, G-SYNC Compatible, for full screen mode, then apply.");
                else
                    Step(TuneArea.GraphicsDriver, nvidia ? ChangeSource.Nvidia : amd ? ChangeSource.Amd : ChangeSource.Display, "tune-adaptive-sync", "G-SYNC / FreeSync switched on", "Not readable", "On",
                        "Adaptive sync only works when it's on in the driver and, on many monitors, in the monitor's own menu. Hanki couldn't read those switches here.",
                        nvidia ? "NVIDIA Control Panel → Set up G-SYNC: tick Enable G-SYNC, G-SYNC Compatible, for full screen mode." :
                        amd ? "AMD Software → Gaming → Display: turn on AMD FreeSync." : "Turn on adaptive sync in your graphics driver and in the monitor's menu.", optional: true);
            } else if (sync == AdaptiveSync.NotSure)
                Step(TuneArea.Display, ChangeSource.Display, "tune-sync-unknown", "G-SYNC or FreeSync", "Unknown", "Check your monitor",
                    "With adaptive sync the best settings differ (V-Sync on in the driver and a frame cap just below the refresh rate), so Hanki leaves vertical sync and frame caps alone until it knows.",
                    "Look for G-SYNC, G-SYNC Compatible, FreeSync or Adaptive-Sync in the monitor's specifications or menu, then run Tune my PC again.");
        }

        // ---- Windows ---------------------------------------------------------------------------------------------
        if (gaming) {
            if (w.GameMode == false)
                Hanki(TuneArea.Windows, ChangeSource.Windows, "tune-game-mode", "Game Mode", "Off", "On",
                    "Game Mode gives the game priority and holds back Windows Update installs and some background work while you play. It helps most when other apps are running.", false, Wg, "game-mode", "on");
            else Good(TuneArea.Windows, "Game Mode", "On");
            if (w.WindowedOptimizations == false)
                Hanki(TuneArea.Windows, ChangeSource.Windows, "tune-windowed", "Optimizations for windowed games", "Off", "On",
                    "DirectX 10 and 11 games in a window or borderless window use a faster presentation model: lower latency, and Auto HDR and variable refresh rate work there too.", false, Wg, "windowed-optimizations", "on");
            else Good(TuneArea.Windows, "Optimizations for windowed games", w.WindowedOptimizations == true ? "On" : "Windows default");
            // Auto HDR: only with HDR switched on, for the image-quality choice.
            if (scenario == TuneScenario.GamingQuality && primary?.HdrEnabled == true) {
                if (w.AutoHdr != true)
                    Hanki(TuneArea.Windows, ChangeSource.Windows, "tune-auto-hdr", "Auto HDR", w.AutoHdr == false ? "Off" : "Windows default", "On",
                        "With HDR on, Auto HDR adds HDR to DirectX 11 and 12 games made for SDR: brighter highlights and richer color. Some games look better without it; you can turn it off for a game in Game Bar.",
                        true, Wg, "auto-hdr", "on");
                else Good(TuneArea.Windows, "Auto HDR", "On");
            }
            if (x.Graphics.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia && FrameGenerationGpu().IsMatch(a.Name)) && w.HardwareScheduling == false)
                Step(TuneArea.Windows, ChangeSource.Windows, "tune-hags", "Hardware-accelerated GPU scheduling", "Off", "On",
                    "DLSS Frame Generation on GeForce RTX 40 and 50 series cards needs it. Otherwise it makes little measurable difference, so Hanki only suggests it for these cards.",
                    "Turn it on under Settings → Display → Graphics → Change default graphics settings, then restart Windows.", Graphics, optional: true);
            else Good(TuneArea.Windows, "Hardware-accelerated GPU scheduling", (w.HardwareScheduling switch { true => "On", false => "Off", _ => "Windows default" }) + ", no change needed");
        }
        if (w.RecordsInBackground)
            Hanki(TuneArea.Windows, ChangeSource.Windows, "tune-background-recording", "Game Bar background recording", "On", "Off",
                "“Record what happened” keeps the video encoder and disk busy the whole time you play; Windows' own setting warns it may affect game performance. You can still record on demand with Win+Alt+R.",
                scenario is TuneScenario.GamingQuality or TuneScenario.Creative, Wg, "background-recording", "off");
        else Good(TuneArea.Windows, "Game Bar background recording", "Off");

        // ---- Mouse: acceleration off for games, so the same hand movement always aims the same distance -------------
        if (gaming) {
            if (w.MouseAcceleration == true)
                Hanki(TuneArea.Mouse, ChangeSource.Windows, "tune-mouse", "Mouse acceleration (Enhance pointer precision)", "On", "Off",
                    "Acceleration moves the pointer further when you move the mouse faster, so aim depends on speed. Off gives a fixed hand-to-screen ratio that muscle memory relies on. Games with raw input aren't affected either way.",
                    false, Wg, "mouse-acceleration", "0,0,0");
            else if (w.MouseAcceleration == false) Good(TuneArea.Mouse, "Mouse acceleration", "Off");
        }

        // ---- Power and processor ---------------------------------------------------------------------------------
        // Windows' power mode applies to the Balanced plan; Hanki changes it there and reads it back. With another plan it
        // stays a step, since Windows may not use the power mode at all. It's set for the current power source: a laptop
        // keeps one for plugged in and one for battery.
        void PowerMode(string target, string why, bool optional = false) {
            if (w.PowerMode is not null && w.PowerPlan == WindowsGamingParsing.BalancedPlan)
                Hanki(TuneArea.Processor, ChangeSource.Windows, "tune-power-mode", "Power mode", w.PowerMode, target, why, optional, Wg, "power-mode", target);
            else Step(TuneArea.Processor, ChangeSource.Windows, "tune-power-mode", "Power mode", w.PowerMode ?? "Unknown", target, why, "Settings → System → Power & battery → Power mode.", Power, optional);
        }
        const string Efficient = "Lowers processor and graphics power use: cooler, quieter and less energy.";
        if (lowPower && laptop && !onAc) {
            if (w.PowerMode != "Best power efficiency") PowerMode("Best power efficiency", Efficient + " This sets the mode for battery; plugged in keeps its own.");
            else Good(TuneArea.Processor, "Power mode on battery", w.PowerMode);
        }
        // Energy saver: dims the screen and pauses background apps, sync and non-critical updates on battery.
        if (lowPower && laptop && w.EnergySaverThreshold is { } threshold && w.PowerPlan is { } batteryPlan) {
            string level = threshold == 0 ? "Never" : threshold >= 100 ? "Always on battery" : $"{threshold}% battery";
            if (threshold < 50)
                Hanki(TuneArea.Processor, ChangeSource.Windows, "tune-energy-saver", "Energy saver turns on at", level, "50% battery",
                    "Energy saver dims the screen, pauses background apps and sync, and holds back non-critical updates on battery. Starting it at half charge makes the second half of the battery last noticeably longer; you can still turn it off from Quick Settings.",
                    true, PerformanceSettings.EnergySaverKind, batteryPlan + "|dc", "50");
            else Good(TuneArea.Processor, "Energy saver turns on at", level);
        }
        if (onAc || !laptop) {
            if (lowPower) {
                if (w.PowerMode != "Best power efficiency") PowerMode("Best power efficiency", Efficient + (laptop ? " This sets the mode for plugged in; run Tune my PC on battery to set that one too." : ""));
                else Good(TuneArea.Processor, "Power mode", w.PowerMode);
            } else if (w.PowerMode == "Best power efficiency")
                PowerMode(scenario is TuneScenario.GamingPerformance or TuneScenario.Creative ? "Best performance" : "Balanced", "“Best power efficiency” lowers processor and graphics performance to save energy.");
            else if (w.PowerMode == "Balanced" && scenario is TuneScenario.GamingPerformance or TuneScenario.Creative)
                PowerMode("Best performance", "Lets the processor reach its highest clocks sooner. The gain is small in games limited by the graphics card and larger in processor-heavy games and creative apps; laptops run warmer and louder.", optional: true);
            else if (w.PowerMode is not null) Good(TuneArea.Processor, "Power mode", w.PowerMode);
            if (!lowPower && w.ProcessorMaximumAc is { } max && max < 100 && w.PowerPlan is { } plan)
                Hanki(TuneArea.Processor, ChangeSource.Windows, "tune-processor-maximum", "Maximum processor state (plugged in)", $"{max}%", "100%",
                    $"The power plan caps the processor at {max}% on mains power, which also blocks boost clocks.", false, "Processor power", plan + "|ac", "100");
            else if (!lowPower && w.ProcessorMaximumAc == 100) Good(TuneArea.Processor, "Maximum processor state", "100%");
        } else if (!lowPower)
            Step(TuneArea.Processor, ChangeSource.Windows, "tune-plug-in", "Power source", "On battery", "Plugged in",
                "Laptops limit processor and graphics power on battery, whatever the settings. The plugged-in settings in this plan apply once you plug in.", "Plug in the charger, then run Tune my PC again.");

        // Processor helpers from AMD and Intel that steer games onto the right cores.
        if (gaming && x.Cpu is { } cpu) {
            if (DualCacheRyzen().IsMatch(cpu.Name)) {
                if (x.VCacheOptimizer == false)
                    Step(TuneArea.Processor, ChangeSource.Windows, "tune-x3d", "AMD 3D V-Cache Performance Optimizer", "Not installed", "Installed",
                        $"The {cpu.Name} has two groups of cores, and only one has the extra 3D V-Cache that games benefit from. AMD's chipset driver parks the other group while you play; without it, games can run on the wrong cores and lose frames.",
                        "Install the newest AMD chipset driver for your motherboard from amd.com (it includes the 3D V-Cache Performance Optimizer), keep Xbox Game Bar updated and Game Mode on, then restart.");
                else if (x.VCacheOptimizer == true) {
                    Good(TuneArea.Processor, "AMD 3D V-Cache Performance Optimizer", "Installed");
                    if (w.PowerPlan is { } plan && plan != WindowsGamingParsing.BalancedPlan)
                        Step(TuneArea.Processor, ChangeSource.Windows, "tune-x3d-plan", "Power plan", w.PowerPlanName ?? "Another plan", "Balanced",
                            "AMD's driver parks the cores without 3D V-Cache while you play, and that only works with the Balanced power plan. High performance and similar plans keep every core awake, so games can land on the slower group.",
                            "Choose Balanced under Control Panel → Power Options (or CPU → Power plans in Hanki), then use Power mode for more speed.");
                }
            }
            if (IntelApoProcessor().IsMatch(cpu.Name)) {
                if (x.IntelTuning == false)
                    Step(TuneArea.Processor, ChangeSource.Windows, "tune-apo", "Intel Application Optimization (APO)", "Not set up", "On",
                        $"Intel lists the {cpu.Name} as fully supported by APO, which steers the threads of the games on Intel's list across performance and efficiency cores for more frames. It runs inside Intel Dynamic Tuning Technology, which isn't installed.",
                        "Install Intel Dynamic Tuning Technology (with APO) from your motherboard or laptop maker's support page and update the BIOS if asked. Intel's optional APO app lets you pick games.", optional: true);
                else if (x.IntelTuning == true) Good(TuneArea.Processor, "Intel Dynamic Tuning (APO)", "Installed");
            }
        }

        // ---- NVIDIA global settings (every game without its own value) -------------------------------------------
        if (x.Nvidia is { } nv) {
            void Nv(uint id, uint? value, string why, bool optional = false) {
                if (nv.FirstOrDefault(s => s.Setting.Id == id) is not { } current) return;
                if (NvidiaPresets.Change(current, value, why, optional) is { } change) items.Add(new(TuneArea.GraphicsDriver, change with { Id = "tune-" + change.Id }));
                else Good(TuneArea.GraphicsDriver, current.Setting.Name, current.Text);
            }
            uint? Effective(uint id) => nv.FirstOrDefault(s => s.Setting.Id == id)?.Effective;
            const string GsyncVsync = "With G-SYNC, V-Sync on in the driver stops tearing when a frame arrives early, without classic V-Sync's lag. Turn V-Sync off inside games.";
            string capWhy = $"Capping 3 FPS below the {cap} Hz refresh rate keeps games inside the G-SYNC range, where it adds no lag. Games with NVIDIA Reflex cap automatically.";
            bool fixedRefresh = Effective(NvidiaSettings.MonitorTechnologyId) == 4;
            switch (scenario) {
                case TuneScenario.GamingPerformance:
                    Nv(NvidiaSettings.PreRenderedFramesId, 1, "Low Latency Mode: the driver queues one frame, which lowers input lag when the graphics card is the limit. In games with NVIDIA Reflex, turn Reflex on instead; it overrides this.");
                    if (sync == AdaptiveSync.Yes) {
                        Nv(NvidiaSettings.VerticalSyncId, NvidiaSettings.VsyncOn, GsyncVsync);
                        if (cap > 63) Nv(NvidiaSettings.FrameRateLimitId, cap - 3, capWhy);
                        if (fixedRefresh) Nv(NvidiaSettings.MonitorTechnologyId, 0, "Games use G-SYNC instead of a fixed refresh rate.");
                    } else if (sync == AdaptiveSync.No)
                        Nv(NvidiaSettings.VerticalSyncId, NvidiaSettings.VsyncOff, "Without adaptive sync, V-Sync adds input lag. For competitive play the usual choice is V-Sync off and some tearing.");
                    Nv(NvidiaSettings.PreferredRefreshRateId, 1, "Games that don't choose a refresh rate get your display's highest.");
                    Nv(NvidiaSettings.TextureFilteringId, NvidiaSettings.TexturePerformance, "“Performance” texture filtering trades a little texture sharpness for a small frame-rate gain.", optional: true);
                    break;
                case TuneScenario.GamingQuality:
                    Nv(NvidiaSettings.VerticalSyncId, NvidiaSettings.VsyncOn, sync == AdaptiveSync.Yes ? GsyncVsync :
                        "V-Sync removes tearing. In single-player games a clean image matters more than the small extra lag. Turn V-Sync off inside games so it isn't applied twice.");
                    if (sync == AdaptiveSync.Yes && cap > 63) Nv(NvidiaSettings.FrameRateLimitId, cap - 3, capWhy);
                    if (sync == AdaptiveSync.Yes && fixedRefresh) Nv(NvidiaSettings.MonitorTechnologyId, 0, "Games use G-SYNC instead of a fixed refresh rate.");
                    Nv(NvidiaSettings.PreferredRefreshRateId, 1, "Games that don't choose a refresh rate get your display's highest.");
                    Nv(NvidiaSettings.TextureFilteringId, NvidiaSettings.TextureHighQuality, "“High quality” texture filtering turns off the driver's texture shortcuts.");
                    Nv(NvidiaSettings.AnisotropicModeId, 1, "Lets the driver set anisotropic filtering for every game.", optional: true);
                    Nv(NvidiaSettings.AnisotropicLevelId, 16, "16x anisotropic filtering keeps textures sharp at steep angles; modern cards handle it cheaply.", optional: true);
                    break;
                case TuneScenario.LowPower:
                    if (cap >= 30) Nv(NvidiaSettings.FrameRateLimitId, Math.Min(cap, 60u), "A 60 FPS cap in every game saves power, heat and fan noise, and still looks smooth on most displays.");
                    if (Effective(NvidiaSettings.PowerManagementId) == NvidiaSettings.PowerPreferMaximum)
                        Nv(NvidiaSettings.PowerManagementId, null, "“Prefer maximum performance” for all apps keeps the card at high clocks even at the desktop, which can add 15–25 W.");
                    break;
            }
            if (Effective(NvidiaSettings.PowerManagementId) == NvidiaSettings.PowerPreferMinimum && !lowPower)
                Nv(NvidiaSettings.PowerManagementId, null, "“Prefer maximum power savings” keeps the card at low clocks in every game.");
            if (scenario == TuneScenario.GamingPerformance) {
                const string clocksWhy = "Keeps the graphics card at full clocks while this game runs, for steadier frame times. Set per game, the card still clocks down at the desktop, where full clocks can add 15–25 W.";
                var games = (x.Games ?? []).Take(GameLimit).ToArray();
                int alreadyFull = 0;
                foreach (var game in games) {
                    if (GamingProfiles.NvidiaGameChange(game, NvidiaSettings.PowerManagementId, NvidiaSettings.PowerPreferMaximum, clocksWhy, optional: true) is { } change)
                        items.Add(new(TuneArea.Games, change with { Id = "tune-clocks:" + Path.GetFileName(game.ExecutablePath).ToLowerInvariant(), Setting = "Full GPU clocks: " + game.Name }));
                    else if (GamingProfiles.NvidiaEffective(game, NvidiaSettings.PowerManagementId)?.Value == NvidiaSettings.PowerPreferMaximum) alreadyFull++;
                }
                if (alreadyFull > 0) Good(TuneArea.Games, "Full GPU clocks", alreadyFull == 1 ? "On for 1 game" : $"On for {alreadyFull} games");
                if (games.Length == 0)
                    Step(TuneArea.GraphicsDriver, ChangeSource.Nvidia, "tune-per-game-clocks", "Full GPU clocks in your games", "No games listed", "Set per game",
                        "Keeping the graphics card at full clocks helps some games. Set per game it doesn't keep the card clocked up at the desktop, where it can add 15–25 W.",
                        "Gaming → Games: find your games or add one, then run Tune my PC again. Hanki then offers full clocks for each of them.", optional: true);
            }
        } else if (nvidia && gaming)
            Step(TuneArea.GraphicsDriver, ChangeSource.Nvidia, "tune-nvidia-missing", "NVIDIA driver settings", "Not available", "Readable",
                "Hanki couldn't open the NVIDIA driver interface, so driver settings aren't part of this plan.", "Install or repair the NVIDIA driver from nvidia.com, then run Tune my PC again.");

        // ---- AMD Radeon: changes through AMD's driver interface when it could be read, otherwise steps ----------------------
        if (x.Amd is { } radeon)
            foreach (var change in AmdSettings.ForScenario(scenario, sync, radeon, cap)) items.Add(new(TuneArea.GraphicsDriver, change with { Id = "tune-" + change.Id }));
        else if (amd) {
            string choices = scenario switch {
                TuneScenario.GamingPerformance => "Radeon Anti-Lag on, Radeon Chill off, Radeon Boost off, Enhanced Sync off" + (sync == AdaptiveSync.Yes ? $", FreeSync on with Frame Rate Target Control at {Math.Max(30, (int)cap - 3)} FPS" : "") + ", Texture Filtering Quality: Performance.",
                TuneScenario.GamingQuality => "Radeon Anti-Lag on, Radeon Chill off" + (sync == AdaptiveSync.Yes ? $", FreeSync on with Frame Rate Target Control at {Math.Max(30, (int)cap - 3)} FPS" : ", Wait for Vertical Refresh: Always on") + ", Texture Filtering Quality: High.",
                TuneScenario.Creative => "use the Default graphics profile; gaming features such as Chill or Boost don't help creative apps.",
                _ => "Radeon Chill on (for example 40–60 FPS) and Frame Rate Target Control at 60 FPS."
            };
            Step(TuneArea.GraphicsDriver, ChangeSource.Amd, "tune-amd", "AMD Radeon settings", "Couldn't be read", "See the step",
                "AMD's driver interface couldn't be read on this PC, so these are the matching choices for " + Name(scenario) + " to set yourself.", "AMD Software → Gaming → Graphics: " + choices, optional: true);
        }

        // Driver age: NVIDIA, AMD and Intel ship game fixes and optimizations about monthly; half a year behind misses several.
        if (gaming && x.Now is { } now && x.Graphics.HighPerformance is { DriverDate: { } driverDate } gpu) {
            int months = (int)((now.UtcDateTime - driverDate).TotalDays / 30.44);
            if (months >= 6)
                Step(TuneArea.GraphicsDriver, gpu.Vendor switch { GpuVendor.Nvidia => ChangeSource.Nvidia, GpuVendor.Amd => ChangeSource.Amd, _ => ChangeSource.Display },
                    "tune-driver-age", $"Graphics driver · {gpu.Name}", $"From {driverDate:d}, {months} months old", "A driver from the last few months",
                    "Graphics drivers get fixes and optimizations for new games about every month. A driver this old misses them, and new games may warn about it or run worse.",
                    gpu.Vendor switch {
                        GpuVendor.Nvidia => "Install the newest Game Ready driver from the NVIDIA app (Drivers) or nvidia.com.",
                        GpuVendor.Amd => "In AMD Software, open System → Software & Driver and check for updates, or download the driver from amd.com/support.",
                        GpuVendor.Intel => "Install the newest graphics driver with Intel Driver & Support Assistant, or from intel.com.",
                        _ => "Check Windows Update → Advanced options → Optional updates, or your PC maker's support site."
                    } + (laptop ? " Some laptop makers publish their own tested drivers; theirs are fine too." : ""), optional: months < 12);
            else Good(TuneArea.GraphicsDriver, "Graphics driver age", months <= 1 ? $"From {driverDate:d}" : $"From {driverDate:d}, {months} months old");
        }

        // ---- Network: for competitive online games, a steady connection matters more than raw speed ------------------------
        if (scenario == TuneScenario.GamingPerformance && x.Links is { } links) {
            var up = links.Where(a => a.Bps > 0 && (NetworkLink.Wired(a) || NetworkLink.Wireless(a))).ToArray();
            var wired = up.Where(NetworkLink.Wired).ToArray();
            if (wired.FirstOrDefault(a => a.Bps <= 100e6 || a.FullDuplex == false) is { } slow)
                Step(TuneArea.Network, ChangeSource.Windows, "tune-link-speed", $"Wired connection · {slow.Name}", NetworkLink.Rate(slow.Bps) + (slow.FullDuplex == false ? ", half duplex" : ""), "1 Gbps, full duplex",
                    "Network cards and routers from the last 15 years connect at 1 Gbps or more. A slower or half-duplex link usually means a damaged or old cable, or a loose plug, which can also drop packets: lag spikes in online games.",
                    "Try another network cable (Cat 5e or newer) and another port on the router or switch, then check Connect → Network connection speed.",
                    optional: slow.Bps > 10e6 && slow.FullDuplex != false);
            else if (wired.Length > 0) Good(TuneArea.Network, "Wired connection", NetworkLink.Rate(wired.Max(a => a.Bps)));
            else if (up.Any(NetworkLink.Wireless))
                Step(TuneArea.Network, ChangeSource.Windows, "tune-wired", "Network connection", "Wi-Fi", "A network cable",
                    "Wi-Fi shares the air with other devices and walls, so its latency jumps around; those jumps feel like lag in online games. A cable gives a steady, lower ping.",
                    "Connect the PC to the router with a network cable. If that isn't possible, use the router's 5 GHz or 6 GHz network and keep the router in view of the PC.", optional: true);
            if (up.Length > 0)
                Step(TuneArea.Network, ChangeSource.Windows, "tune-downloads", "Downloads while you play", "Not visible to Hanki", "Paused or limited",
                    "Game launchers and Windows Update download in the background and can fill your connection, which raises ping and causes lag spikes in online games.",
                    "Steam → Settings → Downloads: turn off Allow downloads during gameplay (other launchers have a similar setting). For Windows Update, limit the background download bandwidth under Delivery Optimization → Advanced options.",
                    "ms-settings:delivery-optimization-advanced", optional: true);
        }

        // ---- In-game settings: the biggest levers, which only the game controls ------------------------------------------
        if (gaming)
            Step(TuneArea.Games, ChangeSource.Game, "tune-in-game", "In-game settings", "Not visible to Hanki", "See the step",
                "Games override many driver settings, and some of the biggest wins only exist in the game.",
                scenario == TuneScenario.GamingPerformance
                    ? "Turn on NVIDIA Reflex (or AMD Anti-Lag 2) where offered, turn V-Sync off in the game, use fullscreen or borderless, and lower the heaviest settings (shadows, volumetrics) before resolution."
                    : "Turn V-Sync off in the game when the driver's V-Sync is on, and use DLSS, FSR or XeSS in Quality mode if you need more frames.", optional: true);

        // Per-game advice from Launch & measure: what held each game back in its latest run (last 90 days).
        if (gaming)
            foreach (var m in (x.Measured ?? []).Where(m => x.Now is not { } at || at - m.At <= TimeSpan.FromDays(90)).Take(GameLimit)) {
                string id = "tune-measured:" + m.Game.ToLowerInvariant(), setting = "Measured: " + m.Game,
                    current = m.Diagnosis.TrimEnd('.') + (m.AverageFps is { } fps ? $", {fps:0} FPS" + (m.Low1Fps is { } low ? $" (1% low {low:0})" : "") : "");
                string ran = x.Now is { } scanned && (scanned - m.At).TotalDays < 1 ? "today" : $"on {m.At.ToLocalTime():d}";
                void Advice(string target, string why, string manual, bool optional = true) =>
                    Step(TuneArea.Games, ChangeSource.Game, id, setting, current, target, $"Launch & measure {ran}: {why}", manual, optional: optional);
                switch (m.Limiter) {
                    case Limiter.Vram:
                        Advice("Textures one step lower", "video memory was full, so textures were streamed from system memory, which causes stutter.",
                            $"In {m.Game}, lower texture quality one step (or the resolution, if textures are already low).", optional: false);
                        break;
                    case Limiter.Gpu when scenario == TuneScenario.GamingPerformance:
                        Advice("Upscaling or lighter graphics", "the graphics card set the pace, so graphics settings decide the frame rate.",
                            $"In {m.Game}, turn on DLSS, FSR or XeSS (Quality or Balanced), or lower ray tracing, shadows and volumetric effects. Lowering view distance won't help much.");
                        break;
                    case Limiter.Gpu:
                        Advice("Upscaling in Quality mode", "the graphics card set the pace.",
                            $"If you want more frames in {m.Game}, use DLSS, FSR or XeSS in Quality mode before lowering settings you can see.");
                        break;
                    case Limiter.Cpu when scenario == TuneScenario.GamingPerformance:
                        Advice("Lighter processor settings", "the processor set the pace while the graphics card had spare capacity.",
                            $"In {m.Game}, lower view distance, crowd or NPC density and physics detail. Lowering resolution or textures won't raise the frame rate here.");
                        break;
                    case Limiter.Cpu:
                        Advice("Higher graphics settings", "the processor set the pace, so the graphics card has room to spare.",
                            $"In {m.Game}, higher resolution, anti-aliasing or texture settings cost little or nothing. If you need more frames, lower view distance or crowd density instead.");
                        break;
                    case Limiter.Memory:
                        Advice("More free memory while playing", "memory ran short and Windows read it back from disk.", $"Close large programs, such as browsers with many tabs, before starting {m.Game}.");
                        break;
                    case Limiter.Storage:
                        Advice("The game on an SSD", "the disk was fully busy for long stretches.", $"Move {m.Game} to an SSD, ideally NVMe (Storage page), and pause downloads while you play.", optional: false);
                        break;
                    case Limiter.Thermal:
                        Advice("Cooler running", "power or temperature limits lowered the clocks.", "Clean dust from fans and filters, keep vents clear and check the PC's airflow.", optional: false);
                        break;
                    case Limiter.Background:
                        Advice("Background programs paused", "another program used the processor or disk at the same time.", $"Pause downloads, sync and scans while you play {m.Game}.");
                        break;
                    case Limiter.None:
                        Good(TuneArea.Games, setting, "No limit reached" + (m.AverageFps is { } f ? $", {f:0} FPS" : ""));
                        break;
                }
            }

        // Streaming and recording: OBS on the processor's x264 while a hardware encoder is available.
        if (x.Obs is { Count: > 0 } obs && x.Graphics.Adapters.OrderBy(a => a.LikelyIntegrated).ThenBy(a => a.PreferenceRank)
                .FirstOrDefault(a => a.Vendor is GpuVendor.Nvidia or GpuVendor.Amd or GpuVendor.Intel) is { } encoderGpu) {
            string hardware = encoderGpu.Vendor switch { GpuVendor.Nvidia => "NVIDIA NVENC", GpuVendor.Amd => "AMD hardware encoder (AMF)", _ => "Intel Quick Sync (QSV)" };
            var software = obs.Where(e => ObsSettings.Software(e.Encoder) == true).GroupBy(e => e.Profile, StringComparer.Ordinal).ToArray();
            foreach (var profile in software)
                Step(TuneArea.Streaming, ChangeSource.Windows, "tune-obs:" + profile.Key.ToLowerInvariant(), $"OBS encoder · {profile.Key}",
                    $"{string.Join(", ", profile.Select(e => e.Encoder).Distinct())} on the processor ({string.Join(" and ", profile.Select(e => e.Use).Distinct())})", hardware,
                    (gaming ? "x264 encodes video on the processor, which takes processor time from the game while you stream or record. " :
                        lowPower ? "x264 encodes video on the processor, which uses far more power than the graphics hardware's video engine. " :
                        "x264 encodes video on the processor, which competes with editing and rendering. ") +
                    $"The {hardware} on your {encoderGpu.Name} does the same job on a separate video engine, with little effect on the processor.",
                    $"OBS → Settings → Output: set Video Encoder to {hardware} (in Simple mode, the Hardware option) for this profile, and keep your bitrate.", optional: true);
            if (software.Length == 0 && obs.Any(e => ObsSettings.Software(e.Encoder) == false)) Good(TuneArea.Streaming, "OBS encoder", "Hardware");
        }

        // ---- Processor, memory, storage and hardware findings (steps: hardware, BIOS or Windows tools) ---------------------
        foreach (var f in x.Findings) {
            if (f.FindingId is "processor-maximum" or "power-mode" or "power-plan") continue;
            if (lowPower && f.FindingId is "memory-speed" or "core-parking") continue;
            bool background = f.Metadata.GetValueOrDefault("source") == "Background", hardware = f.ModuleId == GamingHealth.ModuleId;
            // Busy programs, recorders and a second frame limiter matter while you game; lanes and Resizable BAR don't save power.
            if (background && !gaming || hardware && lowPower) continue;
            var area = background ? TuneArea.Background : hardware ? TuneArea.Hardware :
                f.ModuleId switch { "perf-cpu" => TuneArea.Processor, "perf-memory" => TuneArea.Memory, "perf-storage" => TuneArea.Storage, _ => TuneArea.Windows };
            // Observations worth a try while gaming: Resizable BAR off, and memory that ran short earlier (the pagefile grew).
            bool worthTrying = gaming && f.Severity == FindingSeverity.Informational &&
                (f.FindingId.StartsWith("resizable-bar-", StringComparison.Ordinal) || f.ModuleId == "perf-memory" && f.FindingId == "commit");
            if (f.Severity is FindingSeverity.Warning or FindingSeverity.Critical || worthTrying)
                Step(area, ChangeSource.Windows, f.FindingId, f.Title, f.Metadata.GetValueOrDefault("current", "?"), f.Metadata.GetValueOrDefault("recommended", "?"), f.Explanation,
                    f.Recommendation ?? $"Open {area} for details.", f.Metadata.GetValueOrDefault("settings"), optional: background || worthTrying);
            else if (f.Severity == FindingSeverity.Healthy) Good(area, f.Title, f.Metadata.GetValueOrDefault("current", "OK"));
        }

        // One change per setting: the first wins (a display-mode change and a finding for the same display can't both apply).
        var unique = items.GroupBy(i => i.Change.HankiApplies ? $"{i.Change.Kind}|{i.Change.Target}" : "step|" + i.Change.Id).Select(g => g.First()).ToArray();
        return new(scenario, sync, unique, good);
    }
}
