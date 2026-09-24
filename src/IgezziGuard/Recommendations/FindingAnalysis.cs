using System.Globalization;

namespace IgezziGuard;

public sealed record FindingGroup(string Key, IReadOnlyList<DiagnosticResult> Sources);
public sealed record RankedFinding(FindingGroup Group, int Priority, string Reason);
public sealed record Recommendation(string RuleId, string Observation, string Meaning, string ManualAction,
    string? RepairActionId, string Prerequisites, string VerificationModuleId);

public static class FindingAnalysis
{
    // Correlation groups evidence for review, never replaces the originating results or claims causation.
    public static IReadOnlyList<FindingGroup> Normalize(IEnumerable<DiagnosticResult> results) => results
        .GroupBy(r => r.ModuleId is "dism" or "sfc" && r.Severity is FindingSeverity.Warning or FindingSeverity.Critical
            ? "windows-integrity" : r.ModuleId + ":" + r.FindingId, StringComparer.Ordinal)
        .OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => new FindingGroup(g.Key, g.OrderBy(r => r.ModuleId, StringComparer.Ordinal).ThenBy(r => r.FindingId, StringComparer.Ordinal).ThenBy(r => r.Started).ToArray())).ToArray();

    public static IReadOnlyList<RankedFinding> Rank(IEnumerable<FindingGroup> groups) => groups.Select(g => {
        var known = g.Sources.Where(r => r.Outcome is CollectionOutcome.Completed or CollectionOutcome.Partial && r.Severity != FindingSeverity.Unknown).ToArray();
        int severity = known.Select(r => r.Severity switch { FindingSeverity.Critical => 300, FindingSeverity.Warning => 200, FindingSeverity.Informational => 50, _ => 0 }).DefaultIfEmpty(0).Max();
        int confidence = known.Select(r => r.Confidence switch { FindingConfidence.Confirmed => 20, FindingConfidence.Likely => 10, FindingConfidence.Possible => 3, _ => 0 }).DefaultIfEmpty(0).Max();
        int repetition = Math.Min(10, known.Where(r => r.ModuleId == "events").Select(r => r.Metadata.TryGetValue("value", out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? Math.Max(0, count) : 0).DefaultIfEmpty(0).Max());
        int actionable = known.Any(r => Recommend(r) is not null) ? 5 : 0;
        // Severity dominates. Unknown evidence receives no inferred impact/actionability bonus.
        int score = severity == 0 ? 0 : severity + confidence + repetition + actionable;
        return new RankedFinding(g, score, $"Severity {severity}; evidence confidence {confidence}; repeated-event bonus {repetition}; manual guidance {actionable}. No causal claim.");
    }).OrderByDescending(r => r.Priority).ThenBy(r => r.Group.Key, StringComparer.Ordinal).ToArray();

    public static Recommendation? Recommend(DiagnosticResult r)
    {
        if (r.Outcome is CollectionOutcome.Unavailable or CollectionOutcome.Failed or CollectionOutcome.Cancelled || r.Severity is FindingSeverity.Unknown or FindingSeverity.Healthy) return null;
        if (r.ModuleId == "activation" && r.Recommendation is not null)
            return new("activation:"+r.FindingId,r.Explanation,"Windows licensing status is distinct from system health; no activation bypass or automatic key change is offered.",r.Recommendation,null,"Use only your legitimate Microsoft or organization license.","activation");
        (string meaning, string action, string? repair, string prerequisite)? rule = r.ModuleId switch {
            "dism" when r.Metadata.GetValueOrDefault("state") == "repairable" => ("The component store is reported repairable; it may affect servicing but is not proven to cause your symptom.", "Back up important files and review DISM repair with an administrator.", "dism-restore", "Administrator; Windows servicing available; no pending restart; explicit network-source consent."),
            "sfc" when r.Severity == FindingSeverity.Warning => ("Protected system-file integrity needs review.", "Review Windows servicing evidence; repair the component store first if it also reports corruption.", "sfc-repair", "Administrator; Windows servicing available; no pending restart."),
            "storage" when r.FindingId == "capacity" => ("Low free space can constrain updates and temporary files; it does not establish disk damage.", "Review large personal files in Maintain. Back up before recycling selected files.", null, "No automatic deletion."),
            "storage" => ("Storage provider state and lifetime counters need context; individual counts do not predict failure.", "Back up important data and review the drive manufacturer's diagnostics.", null, "Do not repair or format a drive solely from this report."),
            // Only a total lookup failure is a DNS problem; one unresolved test name is usually a filtered network.
            "network-probes" when r.FindingId == "dns" && r.Severity is FindingSeverity.Warning or FindingSeverity.Critical => ("DNS lookup failed. Resolver, connectivity, VPN, proxy or endpoint conditions may explain it.", "Check the connection and compare DNS responses in Connect. A reviewed DNS-cache refresh is a limited troubleshooting attempt, not a confirmed fix.", "dns-cache-flush", "Administrator; approve verification probes; cache entries are repopulated by Windows. No persistent DNS server setting changes."),
            "network" or "network-probes" => ("This is one part of network reachability, not a complete internet verdict.", "Use Connect to compare local adapter, gateway and DNS evidence. Keep existing network settings recorded.", null, "VPNs and policy may be intentional."),
            "security" => ("Windows security state may reflect another product or organization policy.", "Review Windows Security and your antivirus console. Keep protection enabled; avoid broad exclusions.", null, "Do not remove or disable another security provider automatically."),
            "devices" => ("A reported device code may be intentional, transient or user-impacting.", "Review the device code in Device Manager and the manufacturer's support information.", null, "No automatic driver download, removal or update."),
            "events" => ("Repeated provider/ID signals may help narrow an investigation; they are not a root cause.", "Match the event window to the symptom and compare with crash timeline and dump evidence.", null, "Review repetition across separate incidents."),
            "performance" => ("A snapshot or startup count does not establish sustained resource pressure.", "Repeat monitoring during the same workload. Review startup impact in Task Manager before disabling entries.", null, "Do not disable pagefile, security services or accessibility tools for a score."),
            "update" => ("Update history, restart markers and service settings have different meanings; a single failed attempt that later installed is not a problem.", "Open Diagnose → Windows Update for the details, finish a requested restart, then use Check for updates in Windows Update.", null, "Hanki doesn't change Windows Update settings."),
            "battery-startup" => ("Battery wear and restart history describe this PC's current state; they are not faults by themselves.", "Open Diagnose → Battery & startup for the details. Use Restart, not Shut down, now and then to finish updates and clear memory.", null, "No settings are changed."),
            // Gaming configuration is changed only in Performance → Gaming, with review and Recovery; never repaired automatically from here.
            "app-crashes" => ("Windows recorded these crashes itself; repeated crashes of one app point to that app or the part it crashed in, not to Windows.", "Update the app, or the graphics driver when the crashes are in it. If it keeps crashing, repair or reinstall the app; Diagnose → Crash timeline shows what happened around each crash.", null, "Hanki doesn't update, repair or remove apps from here."),
            "network-link" => ("The speed the adapter and the router agreed on; it caps the connection, but real speeds also depend on the internet line.", "For a slow wired link, try another cable and another port on the router. For Wi-Fi, compare the router with the internet in Connect → Wi-Fi / latency.", null, "No network settings are changed."),
            "time-sync" => ("Whether Windows keeps its clock right; a wrong clock breaks sign-ins, secure websites and updates.", "Check the clock, then use Sync now in Settings → Time & language → Date & time. If syncs keep failing, check the firewall, VPN or router for blocked time requests.", null, "Hanki doesn't change time settings."),
            "gaming" => ("A gaming configuration issue that holds back performance; Windows itself is not faulty.", "Open Tune my PC → Gaming to review it. Hanki changes it only after you approve, and you can undo it in Recovery.", null, "No automatic change from Fix My PC; no overclocking."),
            _ => null
        };
        return rule is { } value ? new(r.ModuleId + ":" + r.FindingId, r.Explanation, value.meaning, value.action,
            value.repair, value.prerequisite, r.ModuleId) : null;
    }
    public static string Describe(DiagnosticResult r)
    {
        var recommendation = Recommend(r);
        return $"{r.Title}\r\n{r.Severity} · {r.Outcome} · Evidence confidence: {r.Confidence}\r\n\r\nObserved\r\n{r.Explanation}\r\n\r\n" +
            (recommendation is null ? "Next step\r\nNo specific repair is supported by this finding. Review coverage and use the individual tool if the symptom persists.\r\n" :
            $"What it may mean\r\n{recommendation.Meaning}\r\n\r\nWhat to do next\r\n{recommendation.ManualAction}\r\n{recommendation.Prerequisites}\r\nAutomatic action: {(recommendation.RepairActionId ?? "None — manual guidance only")}\r\n") +
            $"\r\nCoverage\r\n{r.Coverage}\r\n\r\nTechnical evidence — review before sharing\r\n{r.Evidence}";
    }
}
