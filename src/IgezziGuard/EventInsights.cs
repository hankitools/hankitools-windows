using System.Text;
using System.Text.RegularExpressions;

namespace IgezziGuard;

public enum EventImpact { Serious, Review, Noise, Other }

/// <summary>Plain-language meaning of common Windows events. Explanations are clues to investigate, not diagnoses.</summary>
public sealed record EventMeaning(string Name, EventImpact Impact, string Meaning, string Action);

public static class EventKnowledge
{
    public static EventMeaning Describe(string provider, int id)
    {
        // Some providers log under a "Microsoft-Windows-" name that Event Viewer shows in short form.
        string p = provider.ToLowerInvariant() switch { "microsoft-windows-distributedcom" => "distributedcom", "microsoft-windows-ntfs" => "ntfs", var name => name };
        return (p, id) switch {
            ("microsoft-windows-kernel-power", 41) => new("Unexpected restart", EventImpact.Serious,
                "Windows started again without a clean shutdown: a crash, freeze, power cut or holding the power button.",
                "Open Crash timeline at that time to see what happened just before."),
            ("eventlog", 6008) => new("Unexpected shutdown", EventImpact.Serious,
                "The previous shutdown was not clean. The message includes when it happened.",
                "Open Crash timeline at the time given in the message."),
            ("microsoft-windows-wer-systemerrorreporting" or "bugcheck", 1001) => new("Blue screen (stop error)", EventImpact.Serious,
                "Windows stopped with a blue screen and recorded a stop code. Drivers are the most common cause, but the code alone doesn't prove which one.",
                "Check Dump analysis and Crash timeline. Update the drivers for recently changed hardware."),
            ("microsoft-windows-whea-logger", 18 or 20 or 46 or 47) => new("Hardware error", EventImpact.Serious,
                "The processor, memory or a device reported an error it could not correct.",
                "Back up important files. Check temperatures and remove any overclock; if it repeats, contact the PC or part manufacturer."),
            ("microsoft-windows-whea-logger", _) => new("Corrected hardware error", EventImpact.Review,
                "A device (often PCIe) reported an error that was corrected automatically. Occasional ones are common.",
                "Only worth investigating if there are many or they line up with crashes."),
            ("disk", 7) => new("Bad block on a disk", EventImpact.Serious,
                "A drive reported a bad block, which can mean the drive is wearing out.",
                "Back up now. Check the drive with its manufacturer's tool."),
            ("disk", 11) => new("Disk controller error", EventImpact.Serious,
                "Windows couldn't talk to a drive properly. Cables, the drive or its controller can cause this.",
                "Back up, reseat or replace the cable if it's a desktop, and check the drive's health."),
            ("disk", 51 or 153) => new("Disk had to retry", EventImpact.Review,
                "Reading or writing needed a retry. Occasional ones can happen, especially on USB drives.",
                "If it repeats, back up and check the drive and its connection."),
            ("storahci" or "stornvme" or "iastora" or "iastorac" or "iastorav", 129) => new("Storage controller reset", EventImpact.Review,
                "The storage driver had to reset a drive that stopped responding. This can cause short freezes.",
                "Update the storage/chipset driver and drive firmware; check the drive's health."),
            ("ntfs", 55) => new("File system corruption", EventImpact.Serious,
                "Windows found damage in the file system on a drive.",
                "Back up, then run 'chkdsk /scan' from an administrator terminal."),
            ("ntfs", 98 or 130 or 137) => new("Drive needs a check", EventImpact.Review,
                "Windows flagged a volume for repair or scanned it.",
                "Let Windows run its repair; if it repeats, check the drive's health."),
            ("volmgr", 46 or 161 or 162) => new("Crash dump not created", EventImpact.Review,
                "Windows couldn't prepare or write a crash dump, so blue screens may leave no dump to analyze.",
                "Check that the pagefile is system-managed and the system drive has free space."),
            ("display", 4101) or ("nvlddmkm", _) or ("amdkmdag", _) or ("amdwddmg", _) => new("Graphics driver problem", EventImpact.Review,
                "The graphics driver stopped responding or reported an error and Windows recovered it. Screens may flicker or go black briefly.",
                "Update the graphics driver from the card maker's site; watch temperatures under load."),
            ("application error", 1000) => new("App crashed", EventImpact.Review,
                "An app closed unexpectedly. This affects that app, not Windows as a whole.",
                "Update or reinstall the app if it keeps crashing."),
            ("application hang", 1002) => new("App stopped responding", EventImpact.Review,
                "An app froze and was closed.",
                "Update the app; if many apps freeze, check memory and disk in Performance."),
            (".net runtime", 1026) => new("App crashed (.NET)", EventImpact.Review,
                "A .NET app closed because of an unhandled error.",
                "Update or reinstall the app if it keeps happening."),
            ("microsoft-windows-resource-exhaustion-detector", 2004) => new("Running out of memory", EventImpact.Serious,
                "Windows warned that memory was nearly exhausted and listed the biggest users.",
                "Check Performance → Memory while the same apps are open; close or update the heaviest ones."),
            ("service control manager", 7031 or 7034) => new("A service stopped unexpectedly", EventImpact.Review,
                "A background service crashed. Windows often restarts it automatically.",
                "If it repeats, update or reinstall the software that owns the service."),
            ("service control manager", 7000 or 7001 or 7009 or 7011 or 7022 or 7023 or 7024 or 7026 or 7038 or 7043) => new("A service failed to start", EventImpact.Review,
                "A background service didn't start or timed out. Leftovers from uninstalled software are a common cause.",
                "If you don't recognize the service or nothing is broken, it's usually safe to ignore."),
            ("distributedcom", 10001 or 10005 or 10010 or 10016 or 10028 or 10029) => new("Background component notice (DCOM)", EventImpact.Noise,
                "Windows logs these by design. Microsoft documents them as safe to ignore.",
                "No action needed."),
            ("microsoft-windows-dns-client", 1014) => new("Name lookup timed out", EventImpact.Noise,
                "A website name took too long to look up. A few are normal, especially after waking from sleep.",
                "If there are many, run Connect → Basic checks."),
            ("microsoft-windows-time-service", _) => new("Clock sync notice", EventImpact.Noise,
                "Windows couldn't reach the time server for a moment.",
                "No action needed unless your clock is wrong."),
            ("microsoft-windows-windowsupdateclient", 20) => new("Update failed to install", EventImpact.Review,
                "An update didn't install. Windows usually retries it.",
                "Open Windows Update and check its history; restart and try again."),
            ("microsoft-windows-kernel-pnp", 219) => new("Device driver didn't load at startup", EventImpact.Noise,
                "A driver took too long or was missing during startup. Usually harmless if all devices work.",
                "Only investigate if a device isn't working."),
            ("microsoft-windows-kernel-processor-power", 37) => new("CPU speed limited", EventImpact.Review,
                "Firmware limited the processor speed, often for power saving or heat.",
                "Check cooling and the power plan if the PC feels slow."),
            ("microsoft-windows-security-spp", _) => new("Licensing notice", EventImpact.Noise,
                "The Windows licensing service logged a notice.",
                "If Windows says it isn't activated, see Diagnose → Windows Activation."),
            ("esent", _) or ("microsoft-windows-user profiles service", _) or ("microsoft-windows-kernel-eventtracing", _) or ("microsoft-windows-perflib", _) or ("perflib", _) =>
                new("Windows housekeeping notice", EventImpact.Noise, "Routine notices from Windows components.", "No action needed."),
            _ when p.StartsWith("netwtw", StringComparison.Ordinal) || p.StartsWith("netwbw", StringComparison.Ordinal) || p.StartsWith("rtwlan", StringComparison.Ordinal) =>
                new("Wi-Fi adapter event", EventImpact.Review, "The Wi-Fi driver reported a problem or reset.", "Update the Wi-Fi driver and run Connect → Wi-Fi / latency."),
            _ => new("Other warning or error", EventImpact.Other, "Not in Hanki's list of common events.", "Search the provider and ID if it repeats or matches a problem you notice.")
        };
    }

