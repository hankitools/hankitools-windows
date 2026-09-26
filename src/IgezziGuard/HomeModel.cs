namespace IgezziGuard;

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

public static class HomeScanStatus
{
    /// <summary>Whether every check of the scan ran; checks that need administrator rights still count as run.</summary>
    public static bool Finished(DiagnosticScan scan) => !scan.Cancelled && scan.CompletedModules >= scan.PlannedModules;
}
