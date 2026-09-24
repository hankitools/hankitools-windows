using System.Globalization;
using System.Text.RegularExpressions;
namespace IgezziGuard;

public sealed record CpuFacts(string Name, int Cores, int Threads, int BaseMhz);
/// <param name="RatedMts">What the module's SPD reports to Windows (often the standard JEDEC speed, not the XMP/EXPO profile).</param>
public sealed record MemoryModule(string Slot, ulong Bytes, int RatedMts, int ConfiguredMts, string Manufacturer, string PartNumber, int Type);
/// <param name="Automatic">"Automatically manage paging file size for all drives".</param>
public sealed record PagefileFacts(bool Automatic, IReadOnlyList<(string Path, int InitialMb, int MaximumMb)> Configured, IReadOnlyList<(string Path, int AllocatedMb, int PeakMb)> Active, int CrashDumpMode);
public sealed record MemoryFacts(ulong InstalledBytes, int Slots, IReadOnlyList<MemoryModule> Modules, ulong CommitBytes, ulong CommitLimitBytes, ulong CommitPeakBytes, ulong AvailableBytes, PagefileFacts Pagefile,
    IReadOnlyList<(string Name, ulong PrivateBytes)> TopProcesses);
public sealed record DiskFacts(int Number, string Name, string Media, string Bus, ulong Size, string Health, double? TemperatureC, int? WearPercent);
public sealed record VolumeFacts(char Letter, int DiskNumber, string FileSystem, ulong Size, ulong Free);
/// <param name="TrimDisabled">NTFS DisableDeleteNotification; null when Windows doesn't say.</param>
public sealed record StorageFacts(IReadOnlyList<DiskFacts> Disks, IReadOnlyList<VolumeFacts> Volumes, bool? TrimDisabled, string? OptimizeTaskState, DateTimeOffset? OptimizeLastRun,
    int WindowsBuild, bool? DirectX12Ready, IReadOnlyList<string> Notes)
{
    public DiskFacts? DiskFor(char letter) => Volumes.FirstOrDefault(v => char.ToUpperInvariant(v.Letter) == char.ToUpperInvariant(letter)) is { } v ? Disks.FirstOrDefault(d => d.Number == v.DiskNumber) : null;
}

/// <summary>
/// CPU, memory and storage rules for Hanki Performance (HANKI-PERF-302/303/304/305/306/307/308). Like the gaming rules,
/// they only fire on what Windows reported, word opportunities as observations, and say plainly what they can't tell.
/// </summary>
public static partial class SystemAnalyzers
{
    private static DiagnosticResult Result(string module, string id, FindingSeverity severity, string title, string explanation, string current, string recommended,
        string remedy, DateTimeOffset now, string? recommendation = null, IReadOnlyDictionary<string, string>? extra = null, FindingConfidence confidence = FindingConfidence.Confirmed)
    {
        var metadata = new Dictionary<string, string> { ["current"] = current, ["recommended"] = recommended, ["remedy"] = remedy };
        foreach (var pair in extra ?? new Dictionary<string, string>()) metadata[pair.Key] = pair.Value;
        return new(module, id, DiagnosticCategory.Performance, CollectionOutcome.Completed, severity, title, explanation, now, now,
            evidence: $"Current: {current}\nRecommended: {recommended}", confidence: confidence, recommendation: recommendation, metadata: metadata);
    }
    private static string GB(ulong bytes) => $"{bytes / (1024d * 1024 * 1024):0.#} GB";