    /// <summary>English messages only; other languages return null.</summary>
    internal static string? Subject(string provider, int id, string message)
    {
        string p = provider.ToLowerInvariant();
        var match = (p, id) switch {
            ("application error", 1000) => Regex.Match(message, @"Faulting application name:\s*([^,\r\n]+)"),
            ("application hang", 1002) => Regex.Match(message, @"The program\s+(\S+)"),
            (".net runtime", 1026) => Regex.Match(message, @"Application:\s*(\S+)"),
            ("service control manager", _) => Regex.Match(message, @"^The (.+?) service", RegexOptions.Multiline),
            _ => Match.Empty
        };
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}

public static class EventInsights
{
    public static Diagnosis Summarize(IReadOnlyList<CrashEvent> events, string coverage, DateTimeOffset now)
    {
        var problems = events.Where(e => e.Level is "Critical" or "Error" or "Warning" || CrashTimeline.IsMarker(e)).ToArray();
        var groups = problems.GroupBy(e => (Provider: e.Provider, e.Id))
            .Select(g => (g.Key.Provider, g.Key.Id, Meaning: EventKnowledge.Describe(g.Key.Provider, g.Key.Id), Events: g.OrderByDescending(e => e.Time).ToArray()))
            .OrderBy(g => g.Meaning.Impact).ThenByDescending(g => g.Events.Length).ToArray();
        int restarts = problems.Where(CrashTimeline.IsMarker).Select(e => e.Time.ToString("yyyyMMddHHmm")).Distinct().Count();
        int critical = problems.Count(e => e.Level == "Critical"), errors = problems.Count(e => e.Level == "Error"), warnings = problems.Count(e => e.Level == "Warning");
        var serious = groups.Where(g => g.Meaning.Impact == EventImpact.Serious).ToArray();
        var review = groups.Where(g => g.Meaning.Impact == EventImpact.Review).ToArray();
        var noise = groups.Where(g => g.Meaning.Impact == EventImpact.Noise).ToArray();
        var other = groups.Where(g => g.Meaning.Impact == EventImpact.Other).ToArray();

        var cards = new List<ResultCard> {
            new("Last 7 days at a glance", $"{critical} critical, {errors} errors and {warnings} warnings in the System and Application logs." +
                (restarts > 0 ? $"\n{restarts} unexpected restart(s) or blue screen(s)." : "\nNo unexpected restarts recorded.") +
                "\nMost Windows PCs log some errors every day; what matters is which ones and how often.",
                serious.Length > 0 ? CardStatus.Problem : review.Length > 0 ? CardStatus.Review : CardStatus.Good)
        };
        foreach (var g in serious.Concat(review).Take(8)) {
            var subjects = g.Events.Select(e => EventKnowledge.Subject(g.Provider, g.Id, e.Message)).OfType<string>()
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase).OrderByDescending(s => s.Count()).Take(4).Select(s => s.Count() > 1 ? $"{s.Key} ({s.Count()}×)" : s.Key).ToArray();
            cards.Add(new($"{g.Meaning.Name}", $"Seen {Times(g.Events.Length)}, most recently {When(g.Events[0].Time, now)}." +
                (subjects.Length > 0 ? "\nWhich: " + string.Join(", ", subjects) + "." : "") +
                $"\n{g.Meaning.Meaning}\nWhat to do: {g.Meaning.Action}\n{g.Provider} · event {g.Id}",
                g.Meaning.Impact == EventImpact.Serious ? CardStatus.Problem : CardStatus.Review));
        }
        if (noise.Length > 0)
            cards.Add(new("Usually harmless", string.Join("\n", noise.Select(g => $"{g.Meaning.Name}: {Times(g.Events.Length)} — {g.Meaning.Meaning}")), CardStatus.Good));
        if (other.Length > 0)
            cards.Add(new("Other warnings and errors", $"{other.Sum(g => g.Events.Length)} events from {other.Length} other sources, such as " +
                string.Join(", ", other.Take(4).Select(g => $"{g.Provider} {g.Id} ({g.Events.Length}×)")) + ". Look them up only if they match a problem you notice.", CardStatus.Info));

