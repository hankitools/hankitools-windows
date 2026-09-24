namespace IgezziGuard;

/// <summary>
/// Gaming Health Scan rules (HANKI-GAME-201, HANKI-GPU-104, HANKI-GPU-108). Read-only: turns what Windows and the
/// graphics driver report into findings in the common result model. Every finding records the current state, the
/// recommended state, the impact and how it can be changed (metadata "current", "recommended", "impact", "remedy",
/// "source"). Rules only fire on configuration Hanki has actually read (HANKI-GAME-211); a PC without problems
/// gets "looks OK" results, not invented warnings.
/// </summary>
public static class GamingHealth
{
    public const string ModuleId = "gaming";
    /// <summary>How a finding can be changed. Only display-mode, gpu-preference and processor-power are applied by Hanki.</summary>
    public const string RemedyDisplayMode = "display-mode", RemedyGpuPreference = "gpu-preference", RemedyProcessor = "processor-power",
        RemedySettings = "settings", RemedyHardware = "hardware", RemedyNone = "none";
    public static readonly Guid PowerSaverPlan = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    public static IReadOnlyList<DiagnosticResult> Evaluate(GraphicsInventory graphics, WindowsGamingSettings windows, NvidiaProfileView? nvidiaGlobal, DateTimeOffset now)
    {
        var results = new List<DiagnosticResult>();
        void Add(string id, FindingSeverity severity, string title, string explanation, string current, string recommended, string impact, string remedy, string source,
            string? recommendation = null, IReadOnlyDictionary<string, string>? extra = null)
        {
            var metadata = new Dictionary<string, string> { ["current"] = current, ["recommended"] = recommended, ["impact"] = impact, ["remedy"] = remedy, ["source"] = source };
            foreach (var pair in extra ?? new Dictionary<string, string>()) metadata[pair.Key] = pair.Value;
            results.Add(new DiagnosticResult(ModuleId, id, DiagnosticCategory.Performance, CollectionOutcome.Completed, severity, title, explanation, now, now,
                evidence: $"Current: {current}\nRecommended: {recommended}", confidence: FindingConfidence.Confirmed, recommendation: recommendation, metadata: metadata));
        }

        // Displays (HANKI-GAME-207).
        int index = 0;
        foreach (var display in graphics.Displays) {
            index++;
            var current = GraphicsFacts.Describe(display.Current);
            var extra = new Dictionary<string, string> { ["device"] = display.Device };
            if (GraphicsFacts.FasterRefresh(display) is { } faster) {
                extra["width"] = faster.Width.ToString(); extra["height"] = faster.Height.ToString(); extra["refresh"] = Math.Round(faster.RefreshHz).ToString();
                Add($"refresh-{index}", FindingSeverity.Warning, $"{display.Name} runs below its fastest refresh rate",
                    $"Your display supports {Math.Round(faster.RefreshHz):0} Hz at {faster.Width} × {faster.Height}, but Windows uses {Math.Round(display.Current.RefreshHz):0} Hz. Games can't show more frames than the display refreshes, and motion looks less smooth.",
                    current, GraphicsFacts.Describe(faster), "High", RemedyDisplayMode, "Display", "Switch to the faster refresh rate. Windows asks you to keep the change, and puts it back if you don't.", extra);
            } else {
                Add($"refresh-{index}", FindingSeverity.Healthy, $"{display.Name} refresh rate",
                    $"The display runs at its fastest refresh rate for this resolution ({Math.Round(display.Current.RefreshHz):0} Hz).", current, current, "None", RemedyNone, "Display", extra: extra);
            }
            // A desktop monitor plugged into the motherboard while a graphics card is installed.
            if (graphics.Portable == false && graphics.AdapterFor(display) is { LikelyIntegrated: true } integrated && graphics.Adapters.FirstOrDefault(a => !a.LikelyIntegrated) is { } dedicated)
                Add($"display-port-{index}", FindingSeverity.Warning, $"{display.Name} is connected to the integrated graphics",
                    $"The display is driven by {integrated.Name}, not the graphics card ({dedicated.Name}). Games can still render on the card, but copying every frame to the motherboard's output costs performance and can add latency.",
                    integrated.Name, dedicated.Name, "High", RemedyHardware, "Display", $"Plug the monitor cable into the {dedicated.Name}'s outputs on the back of the PC.");
        }

        // Per-app GPU choices only matter when there is more than one GPU to choose from.
        if (graphics.Adapters.Count > 1)
            foreach (var app in windows.GpuPreferences.Where(p => p.Preference == GpuPreference.PowerSaving)) {
                string exe = Path.GetFileName(app.Application);
                Add("gpu-preference:" + exe.ToLowerInvariant(), FindingSeverity.Warning, $"{exe} is set to use the power-saving GPU",
                    $"Windows Graphics settings tell {exe} to use the power-saving (usually integrated) GPU, so a game runs much slower than on the graphics card.",
                    WindowsGamingParsing.PreferenceText(app.Preference), WindowsGamingParsing.PreferenceText(GpuPreference.HighPerformance), "High", RemedyGpuPreference, "Windows",
                    "Set it to High performance. Hanki records the current choice so you can undo it in Recovery.", new Dictionary<string, string> { ["application"] = app.Application });
            }

        // Game Mode (HANKI-GPU-108). Missing means never changed: on by default.
        if (windows.GameMode == false)
            Add("game-mode", FindingSeverity.Warning, "Windows Game Mode is off",
                "Game Mode lets Windows give games priority and holds back Windows Update installs and some background work while you play.",
                "Off", "On", "Medium", RemedySettings, "Windows", "Turn it on in Settings → Gaming → Game Mode.", new Dictionary<string, string> { ["settings"] = "ms-settings:gaming-gamemode" });
        else
            Add("game-mode", FindingSeverity.Healthy, "Windows Game Mode", "Game Mode is on" + (windows.GameMode is null ? " (the Windows default)." : "."),
                "On", "On", "None", RemedyNone, "Windows");

        // Power (HANKI-PERF-302): only while plugged in; on battery, saving power is the point.
        if (graphics.OnAcPower) {
            if (windows.PowerMode == "Best power efficiency")
                Add("power-mode", FindingSeverity.Warning, "Windows power mode favours battery life while plugged in",
                    "“Best power efficiency” lowers processor and graphics performance to save energy. While plugged in, Balanced or Best performance lets games run at full speed.",
                    windows.PowerMode, "Balanced or Best performance", "Medium", RemedySettings, "Windows", "Change it in Settings → System → Power & battery → Power mode.",
                    new Dictionary<string, string> { ["settings"] = "ms-settings:powersleep" });
            if (windows.ProcessorMaximumAc is { } maximum && maximum < 100)
                Add("processor-maximum", FindingSeverity.Warning, $"Processor is limited to {maximum}% while plugged in",
                    $"The power plan “{windows.PowerPlanName ?? "active plan"}” caps the processor at {maximum}% of its speed on mains power, which also blocks boost clocks in demanding games.",
                    $"{maximum}%", "100%", "Medium", RemedyProcessor, "Windows", "Set the maximum processor state to 100% for mains power. Hanki records the current value in Recovery.",
                    new Dictionary<string, string> { ["plan"] = windows.PowerPlan?.ToString() ?? "" });
            if (windows.PowerPlan == PowerSaverPlan && graphics.Portable == false)
                Add("power-plan", FindingSeverity.Warning, "The Power saver plan is active",
                    "Power saver lowers processor performance on a desktop PC, where saving battery doesn't apply.",
                    "Power saver", "Balanced", "Medium", RemedySettings, "Windows", "Choose Balanced under CPU → Power plans.");
        }

        // NVIDIA global settings (HANKI-GPU-102): per-game changes are preferred, so these are observations.
        if (nvidiaGlobal is not null) {
            var power = nvidiaGlobal.Values.FirstOrDefault(v => v.Setting.Id == NvidiaSettings.PowerManagementId);
            if (power?.Value == NvidiaSettings.PowerPreferMinimum)
                Add("nvidia-power", FindingSeverity.Warning, "NVIDIA global power mode favours power saving",
                    "The global NVIDIA setting “Prefer maximum power savings” keeps the graphics card at lower clocks in every game.",
                    power.Text, "Normal (driver default)", "Medium", RemedySettings, "NVIDIA", "Set Power management mode back to Normal in NVIDIA Control Panel → Manage 3D settings.");
            else if (power?.Value == NvidiaSettings.PowerPreferMaximum)
                Add("nvidia-power", FindingSeverity.Informational, "NVIDIA keeps the GPU at high clocks for everything",
                    "“Prefer maximum performance” is set globally, so the graphics card also stays clocked up at the desktop and in video, using more power. Setting it per game gives the same benefit in games.",
                    power.Text, "Normal globally, and per game where it helps", "Low", RemedyNone, "NVIDIA");
            else if (power is { Value: not null })
                Add("nvidia-power", FindingSeverity.Healthy, "NVIDIA power management mode", $"{power.Text} ({NvidiaSettings.SourceText(power.Source)}).", power.Text, power.Text, "None", RemedyNone, "NVIDIA");
            var limit = nvidiaGlobal.Values.FirstOrDefault(v => v.Setting.Id == NvidiaSettings.FrameRateLimitId);
            if (limit?.Value is > 0 and var fps) {
                double fastest = graphics.Displays.Select(d => d.Current.RefreshHz).DefaultIfEmpty(0).Max();
                Add("nvidia-fps-cap", FindingSeverity.Informational, $"NVIDIA caps every game at {fps} FPS",
                    fastest > 0 && fps < fastest * 0.9
                        ? $"The global frame-rate limit ({fps} FPS) is below your display's {Math.Round(fastest):0} Hz, so games can't use the full refresh rate. That may be deliberate, for example to reduce heat."
                        : $"A global frame-rate limit of {fps} FPS applies to every game. Games with their own limiter may end up with two.",
                    $"{fps} FPS", "One deliberate limiter", "Low", RemedyNone, "NVIDIA");
            }
        }

        // Evidence rule (HANKI-GAME-211): a setting people are often told to flip, where Hanki recommends nothing.
        Add("hags", FindingSeverity.Informational, "Hardware-accelerated GPU scheduling",
            $"{(windows.HardwareScheduling is { } on ? (on ? "On" : "Off") : "Windows default")}. No change recommended: its effect depends on the game and driver, and there's no evidence it helps on this PC.",
            windows.HardwareScheduling is { } h ? (h ? "On" : "Off") : "Windows default", "No change", "None", RemedyNone, "Windows");

        if (graphics.HighPerformance is { } gpu && gpu.DriverDate is { } date && now.UtcDateTime - date > TimeSpan.FromDays(365))
            Add("driver-age", FindingSeverity.Informational, "The graphics driver is over a year old",
                $"{gpu.Name} uses a driver from {date:d}. Newer drivers often include fixes and optimizations for recent games.",
                date.ToString("d"), "A current driver from the manufacturer", "Low", RemedyNone, "Driver");
        return results;
    }

    public static bool CanApply(DiagnosticResult r) => r.Metadata.GetValueOrDefault("remedy") is RemedyDisplayMode or RemedyGpuPreference or RemedyProcessor;
    /// <summary>Only high-impact problems belong in Fix My PC (HANKI-GAME-214); the rest stay in Hanki Performance.</summary>
    public static bool BelongsInFixMyPc(DiagnosticResult r) => r.Severity is FindingSeverity.Warning or FindingSeverity.Critical && r.Metadata.GetValueOrDefault("impact") == "High";
}