    // ---- CPU (HANKI-PERF-302) ------------------------------------------------------------------------------------
    public static IReadOnlyList<DiagnosticResult> Cpu(CpuFacts cpu, WindowsGamingSettings power, bool? portable, bool onAc, DateTimeOffset now)
    {
        var r = new List<DiagnosticResult> {
            Result("perf-cpu", "processor", FindingSeverity.Informational, cpu.Name, $"{cpu.Cores} cores, {cpu.Threads} threads, base clock {cpu.BaseMhz / 1000.0:0.0#} GHz.", cpu.Name, cpu.Name, GamingHealth.RemedyNone, now)
        };
        if (power.ProcessorMaximumAc is { } max && max < 100)
            r.Add(Result("perf-cpu", "processor-maximum", FindingSeverity.Warning, $"Processor is limited to {max}% while plugged in",
                $"The power plan “{power.PowerPlanName ?? "active plan"}” caps the processor at {max}% of its speed on mains power, which also blocks boost clocks.",
                $"{max}%", "100%", GamingHealth.RemedyProcessor, now, "Set it to 100% for mains power. Battery settings aren't changed.", new Dictionary<string, string> { ["plan"] = power.PowerPlan?.ToString() ?? "" }));
        else if (power.ProcessorMaximumAc is not null)
            r.Add(Result("perf-cpu", "processor-maximum", FindingSeverity.Healthy, "Processor can reach full speed", "The maximum processor state is 100% on mains power, so boost clocks are available.", "100%", "100%", GamingHealth.RemedyNone, now));
        if (portable == true && power.ProcessorMaximumDc is { } dc && dc < 100)
            r.Add(Result("perf-cpu", "processor-battery", FindingSeverity.Informational, $"On battery the processor is limited to {dc}%", "This saves battery and is normal; it doesn't apply when plugged in.", $"{dc}%", "No change", GamingHealth.RemedyNone, now));
        if (power.ProcessorMinimumAc == 100)
            r.Add(Result("perf-cpu", "processor-minimum", FindingSeverity.Informational, "Processor never slows down when idle",
                "The minimum processor state is 100%, so the processor stays at full clock even at the desktop. Games gain nothing from it; it uses more power and adds heat. No change is required, but 5% is Windows' usual value.",
                "100%", "5% (usual)", GamingHealth.RemedyNone, now));
        if (onAc && power.PowerMode == "Best power efficiency")
            r.Add(Result("perf-cpu", "power-mode", FindingSeverity.Warning, "Windows power mode favours battery life while plugged in", "“Best power efficiency” lowers processor performance to save energy.",
                power.PowerMode, "Balanced or Best performance", GamingHealth.RemedySettings, now, "Change it in Settings → System → Power & battery → Power mode.", new Dictionary<string, string> { ["settings"] = "ms-settings:powersleep" }));
        r.Add(Result("perf-cpu", "core-parking", FindingSeverity.Informational, "Core parking and hidden processor settings",
            "No change recommended. Windows parks idle cores to save power and unparks them within milliseconds under load; forcing every core active doesn't raise frame rates on a healthy system.",
            "Windows default", "No change", GamingHealth.RemedyNone, now));
        return r;
    }

    // ---- Memory (HANKI-PERF-303/304/305) ---------------------------------------------------------------------------------
    [GeneratedRegex(@"(?<![0-9])(2400|2666|2933|3000|3200|3466|3600|3733|3800|4000|4133|4266|4400|4600|4800|5200|5600|6000|6200|6400|6600|6800|7000|7200|7600|8000|8200|8400)(?![0-9])")]
    private static partial Regex SpeedInPartNumber();
    /// <summary>A profile speed some vendors print in the part number (G.Skill F5-6000…, Corsair …B6000C36). A hint only, never proof.</summary>
    public static int? PartNumberSpeed(string partNumber) => SpeedInPartNumber().Match(partNumber.Replace(" ", "")) is { Success: true } m ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null;
    public static string MemoryType(int smbios) => smbios switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", 30 => "LPDDR4", 35 => "LPDDR5", _ => "RAM" };

