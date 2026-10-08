using System.Text;

namespace IgezziGuard;

public sealed record CrashEvent(DateTimeOffset Time, string Log, string Provider, int Id, long? RecordId, string Level, string Message);
public static class CrashTimeline
{
    public static bool IsMarker(CrashEvent e) =>
        (e.Provider.Equals("Microsoft-Windows-Kernel-Power", StringComparison.OrdinalIgnoreCase) && e.Id == 41) ||
        (e.Provider.Equals("EventLog", StringComparison.OrdinalIgnoreCase) && e.Id == 6008) ||
        (e.Provider.Equals("Microsoft-Windows-WER-SystemErrorReporting", StringComparison.OrdinalIgnoreCase) && e.Id == 1001);

    public static string Explain(CrashEvent e)
    {
        string p = e.Provider.ToLowerInvariant();
        if (p == "microsoft-windows-kernel-power" && e.Id == 41) return "Unclean restart recorded at startup. This is not the crash time or proof of a power-supply fault. Check bugcheck data and the preceding session.";
        if (p == "eventlog" && e.Id == 6008) return "Previous shutdown was unexpected. The message may contain the earlier shutdown time; use that time for a custom window. Logged time can be after reboot.";
        if (p == "microsoft-windows-wer-systemerrorreporting" && e.Id == 1001) return "A bugcheck report. Review its code and dump location; the code alone does not identify the failing component.";
        if (p == "microsoft-windows-whea-logger") return "Hardware-error evidence. Read the message to distinguish corrected from uncorrected errors and identify the reported device. A corrected event alone does not prove the crash's cause.";
        if (p == "volmgr" && e.Id is 46 or 161) return "Crash-dump creation or initialization problem. Inspect dump/pagefile configuration and disk space; missing dumps do not rule out a bugcheck.";
        if ((p == "disk" && e.Id is 7 or 51 or 153) || ((p == "storahci" || p == "stornvme" || p == "iastora" || p == "iastorac") && e.Id == 129))
            return "Storage I/O or reset evidence. Check the named device, driver and repeated patterns; proximity is not proof of a failed drive.";
        if (p == "display" && e.Id == 4101) return "Display-driver recovery. Compare with black-screen symptoms; this does not establish a system-wide crash or a faulty GPU.";
        if (p == "application error" && e.Id == 1000) return "An application crashed. Inspect the application and faulting module; this does not necessarily explain a Windows restart.";
        if (p == "microsoft-windows-resource-exhaustion-detector" && e.Id == 2004) return "Resource exhaustion reported. Compare listed processes with memory/commit measurements.";
        return "Context event. Read the full message and compare with the symptom; timing alone does not establish causation.";
    }
    public static CrashEvent[] Window(IEnumerable<CrashEvent> events, DateTimeOffset center, int minutes)
    {
        if (minutes is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(minutes));
        return events.Where(e => e.Time >= center.AddMinutes(-minutes) && e.Time <= center.AddMinutes(minutes))
            .OrderBy(e => e.Time).ThenBy(e => e.Log).ThenBy(e => e.RecordId).ToArray();
    }
    /// <summary>One plain line naming what the window contains, most serious first.</summary>
    public static string Summary(IReadOnlyList<CrashEvent> selected)
    {
        var groups = selected.Where(e => e.Level != "Information" || IsMarker(e))
            .GroupBy(e => EventKnowledge.Describe(e.Provider, e.Id))
            .OrderBy(g => g.Key.Impact).ThenByDescending(g => g.Count()).ToArray();
        return groups.Length == 0 ? "IN SHORT: No warnings, errors or restart markers were recorded in this window."
            : "IN SHORT: " + string.Join("; ", groups.Select(g => g.Key.Impact == EventImpact.Other ? $"{g.Count()} other warning/error event(s)" : $"{g.Key.Name} ({g.Count()}×)")) + ".";
    }
    public static string Report(IEnumerable<CrashEvent> events, DateTimeOffset center, int minutes, string coverage)
    {
        var selected = Window(events, center, minutes);
        var result = new StringBuilder($"HANKI / CRASH TIMELINE\r\nWindow: {center.AddMinutes(-minutes):O} to {center.AddMinutes(minutes):O}\r\n\r\n{Summary(selected)}\r\n\r\n{coverage}\r\n\r\n");
        result.AppendLine("Times below are event recording times, shown in this PC's local timezone. Restart markers can be recorded after the actual crash. Nearby events are context, not a causal diagnosis. No dump analysis or repairs performed. Reports may contain private paths, names and application data.");
        if (selected.Length == 0) result.AppendLine("No matching events returned. This does not prove the PC was healthy; logs may be unavailable, cleared or incomplete.");
        foreach (var e in selected) {
            result.AppendLine($"\r\n{e.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}  •  {e.Log}  •  {e.Level}\r\n{e.Provider} / {e.Id} / record {e.RecordId}\r\n{Explain(e)}\r\nMESSAGE\r\n{e.Message}");
        }
        return result.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }
}
