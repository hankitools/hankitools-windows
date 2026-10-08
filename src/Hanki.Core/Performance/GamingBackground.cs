using System.Globalization;
namespace IgezziGuard;

/// <summary>A running program, grouped by executable name (without .exe), from one short sample.</summary>
public sealed record RunningApp(string Name, int Processes, double CpuPercent, long MemoryMb);
/// <param name="RtssLimit">RivaTuner Statistics Server's global frame limit (FPS), when its profile says so; 0 means no limit.</param>
public sealed record BackgroundSnapshot(IReadOnlyList<RunningApp> Apps, double? RtssLimit);

/// <summary>
/// Overlays, recorders, frame limiters and busy background programs while you game (HANKI-GAME-218). Read-only:
/// what runs is reported with what it does and what to try; nothing is closed or changed. A program being listed
/// isn't a fault; overlays are a common suspect when one game stutters or crashes, and busy programs compete with
/// the game for the processor.
/// </summary>
public static class GamingBackground
{
    public enum Kind { Limiter, Overlay, Recorder }
    public sealed record Known(string Name, Kind Kind, string Note, params string[] Processes);

    /// <summary>Programs recognised by executable name (case-insensitive, without .exe).</summary>
    public static readonly IReadOnlyList<Known> Catalog = [
        new("RivaTuner Statistics Server", Kind.Limiter, "Its frame limiter and on-screen display hook into every game.", "RTSS"),
        new("MSI Afterburner", Kind.Overlay, "Its monitoring overlay runs through RivaTuner; its fan and clock settings stay active while it runs.", "MSIAfterburner"),
        new("Discord", Kind.Overlay, "The in-game overlay hooks into games when it's turned on in Discord's settings.", "Discord"),
        new("Steam overlay", Kind.Overlay, "Steam draws its overlay inside games started from Steam.", "GameOverlayUI"),
        new("NVIDIA overlay", Kind.Overlay, "The NVIDIA app's overlay (Alt+Z) for recording, filters and statistics.", "NVIDIA Overlay"),
        new("Xbox Game Bar", Kind.Overlay, "Windows' Game Bar (Win+G) overlay.", "GameBar", "GameBarFTServer"),
        new("AMD Software overlay", Kind.Overlay, "AMD Software's in-game overlay (Alt+R) and metrics.", "RadeonSoftware", "AMDRSServ"),
        new("Overwolf", Kind.Overlay, "Hosts overlays for other apps inside games.", "Overwolf"),
        new("EA app", Kind.Overlay, "Draws its overlay in EA games.", "EADesktop"),
        new("Ubisoft Connect", Kind.Overlay, "Draws its overlay in Ubisoft games.", "upc", "UplayWebCore"),
        new("OBS Studio", Kind.Recorder, "Recording or streaming encodes video while you play.", "obs64", "obs32"),
        new("Streamlabs", Kind.Recorder, "Recording or streaming encodes video while you play.", "Streamlabs OBS", "Streamlabs Desktop"),
        new("Medal", Kind.Recorder, "Records clips in the background.", "Medal"),
        new("XSplit", Kind.Recorder, "Recording or streaming encodes video while you play.", "XSplit.Core", "XSplit.Gamecaster"),
    ];
    /// <summary>Windows and driver components that are always there; never reported as busy background programs.</summary>
    private static readonly HashSet<string> WindowsProcesses = new(StringComparer.OrdinalIgnoreCase) {
        "Idle", "System", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "explorer", "fontdrvhost",
        "sihost", "ctfmon", "RuntimeBroker", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "audiodg", "spoolsv", "conhost", "dllhost",
        "nvcontainer", "NVDisplay.Container", "atieclxx", "atiesrxx", "amdfendrsr", "WmiPrvSE", "MsMpEng", "NisSrv", "SecurityHealthService", "HankiTools", "Taskmgr"
    };
    /// <summary>A background program counts as busy above this share of all processor time in the sample.</summary>
    public const double BusyCpuPercent = 8;

