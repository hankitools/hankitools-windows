using System.Globalization;
using System.Text.RegularExpressions;
namespace IgezziGuard;

/// <summary>Something the Home search can open: a guided fix for a symptom, or a tool page (by route name).</summary>
public sealed record SearchEntry(string Title, string Detail, string Keywords, bool GuidedFix, string Target);

/// <summary>
/// Home search: everyday words to guided fixes and tools. Every word must match; matches in the title rank above
/// matches in the keywords, and word starts above other matches. Guided fixes come first when scores tie.
/// </summary>
public static class HomeSearch
{
    // Everyday words for each symptom, so "laggy" finds "My PC is slow" and "bsod" finds the blue-screen fix.
    private static readonly (string Match, string Words)[] Synonyms = [
        ("blue screen", "bsod crash crashed crashes restart restarted restarts reboot reboots"),
        ("freezes", "freeze frozen hang hangs stuck unresponsive not responding"),
        ("is slow", "lag laggy sluggish speed performance slowdown"),
        ("battery", "drain drains draining charge charging laptop"),
        ("internet", "wifi wi-fi wireless network connection offline dns disconnects ping latency"),
        ("disk space", "storage full drive ssd hdd cleanup clean files space"),
        ("viruses", "virus malware antivirus defender security threat hacked scan"),
        ("Windows Update", "update updates stuck fails error install patch"),
        ("activated", "activation activate license licence product key genuine"),
    ];
    public static readonly IReadOnlyList<string> Suggestions = ["slow", "blue screen", "Wi-Fi", "FPS", "disk space", "undo"];

    public static SearchEntry Guide(int index, string symptom, int steps, IEnumerable<string> stepTitles) =>
        new(symptom, $"Guided fix · {steps} {(steps == 1 ? "step" : "steps")}", symptom + " " + string.Join(" ", stepTitles) + " " +
            string.Join(" ", Synonyms.Where(s => symptom.Contains(s.Match, StringComparison.OrdinalIgnoreCase)).Select(s => s.Words)), true, "guide:" + index.ToString(CultureInfo.InvariantCulture));

    /// <summary>A tool page from its route ("Diagnose  /  Crash timeline"): titled by its last part, with where it lives.</summary>
    public static SearchEntry Tool(string route, string keywords, string? description = null)
    {
        var parts = route.Split("  /  ");
        string detail = description ?? (parts.Length > 1 ? "In " + string.Join(" › ", parts[..^1]) : "Tool");
        return new(parts[^1], detail, keywords + " " + route, false, route);
    }