    public static IReadOnlyList<DiagnosticResult> Memory(MemoryFacts m, DateTimeOffset now)
    {
        var r = new List<DiagnosticResult>();
        var modules = m.Modules.Where(x => x.Bytes > 0).ToArray();
        string type = modules.Length > 0 ? MemoryType(modules[0].Type) : "RAM";
        int configured = modules.Select(x => x.ConfiguredMts).Where(x => x > 0).DefaultIfEmpty(0).Min();
        r.Add(Result("perf-memory", "installed", FindingSeverity.Informational, $"{GB(m.InstalledBytes)} {type}",
            modules.Length == 0 ? "Windows didn't report individual memory modules." : $"{modules.Length} {(modules.Length == 1 ? "module" : "modules")} in {Math.Max(m.Slots, modules.Length)} slots, running at {configured} MT/s: " +
            string.Join("; ", modules.Select(x => $"{x.Slot} {GB(x.Bytes)} {x.Manufacturer} {x.PartNumber}".Trim())), GB(m.InstalledBytes), GB(m.InstalledBytes), GamingHealth.RemedyNone, now));

        // XMP/EXPO: only when Windows' own rated speed is clearly above the running speed.
        var slower = modules.Where(x => x.RatedMts > 0 && x.ConfiguredMts > 0 && x.RatedMts >= x.ConfiguredMts * 1.1).ToArray();
        var hinted = modules.Where(x => x.ConfiguredMts > 0 && PartNumberSpeed(x.PartNumber) is { } p && p >= x.ConfiguredMts * 1.1).ToArray();
        if (slower.Length > 0)
            r.Add(Result("perf-memory", "memory-speed", FindingSeverity.Warning, "Memory runs below its rated speed",
                $"The modules report {slower.Max(x => x.RatedMts)} MT/s but run at {configured} MT/s. The memory profile (XMP or EXPO) is probably not enabled in the BIOS.",
                $"{configured} MT/s", $"{slower.Max(x => x.RatedMts)} MT/s", GamingHealth.RemedyHardware, now,
                "Enable XMP (Intel) or EXPO (AMD) in the BIOS/UEFI setup. Hanki doesn't change firmware settings. If the PC becomes unstable, turn it off again."));
        else if (hinted.Length > 0)
            r.Add(Result("perf-memory", "memory-speed", FindingSeverity.Informational, "Memory may be running below its rated profile",
                $"The module part number ({hinted[0].PartNumber}) suggests a {PartNumberSpeed(hinted[0].PartNumber)} MT/s profile, but it runs at {configured} MT/s. Part numbers aren't reliable, so this is a hint: check the memory profile (XMP or EXPO) in the BIOS.",
                $"{configured} MT/s", $"Possibly {PartNumberSpeed(hinted[0].PartNumber)} MT/s", GamingHealth.RemedyHardware, now, confidence: FindingConfidence.Possible));
        if (modules.Length == 1 && m.Slots >= 2)
            r.Add(Result("perf-memory", "channels", FindingSeverity.Informational, "One memory module",
                "A single module runs in single-channel mode. Two matching modules (dual channel) give roughly twice the memory bandwidth, which helps integrated graphics and some games.",
                "1 module", "2 matching modules", GamingHealth.RemedyHardware, now));

        double commit = m.CommitLimitBytes == 0 ? 0 : 100.0 * m.CommitBytes / m.CommitLimitBytes, peak = m.CommitLimitBytes == 0 ? 0 : 100.0 * m.CommitPeakBytes / m.CommitLimitBytes;
        if (commit >= 90)
            r.Add(Result("perf-memory", "commit", FindingSeverity.Warning, $"Memory is {commit:0}% committed",
                $"Programs have reserved {GB(m.CommitBytes)} of the {GB(m.CommitLimitBytes)} Windows can back with RAM and the pagefile. Near the limit, programs can fail to start or crash, and games stutter while Windows pages memory. Biggest users now: " +
                string.Join(", ", m.TopProcesses.Take(4).Select(p => $"{p.Name} {GB(p.PrivateBytes)}")) + ".",
                $"{commit:0}%", "Below 80%", GamingHealth.RemedyNone, now, "Close programs you aren't using, keep the pagefile system-managed, and consider more RAM if this is normal for you. Hanki doesn't close programs."));
        else
            r.Add(Result("perf-memory", "commit", FindingSeverity.Healthy, "Memory headroom", $"{commit:0}% of the memory Windows can back is in use (peak since start {peak:0}%).", $"{commit:0}%", "Below 80%", GamingHealth.RemedyNone, now));

        // Pagefile (HANKI-PERF-305). System-managed is the normal default; no size formula is used.
        var p = m.Pagefile;
        if (p.Automatic)
            r.Add(Result("perf-memory", "pagefile", FindingSeverity.Healthy, "Pagefile is system-managed", "Windows sizes the pagefile itself, which is the recommended setting.", "System-managed", "System-managed", GamingHealth.RemedyNone, now));
        else if (p.Configured.Count == 0 && p.Active.Count == 0)
            r.Add(Result("perf-memory", "pagefile", FindingSeverity.Warning, "The pagefile is turned off",
                $"Without a pagefile, the memory Windows can hand out is limited to the {GB(m.InstalledBytes)} of RAM. When it runs out, programs and games crash instead of slowing down" +
                (p.CrashDumpMode != 0 ? ", and Windows can't save crash dumps for troubleshooting." : "."),
                "Off", "System-managed", GamingHealth.RemedySettings, now, "Turn it back on: System Properties → Advanced → Performance → Settings → Advanced → Virtual memory → Automatically manage.", new Dictionary<string, string> { ["settings"] = "pagefile" }));
        else if (p.Configured.Any(c => c.MaximumMb > 0) && m.CommitLimitBytes > 0 && m.CommitPeakBytes >= m.CommitLimitBytes * 0.9)
            r.Add(Result("perf-memory", "pagefile", FindingSeverity.Warning, "The pagefile has a fixed size that's nearly used up",
                $"A custom pagefile size is set, and memory use has reached {peak:0}% of the limit it allows. A system-managed pagefile can grow when needed.",
                string.Join(", ", p.Configured.Select(c => $"{c.Path} {c.InitialMb}–{c.MaximumMb} MB")), "System-managed", GamingHealth.RemedySettings, now,
                "Choose Automatically manage in the Virtual memory settings.", new Dictionary<string, string> { ["settings"] = "pagefile" }));
        else
            r.Add(Result("perf-memory", "pagefile", FindingSeverity.Informational, "Pagefile has a custom size",
                $"{string.Join(", ", p.Configured.Select(c => c.MaximumMb == 0 ? $"{c.Path} (system-managed on this drive)" : $"{c.Path} {c.InitialMb}–{c.MaximumMb} MB"))}. Memory use has peaked at {peak:0}% of the limit, so it isn't holding anything back now.",
                "Custom", "System-managed is Windows' default", GamingHealth.RemedyNone, now));
        r.Add(Result("perf-memory", "memory-cleaners", FindingSeverity.Informational, "Memory cleaners and standby purging",
            "No change recommended. Windows uses spare RAM as a cache and frees it instantly when a game needs it; clearing it just makes Windows reload files from disk.",
            "Windows default", "No change", GamingHealth.RemedyNone, now));
        return r;
    }