    /// <param name="games">Executable names of your games (without .exe): a running game isn't a background program.</param>
    public static IReadOnlyList<DiagnosticResult> Evaluate(BackgroundSnapshot snapshot, DateTimeOffset now, IEnumerable<string>? games = null)
    {
        var yours = (games ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<DiagnosticResult>();
        void Add(string id, FindingSeverity severity, string title, string explanation, string current, string recommendation, string impact) =>
            results.Add(new DiagnosticResult(GamingHealth.ModuleId, id, DiagnosticCategory.Performance, CollectionOutcome.Completed, severity, title, explanation, now, now,
                evidence: current, confidence: FindingConfidence.Confirmed, recommendation: recommendation,
                metadata: new Dictionary<string, string> { ["current"] = current, ["recommended"] = recommendation, ["impact"] = impact, ["remedy"] = GamingHealth.RemedyNone, ["source"] = "Background" }));
        var apps = snapshot.Apps.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        RunningApp? Running(Known k) => k.Processes.Select(p => apps.GetValueOrDefault(p)).FirstOrDefault(a => a is not null);

        foreach (var known in Catalog) {
            if (Running(known) is not { } app) continue;
            string id = known.Kind switch { Kind.Limiter => "limiter:", Kind.Recorder => "recorder:", _ => "overlay:" } + known.Processes[0].ToLowerInvariant().Replace(' ', '-');
            if (known.Kind == Kind.Limiter && snapshot.RtssLimit is > 0 and var limit)
                Add(id, FindingSeverity.Warning, $"RivaTuner caps every game at {limit:0.#} FPS",
                    $"{known.Note} With a driver or in-game limit as well, the lowest one wins, and two limiters can make frame pacing uneven. With G-SYNC or FreeSync, one cap a few FPS below the refresh rate is enough.",
                    $"Running; global frame limit {limit:0.#} FPS", "Keep one deliberate frame limit: in the game, the driver or RivaTuner.", "Medium");
            else if (known.Kind == Kind.Recorder)
                Add(id, FindingSeverity.Warning, $"{known.Name} is running",
                    $"{known.Note} That costs some frames, mostly on the graphics card's encoder and the processor. If you're not recording, close it before playing.",
                    $"Running ({app.CpuPercent:0}% processor)", $"Close {known.Name} when you're not recording.", "Low");
            else
                Add(id, FindingSeverity.Informational, $"{known.Name} is running",
                    $"{known.Note} Overlays usually cost little, but they're a common cause of stutter or crashes in particular games. If a game misbehaves, try it with this overlay turned off.",
                    "Running", $"If a game stutters or crashes, turn off the {known.Name} overlay for it.", "Low");
        }

        var knownProcesses = Catalog.SelectMany(k => k.Processes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var busy = snapshot.Apps.Where(a => a.CpuPercent >= BusyCpuPercent && !knownProcesses.Contains(a.Name) && !WindowsProcesses.Contains(a.Name) && !yours.Contains(a.Name))
            .OrderByDescending(a => a.CpuPercent).Take(5).ToArray();
        foreach (var app in busy)
            Add("busy:" + app.Name.ToLowerInvariant(), FindingSeverity.Warning, $"{app.Name} is busy in the background",
                $"It used {app.CpuPercent:0}% of the processor during the check" + (app.MemoryMb >= 1024 ? $" and holds {app.MemoryMb / 1024.0:0.0} GB of memory" : "") +
                ". While you play, that competes with the game for processor time and can lower 1% lows.",
                $"{app.CpuPercent:0}% processor, {app.MemoryMb:N0} MB", $"Close or pause {app.Name} before playing if you don't need it.", "Medium");
        if (busy.Length == 0)
            Add("busy", FindingSeverity.Healthy, "Background programs", "No program outside Windows used much of the processor during the check.",
                "Quiet", "No change", "None");
        return results;
    }

    /// <summary>RivaTuner's frame limit from a profile file: "[Framerate]" with "Limit=141" (and optional "LimitDenominator"). 0 means no limit.</summary>
    public static double? RtssLimit(string profile)
    {
        bool section = false; double? limit = null, denominator = null;
        foreach (var raw in profile.Split('\n')) {
            var line = raw.Trim();
            if (line.StartsWith('[')) { section = line.Equals("[Framerate]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!section) continue;
            var pair = line.Split('=', 2);
            if (pair.Length != 2 || !double.TryParse(pair[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
            if (pair[0].Trim().Equals("Limit", StringComparison.OrdinalIgnoreCase)) limit = value;
            else if (pair[0].Trim().Equals("LimitDenominator", StringComparison.OrdinalIgnoreCase)) denominator = value;
        }
        if (limit is not { } l || l < 0 || !double.IsFinite(l)) return null;
        return denominator is > 1 and var d ? l / d : l;
    }
}

/// <summary>Reads running programs over a short sample, and RivaTuner's global profile when it's installed. Read-only.</summary>
internal static class BackgroundProbe
{
    internal static BackgroundSnapshot Collect(int milliseconds = 1000)
    {
        var before = Sample();
        Thread.Sleep(milliseconds);
        var after = Sample();
        double capacity = milliseconds * (double)Environment.ProcessorCount;
        var apps = after.GroupBy(p => p.Value.Name, StringComparer.OrdinalIgnoreCase).Select(g => {
            double cpu = g.Where(p => before.ContainsKey(p.Key) && before[p.Key].Name == p.Value.Name)
                .Sum(p => Math.Max(0, (p.Value.Cpu - before[p.Key].Cpu).TotalMilliseconds));
            return new RunningApp(g.Key, g.Count(), Math.Round(cpu / capacity * 100, 1), g.Sum(p => p.Value.Memory) / (1024 * 1024));
        }).ToArray();
        return new BackgroundSnapshot(apps, RtssGlobalLimit());
    }
    private static Dictionary<int, (string Name, TimeSpan Cpu, long Memory)> Sample()
    {
        var result = new Dictionary<int, (string, TimeSpan, long)>();
        foreach (var process in System.Diagnostics.Process.GetProcesses()) {
            try { result[process.Id] = (process.ProcessName, process.TotalProcessorTime, process.WorkingSet64); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            finally { process.Dispose(); }
        }
        return result;
    }
    private static double? RtssGlobalLimit()
    {
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }) {
            var folder = Environment.GetFolderPath(root);
            if (folder.Length == 0) continue;
            var profile = Path.Combine(folder, "RivaTuner Statistics Server", "Profiles", "Global");
            try { if (File.Exists(profile) && new FileInfo(profile).Length < 1_000_000) return GamingBackground.RtssLimit(File.ReadAllText(profile)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }
}
