using System.Globalization;
using System.Text.RegularExpressions;

namespace IgezziGuard;

public sealed record InstalledApp(string Name, string Publisher, string Version, DateTime? InstallDate,
    long? EstimatedBytes, string Source)
{
    public string Usage => "Unknown — not measured";
}

public static class ReviewParsing
{
    public static DateTime? InstallDate(string? raw) => DateTime.TryParseExact(raw, "yyyyMMdd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    public static long? EstimatedBytes(object? value) => value is int n ? (long)unchecked((uint)n) * 1024 : null;
    public static string PagefileSetting(string raw)
    {
        var match = Regex.Match(raw, @"^(.*?)\s+(\d+)\s+(\d+)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) return raw + " — unrecognised setting; no sizing conclusion";
        var path = match.Groups[1].Value.Trim('"');
        if (!ulong.TryParse(match.Groups[2].Value, out var min) || !ulong.TryParse(match.Groups[3].Value, out var max))
            return raw + " — invalid size";
        return min == 0 && max == 0 ? path + " — system-managed size entry" :
            $"{path} — configured initial {min:N0} MB / maximum {max:N0} MB (not current allocation)";
    }
    public static string CommitGuidance(ulong used, ulong limit)
    {
        if (limit == 0) return "Commit limit unavailable; no memory-pressure assessment.";
        return (double)used / limit >= 0.9
            ? "Commit is at least 90% of the current limit in this snapshot. Review memory-heavy workloads and pagefile/disk headroom; one sample does not prove sustained pressure."
            : "Commit is below 90% of its current limit in this snapshot. This does not prove that memory is sufficient during your heaviest workload.";
    }
}