    // ---- Storage (HANKI-PERF-306/307/308) ---------------------------------------------------------------------------------
    public static string MediaText(DiskFacts d) => d.Bus.Equals("NVMe", StringComparison.OrdinalIgnoreCase) ? "NVMe SSD"
        : d.Media.Equals("SSD", StringComparison.OrdinalIgnoreCase) ? $"{(d.Bus.Length > 0 ? d.Bus + " " : "")}SSD" : d.Media.Equals("HDD", StringComparison.OrdinalIgnoreCase) ? "hard disk" : "unknown drive type";
    public static bool IsHdd(DiskFacts d) => d.Media.Equals("HDD", StringComparison.OrdinalIgnoreCase);
    public static bool IsSsd(DiskFacts d) => d.Media.Equals("SSD", StringComparison.OrdinalIgnoreCase) || d.Bus.Equals("NVMe", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<DiagnosticResult> Storage(StorageFacts s, IReadOnlyList<GameEntry> games, char systemDrive, DateTimeOffset now)
    {
        var r = new List<DiagnosticResult>();
        foreach (var d in s.Disks)
            r.Add(Result("perf-storage", $"disk-{d.Number}", d.Health is "Healthy" or "" ? FindingSeverity.Informational : FindingSeverity.Warning,
                $"{d.Name} ({MediaText(d)}, {GB(d.Size)})",
                (d.Health is "Healthy" or "" ? "Windows reports it as healthy" : $"Windows reports its health as {d.Health}; back up important files and check the drive") +
                (d.TemperatureC is { } t ? $" · {t:0} °C" : "") + (d.WearPercent is { } w ? $" · {w}% worn" : "") + ". Drives: " +
                string.Join(", ", s.Volumes.Where(v => v.DiskNumber == d.Number).Select(v => $"{v.Letter}: {GB(v.Free)} free of {GB(v.Size)}")),
                d.Health, "Healthy", GamingHealth.RemedyNone, now));
        foreach (var v in s.Volumes) {
            double freeShare = v.Size == 0 ? 1 : (double)v.Free / v.Size;
            bool system = char.ToUpperInvariant(v.Letter) == char.ToUpperInvariant(systemDrive);
            if (freeShare < 0.1 || system && v.Free < 20UL * 1024 * 1024 * 1024)
                r.Add(Result("perf-storage", $"space-{v.Letter}", FindingSeverity.Warning, $"Drive {v.Letter}: is nearly full",
                    $"{GB(v.Free)} free of {GB(v.Size)}.{(system ? " Windows needs room on the system drive for updates, the pagefile and temporary files." : " Games need room to patch and stream data.")}",
                    $"{GB(v.Free)} free", system ? "At least 20 GB free" : "At least 10% free", GamingHealth.RemedySettings, now, "Free up space in System → Maintain → Files & storage.", new Dictionary<string, string> { ["drive"] = v.Letter.ToString() }));
        }
        // Games on a hard disk while an SSD has room (HANKI-PERF-306).
        var ssdRoom = s.Volumes.Where(v => s.Disks.FirstOrDefault(d => d.Number == v.DiskNumber) is { } d && IsSsd(d) && v.Free > 50UL * 1024 * 1024 * 1024).ToArray();
        foreach (var game in games.Where(g => !g.Hidden && g.Executable.Length > 2 && g.Executable[1] == ':')) {
            var disk = s.DiskFor(game.Executable[0]);
            if (disk is null || !IsHdd(disk)) continue;
            r.Add(Result("perf-storage", "game-hdd:" + game.Name.ToLowerInvariant(), FindingSeverity.Warning, $"{game.Name} is installed on a hard disk",
                $"It's on {game.Executable[0]}: ({disk.Name}, a hard disk). Games on hard disks load slower and can stutter while streaming data; how much depends on the game." +
                (ssdRoom.Length > 0 ? $" {string.Join(", ", ssdRoom.Select(v => $"{v.Letter}:"))} is an SSD with room." : ""),
                "Hard disk", "SSD, ideally NVMe", GamingHealth.RemedyHardware, now, "Move it with the game launcher's own move or install-location option; don't copy game folders by hand."));
        }
        // TRIM and Windows drive optimization (HANKI-PERF-307).
        bool anySsd = s.Disks.Any(IsSsd);
        if (anySsd && s.TrimDisabled == true)
            r.Add(Result("perf-storage", "trim", FindingSeverity.Warning, "TRIM is turned off",
                "Windows isn't telling the SSD which blocks are free, which slows writes over time and adds wear. TRIM is on by default.",
                "Off", "On", GamingHealth.RemedySettings, now, "An administrator can turn it back on with: fsutil behavior set DisableDeleteNotify 0"));
        else if (anySsd && s.TrimDisabled == false)
            r.Add(Result("perf-storage", "trim", FindingSeverity.Healthy, "TRIM is on", "Windows tells your SSDs which blocks are free, as it should.", "On", "On", GamingHealth.RemedyNone, now));
        if (s.OptimizeTaskState is "Disabled")
            r.Add(Result("perf-storage", "optimize", FindingSeverity.Warning, "Windows drive optimization is turned off",
                "The scheduled Optimize Drives task is disabled, so SSDs don't get their regular ReTrim and hard disks aren't defragmented.",
                "Disabled", "Scheduled (weekly)", GamingHealth.RemedySettings, now, "Turn it back on in Optimize Drives → Change settings → Run on a schedule."));
        else if (s.OptimizeLastRun is { } last && now - last > TimeSpan.FromDays(30))
            r.Add(Result("perf-storage", "optimize", FindingSeverity.Informational, "Drive optimization hasn't run for a while",
                $"Windows last optimized the drives on {last.ToLocalTime():d}. It runs when the PC is idle, so a PC that's always busy or off may skip it.",
                last.ToLocalTime().ToString("d"), "Within the last month", GamingHealth.RemedyNone, now));
        r.Add(Result("perf-storage", "defrag-ssd", FindingSeverity.Informational, "SSD “optimizers”",
            "No change recommended. SSDs don't need defragmenting; Windows sends them ReTrim instead. Tools that defragment SSDs add wear without speeding up games.",
            "Windows default", "No change", GamingHealth.RemedyNone, now));
        return r;
    }

    /// <summary>DirectStorage readiness (HANKI-PERF-308): the system's capability, never a promise that a game uses it.</summary>
    public static IReadOnlyList<string> DirectStorage(StorageFacts s, IReadOnlyList<GameEntry> games)
    {
        bool windows = s.WindowsBuild >= 19041, windows11 = s.WindowsBuild >= 22000, nvme = s.Disks.Any(d => d.Bus.Equals("NVMe", StringComparison.OrdinalIgnoreCase));
        var lines = new List<string> {
            $"Windows: {(windows11 ? "ready (Windows 11 has the fastest storage path)" : windows ? "supported (Windows 10; Windows 11 is faster)" : "too old")}",
            $"Graphics: {(s.DirectX12Ready is true ? "DirectX 12 ready" : s.DirectX12Ready is false ? "no DirectX 12 support found" : "unknown")}",
            $"NVMe SSD: {(nvme ? "yes" : "no NVMe drive found")}"
        };
        foreach (var game in games.Where(g => !g.Hidden && g.Executable.Length > 2 && g.Executable[1] == ':').Take(30)) {
            var disk = s.DiskFor(game.Executable[0]);
            lines.Add($"{game.Name}: installed on {(disk is null ? "an unknown drive" : MediaText(disk))}");
        }
        lines.Add(windows && nvme && s.DirectX12Ready == true
            ? "This PC meets the main requirements for DirectStorage games. Whether a game uses DirectStorage is up to the game; this doesn't mean any game will load faster."
            : "This PC doesn't meet all the main requirements for DirectStorage. Games still work; they just use the older loading path.");
        return lines;
    }
}