    public static IReadOnlyList<SearchEntry> Find(string query, IEnumerable<SearchEntry> entries, int limit = 6)
    {
        var words = query.Split(new[] { ' ', ',', '.', '?', '!', '/', ';', ':' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        static bool StartsWord(string text, string word) => Regex.IsMatch(text, @"(^|[^\p{L}\p{N}])" + Regex.Escape(word), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        int Score(SearchEntry e) {
            int total = 0;
            foreach (var w in words) {
                int best = StartsWord(e.Title, w) ? 4 : e.Title.Contains(w, StringComparison.OrdinalIgnoreCase) ? 3
                    : StartsWord(e.Keywords, w) ? 2 : e.Keywords.Contains(w, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                if (best == 0) return 0;
                total += best;
            }
            return total;
        }
        return entries.Select(e => (Entry: e, Score: Score(e))).Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Entry.GuidedFix ? 0 : 1).ThenBy(x => x.Entry.Title.Length)
            .Select(x => x.Entry).DistinctBy(e => e.Title, StringComparer.OrdinalIgnoreCase).Take(limit).ToArray();
    }
}

/// <summary>Windows Security Center's view of a protection (antivirus or firewall), whichever product provides it.</summary>
public enum SecurityHealth { Good, NotMonitored, Poor, Snoozed }

/// <summary>Quick read-only facts for Home's "Your PC at a glance". Null means Windows didn't say.</summary>
public sealed record GlanceFacts(string? Windows, string? Build, TimeSpan? Uptime, string SystemDrive, long? SystemFree, long? SystemTotal,
    long? MemoryUsed, long? MemoryTotal, string? Gpu, string? GpuDriver, SecurityHealth? Antivirus, SecurityHealth? Firewall);

/// <summary>One tile: what it is, the value, a line of context and a status color. Target is the page it opens.</summary>
public sealed record GlanceTileModel(string Icon, string Label, string Value, string Detail, CardStatus Status, int? Percent, string Target);

public static class PcGlance
{
    /// <summary>Windows 11 still reports "Windows 10" as its product name; the build number tells them apart.</summary>
    public static string WindowsName(string? product, int build) =>
        product is null ? "Windows" : build >= 22000 ? product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal) : product;
    public static string Running(TimeSpan up) =>
        up.TotalHours < 1 ? Localizer.T("Less than an hour") : up.TotalDays < 1 ? Localizer.Format((int)up.TotalHours == 1 ? "{0} hour" : "{0} hours", (int)up.TotalHours) : Localizer.Format((int)up.TotalDays == 1 ? "{0} day" : "{0} days", (int)up.TotalDays);
    public static string Size(long bytes)
    {
        double gb = bytes / 1073741824d;
        return gb >= 100 ? $"{gb:0} GB" : $"{gb:0.#} GB";
    }
    public static int Percent(long part, long total) => total <= 0 ? 0 : (int)Math.Round(100.0 * part / total);

    public static IReadOnlyList<GlanceTileModel> Tiles(GlanceFacts f)
    {
        var tiles = new List<GlanceTileModel> {
            new("Overview", "Windows", f.Windows ?? "Not reported", f.Build is { } b ? Localizer.Format("Build {0}", b) : "", CardStatus.Info, null, "Diagnose  /  Windows Update"),
            f.Uptime is { } up
                ? new("Diagnostic", "Running since restart", Running(up), up.TotalDays >= 14 ? "A restart installs updates and clears memory" : "Fast Startup keeps this running after Shut down",
                    up.TotalDays >= 14 ? CardStatus.Review : CardStatus.Info, null, "Diagnose  /  Battery & startup")
                : new("Diagnostic", "Running since restart", "Not reported", "", CardStatus.Unknown, null, "Diagnose  /  Battery & startup"),
        };
        if (f.SystemFree is { } free && f.SystemTotal is { } total && total > 0) {
            int used = Percent(total - free, total);
            tiles.Add(new("Storage", Localizer.Format("Drive {0}", f.SystemDrive), Localizer.Format("{0} free", Size(free)), Localizer.Format("of {0} · {1}% used", Size(total), used),
                free < 10L * 1073741824 || used >= 95 ? CardStatus.Problem : used >= 85 ? CardStatus.Review : CardStatus.Good, used, "Maintain"));
        } else tiles.Add(new("Storage", Localizer.Format("Drive {0}", f.SystemDrive), "Not reported", "", CardStatus.Unknown, null, "Maintain"));
        if (f.MemoryUsed is { } inUse && f.MemoryTotal is { } memory && memory > 0) {
            int used = Percent(inUse, memory);
            tiles.Add(new("Memory", "Memory in use", $"{used}%", Localizer.Format("{0} of {1}", Size(inUse), Size(memory)), used >= 90 ? CardStatus.Review : CardStatus.Good, used, "Memory"));
        } else tiles.Add(new("Memory", "Memory in use", "Not reported", "", CardStatus.Unknown, null, "Memory"));
        tiles.Add(new("GPU", "Graphics", f.Gpu ?? "Not reported", f.GpuDriver is { } d ? Localizer.Format("Driver {0}", d) : "", f.Gpu is null ? CardStatus.Unknown : CardStatus.Info, null, "GPU"));
        var (value, detail, status) = Protection(f.Antivirus, f.Firewall);
        tiles.Add(new("Shield", "Protection", value, detail, status, null, "Shield"));
        return tiles;
    }

    public static (string Value, string Detail, CardStatus Status) Protection(SecurityHealth? antivirus, SecurityHealth? firewall) => (antivirus, firewall) switch {
        (null, _) => ("Not reported", "Windows Security didn't report on this PC", CardStatus.Unknown),
        (SecurityHealth.Poor, _) => ("Needs attention", "Antivirus is off or out of date", CardStatus.Problem),
        (_, SecurityHealth.Poor) => ("Needs attention", "The firewall is off", CardStatus.Problem),
        (SecurityHealth.Snoozed, _) => ("Snoozed", "Antivirus protection is paused", CardStatus.Review),
        (SecurityHealth.NotMonitored, _) => ("Not monitored", "Windows Security isn't monitoring antivirus", CardStatus.Review),
        (_, null) => ("On", "Antivirus reports healthy", CardStatus.Good),
        _ => ("On", "Antivirus and firewall report healthy", CardStatus.Good)
    };
}

/// <summary>One line of Home's recent activity. Target is the page its action opens.</summary>
public sealed record ActivityItem(DateTimeOffset At, string Title, string Detail, string ActionLabel, string Target);

public static class HomeActivity
{
    private static int ToReview(DiagnosticScan s) => s.Results.Count(r => r.Severity is FindingSeverity.Warning or FindingSeverity.Critical);
    /// <summary>Whether every check of the scan ran; checks that need administrator rights still count as run.</summary>
    public static bool Finished(DiagnosticScan scan) => !scan.Cancelled && scan.CompletedModules >= scan.PlannedModules;
    /// <summary>How a scan went, in a few words. A cancelled or stopped scan never reads as "nothing needed attention".</summary>
    public static string ScanSummary(DiagnosticScan scan)
    {
        int n = ToReview(scan);
        string found = $"{n} {(n == 1 ? "recommendation" : "recommendations")} to review";
        if (!Finished(scan))
            return $"{(scan.Cancelled ? "Cancelled" : "Stopped")} after {scan.CompletedModules} of {scan.PlannedModules} checks" + (n > 0 ? "; " + found : "");
        return n == 0 ? "Nothing needed attention" : found;
    }

    /// <summary>The latest scan, the latest Performance check and recent changes, newest first.</summary>
    public static IReadOnlyList<ActivityItem> Build(DiagnosticScan? scan, DiagnosticScan? performance, IEnumerable<SettingChange> changes, int limit = 5)
    {
        var items = new List<ActivityItem>();
        if (scan is not null)
            items.Add(new(scan.Ended, "Fix my PC scan", ScanSummary(scan), "View results", "Fix My PC"));
        if (performance is not null) {
            int n = ToReview(performance);
            items.Add(new(performance.Ended, "Performance check", n == 0 ? "No optimization opportunities" : $"{n} {(n == 1 ? "opportunity" : "opportunities")} found", "Open Tune my PC", "Performance overview"));
        }
        foreach (var c in changes.Where(c => c.Status is "Applied" or "Undone"))
            items.Add(new(c.At, ChangeLabel(c), c.Status == "Undone" ? "Changed, then undone" : "Changed by Hanki · can be undone", "Review in Recovery", "Recovery"));
        return items.OrderByDescending(i => i.At).Take(limit).ToArray();
    }

    /// <summary>A change's setting in plain words.</summary>
    public static string ChangeLabel(SettingChange c)
    {
        static string? Nvidia(string hex) => hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && uint.TryParse(hex[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)
            ? NvidiaSettings.Catalog.FirstOrDefault(s => s.Id == id)?.Name : null;
        switch (c.Kind) {
            case "Windows gaming setting":
                return c.Target switch {
                    "game-mode" => "Game Mode", "background-recording" => "Game Bar background recording", "windowed-optimizations" => "Optimizations for windowed games",
                    "variable-refresh" => "Variable refresh rate", "auto-hdr" => "Auto HDR", "mouse-acceleration" => "Mouse acceleration", "power-mode" => "Power mode", _ => "Windows gaming setting"
                };
            case "NVIDIA global setting": return (Nvidia(c.Target) ?? "NVIDIA setting") + " (all games)";
            case "NVIDIA setting": {
                var parts = c.Target.Split('|');
                return (parts.Length == 2 ? Nvidia(parts[1]) : null) is { } name ? $"{name} for {Path.GetFileNameWithoutExtension(parts[0])}" : "NVIDIA game setting";
            }
            case "AMD setting":
                var kind = c.Target.Split('|').LastOrDefault();
                return Enum.TryParse<AmdSettingKind>(kind, out var k) && k.ToString() == kind ? AmdSettings.Name(k) : "Radeon setting";
            case "Display mode": return "Display mode";
            case "Processor power": return "Maximum processor state";
            case "GPU preference": return "GPU for " + Path.GetFileNameWithoutExtension(c.Target.Replace('\\', '/').Split('/').Last());
            default: return c.Kind;
        }
    }
}