        string headline = restarts > 0 ? $"{restarts} unexpected restart(s) in the last 7 days"
            : serious.Length > 0 ? $"{serious[0].Meaning.Name} found in the last 7 days"
            : review.Length > 0 ? "No serious problems, a few things worth a look"
            : "No serious problems in the last 7 days";
        string next = restarts > 0 ? $"Open the Crash timeline tab and build a timeline around {When(problems.Where(CrashTimeline.IsMarker).Max(e => e.Time), now)} to see what happened just before."
            : serious.Length > 0 ? serious[0].Meaning.Action
            : review.Length > 0 ? "Nothing urgent. If a specific app or device misbehaves, start with the matching card above."
            : "Nothing to do. If you have a specific problem, try Guided checks.";
        cards.Add(new("What to do next", next + "\nEvents near a problem are clues, not proof of its cause. The full event list is under View technical details."));
        return Diagnosis.From(Report(events, groups.Select(g => (g.Provider, g.Id, g.Meaning, g.Events)).ToArray(), coverage, now), cards, headline);
    }
    private static string Report(IReadOnlyList<CrashEvent> events, (string Provider, int Id, EventMeaning Meaning, CrashEvent[] Events)[] groups, string coverage, DateTimeOffset now)
    {
        var text = new StringBuilder($"HANKI DIAGNOSE • Recent Event Logs • {now:O}\r\nLast 7 days; warning, error and critical events plus restart markers from the System and Application logs.\r\n{coverage}\r\n");
        text.AppendLine("Reports can contain usernames, computer names, paths and application data. Review before sharing.\r\n");
        text.AppendLine("GROUPED BY SOURCE");
        foreach (var g in groups)
            text.AppendLine($"\r\n{g.Provider} / {g.Id} — {g.Meaning.Name} ({g.Meaning.Impact}) — {g.Events.Length}× — latest {g.Events[0].Time.ToLocalTime():yyyy-MM-dd HH:mm:ss}\r\n{Trim(g.Events[0].Message, 600)}");
        text.AppendLine("\r\nNEWEST EVENTS");
        foreach (var e in events.OrderByDescending(e => e.Time).Take(150))
            text.AppendLine($"{e.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {e.Log,-11}  {e.Level,-11}  {e.Provider} / {e.Id}");
        return text.ToString();
    }
    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + " [truncated]";
    private static string Times(int count) => count == 1 ? "once" : $"{count} times";
    internal static string When(DateTimeOffset time, DateTimeOffset now)
    {
        var local = time.ToLocalTime(); var today = now.ToLocalTime().Date;
        return local.Date == today ? "today at " + local.ToString("t") : local.Date == today.AddDays(-1) ? "yesterday at " + local.ToString("t") : local.ToString("ddd d MMM") + " at " + local.ToString("t");
    }
}
