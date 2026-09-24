using System.Text.RegularExpressions;
namespace IgezziGuard;

public enum TuneScenario { GamingPerformance, GamingQuality, Creative, LowPower }
/// <summary>Whether the display has G-SYNC or FreeSync. Windows doesn't report it reliably, so Tune my PC asks.</summary>
public enum AdaptiveSync { NotSure, Yes, No }
public enum TuneArea { Display, Windows, Mouse, GraphicsDriver, Games, Background, Processor, Memory, Storage }

/// <summary>
/// What the Tune my PC scan read. Nvidia is null without an NVIDIA driver; Findings are the CPU, memory and storage
/// analyzers' results and the background check's (overlays, recorders, limiters, busy programs).
/// </summary>
public sealed record TuneInputs(GraphicsInventory Graphics, WindowsGamingSettings Windows, IReadOnlyList<NvidiaGlobalSetting>? Nvidia, IReadOnlyList<DiagnosticResult> Findings);
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
/// driver, processor, memory and storage. The recommendations and their sources are in docs/TUNING.md. Only
/// documented settings are changed, each through the review dialog and Recovery; hardware and BIOS steps, in-game
/// settings and settings Hanki can't read are listed as steps for you. Nothing is changed by building a plan.
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
    [GeneratedRegex(@"RTX\s*(40|50)\d{2}", RegexOptions.IgnoreCase)] private static partial Regex FrameGenerationGpu();

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

        // Adaptive sync: removes tearing without V-Sync's lag. Hanki can't read whether the monitor has it, so it asks.
        if (gaming) {
            if (sync == AdaptiveSync.Yes) {
                if (w.VariableRefresh != true)
                    Hanki(TuneArea.Windows, ChangeSource.Windows, "tune-vrr", "Variable refresh rate (Windows)", w.VariableRefresh == false ? "Off" : "Windows default", "On",
                        "Lets DirectX 11 games that don't support G-SYNC or FreeSync themselves use your display's adaptive sync.", false, Wg, "variable-refresh", "on");
                else Good(TuneArea.Windows, "Variable refresh rate (Windows)", "On");
                Step(TuneArea.GraphicsDriver, nvidia ? ChangeSource.Nvidia : amd ? ChangeSource.Amd : ChangeSource.Display, "tune-adaptive-sync", "G-SYNC / FreeSync switched on", "Not readable", "On",
                    "Adaptive sync only works when it's on in the driver and, on many monitors, in the monitor's own menu. Hanki can't read those switches.",
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
        if (onAc || !laptop) {
            if (lowPower) {
                if (w.PowerMode != "Best power efficiency")
                    Step(TuneArea.Processor, ChangeSource.Windows, "tune-power-mode", "Power mode", w.PowerMode ?? "Unknown", "Best power efficiency",
                        "Lowers processor and graphics power use: cooler, quieter and less energy.", "Settings → System → Power & battery → Power mode.", Power);
                else Good(TuneArea.Processor, "Power mode", w.PowerMode);
            } else if (w.PowerMode == "Best power efficiency")
                Step(TuneArea.Processor, ChangeSource.Windows, "tune-power-mode", "Power mode", w.PowerMode, "Balanced or Best performance",
                    "“Best power efficiency” lowers processor and graphics performance to save energy.", "Settings → System → Power & battery → Power mode.", Power);
            else if (w.PowerMode == "Balanced" && scenario is TuneScenario.GamingPerformance or TuneScenario.Creative)
                Step(TuneArea.Processor, ChangeSource.Windows, "tune-power-mode", "Power mode", "Balanced", "Best performance",
                    "Lets the processor reach its highest clocks sooner. The gain is small in games limited by the graphics card and larger in processor-heavy games and creative apps; laptops run warmer and louder.",
                    "Settings → System → Power & battery → Power mode.", Power, optional: true);
            else if (w.PowerMode is not null) Good(TuneArea.Processor, "Power mode", w.PowerMode);
            if (!lowPower && w.ProcessorMaximumAc is { } max && max < 100 && w.PowerPlan is { } plan)
                Hanki(TuneArea.Processor, ChangeSource.Windows, "tune-processor-maximum", "Maximum processor state (plugged in)", $"{max}%", "100%",
                    $"The power plan caps the processor at {max}% on mains power, which also blocks boost clocks.", false, "Processor power", plan + "|ac", "100");
            else if (!lowPower && w.ProcessorMaximumAc == 100) Good(TuneArea.Processor, "Maximum processor state", "100%");
        } else if (!lowPower)
            Step(TuneArea.Processor, ChangeSource.Windows, "tune-plug-in", "Power source", "On battery", "Plugged in",
                "Laptops limit processor and graphics power on battery, whatever the settings. The plugged-in settings in this plan apply once you plug in.", "Plug in the charger, then run Tune my PC again.");

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
            if (scenario == TuneScenario.GamingPerformance)
                Step(TuneArea.GraphicsDriver, ChangeSource.Nvidia, "tune-per-game-clocks", "Full GPU clocks in your games", "Set per game", "Optimize each game",
                    "Keeping the graphics card at full clocks helps some games. Set per game it doesn't keep the card clocked up at the desktop, where it can add 15–25 W.",
                    "Gaming → Games: choose a game, pick Competitive or Maximum FPS, then Optimize this game.", optional: true);
        } else if (nvidia && gaming)
            Step(TuneArea.GraphicsDriver, ChangeSource.Nvidia, "tune-nvidia-missing", "NVIDIA driver settings", "Not available", "Readable",
                "Hanki couldn't open the NVIDIA driver interface, so driver settings aren't part of this plan.", "Install or repair the NVIDIA driver from nvidia.com, then run Tune my PC again.");

        // ---- AMD Radeon: shown as steps until Hanki reads Radeon settings ----------------------------------------------
        if (amd) {
            string radeon = scenario switch {
                TuneScenario.GamingPerformance => "Radeon Anti-Lag on, Radeon Chill off, Radeon Boost off, Enhanced Sync off" + (sync == AdaptiveSync.Yes ? $", FreeSync on with Frame Rate Target Control at {Math.Max(30, (int)cap - 3)} FPS" : "") + ", Texture Filtering Quality: Performance.",
                TuneScenario.GamingQuality => "Radeon Anti-Lag on, Radeon Chill off" + (sync == AdaptiveSync.Yes ? $", FreeSync on with Frame Rate Target Control at {Math.Max(30, (int)cap - 3)} FPS" : ", Wait for Vertical Refresh: Always on") + ", Texture Filtering Quality: High.",
                TuneScenario.Creative => "use the Default graphics profile; gaming features such as Chill or Boost don't help creative apps.",
                _ => "Radeon Chill on (for example 40–60 FPS) and Frame Rate Target Control at 60 FPS."
            };
            Step(TuneArea.GraphicsDriver, ChangeSource.Amd, "tune-amd", "AMD Radeon settings", "Not read by Hanki yet", "See the step",
                "Hanki doesn't read or change Radeon settings yet. These are the matching choices for " + Name(scenario) + ".", "AMD Software → Gaming → Graphics: " + radeon, optional: true);
        }

        // ---- In-game settings: the biggest levers, which only the game controls ------------------------------------------
        if (gaming)
            Step(TuneArea.Games, ChangeSource.Game, "tune-in-game", "In-game settings", "Not visible to Hanki", "See the step",
                "Games override many driver settings, and some of the biggest wins only exist in the game.",
                scenario == TuneScenario.GamingPerformance
                    ? "Turn on NVIDIA Reflex (or AMD Anti-Lag 2) where offered, turn V-Sync off in the game, use fullscreen or borderless, and lower the heaviest settings (shadows, volumetrics) before resolution."
                    : "Turn V-Sync off in the game when the driver's V-Sync is on, and use DLSS, FSR or XeSS in Quality mode if you need more frames.", optional: true);

        // ---- Processor, memory and storage findings (steps: hardware, BIOS or Windows tools) ------------------------------
        foreach (var f in x.Findings) {
            if (f.FindingId is "processor-maximum" or "power-mode" or "power-plan") continue;
            if (lowPower && f.FindingId is "memory-speed" or "core-parking") continue;
            bool background = f.Metadata.GetValueOrDefault("source") == "Background";
            // Busy programs, recorders and a second frame limiter matter while you game.
            if (background && !gaming) continue;
            var area = background ? TuneArea.Background : f.ModuleId switch { "perf-cpu" => TuneArea.Processor, "perf-memory" => TuneArea.Memory, "perf-storage" => TuneArea.Storage, _ => TuneArea.Windows };
            if (f.Severity is FindingSeverity.Warning or FindingSeverity.Critical)
                Step(area, ChangeSource.Windows, f.FindingId, f.Title, f.Metadata.GetValueOrDefault("current", "?"), f.Metadata.GetValueOrDefault("recommended", "?"), f.Explanation,
                    f.Recommendation ?? $"Open {area} for details.", f.Metadata.GetValueOrDefault("settings"), optional: background);
            else if (f.Severity == FindingSeverity.Healthy) Good(area, f.Title, f.Metadata.GetValueOrDefault("current", "OK"));
        }

        // One change per setting: the first wins (a display-mode change and a finding for the same display can't both apply).
        var unique = items.GroupBy(i => i.Change.HankiApplies ? $"{i.Change.Kind}|{i.Change.Target}" : "step|" + i.Change.Id).Select(g => g.First()).ToArray();
        return new(scenario, sync, unique, good);
    }
}
