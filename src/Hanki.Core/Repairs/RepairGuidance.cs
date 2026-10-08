using System.Text;
namespace IgezziGuard;

/// <summary>
/// Plain-language text around automatic repairs: why none are offered, why each proposal was made,
/// what stops the approval, and how it went. Kept apart from the dialog so it can be tested.
/// </summary>
public static class RepairGuidance
{
    /// <summary>Repairs are offered only from a scan this recent (RepairWorkflow enforces the same limit).</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(30);

    /// <summary>The name used in results and customer reports; it reads the same whether or not the repair ran.</summary>
    public static string Name(string actionId) => actionId switch {
        "dns-cache-flush" => "Windows DNS cache refresh",
        "dism-restore" => "Windows component store repair (DISM)",
        "sfc-repair" => "Protected Windows files repair (SFC)",
        _ => actionId
    };

    /// <summary>Why nothing was proposed: what Hanki can repair and which of those this scan couldn't look at.</summary>
    public static string NoProposals(DiagnosticScan scan)
    {
        bool servicingChecked = scan.Results.Any(r => r.ModuleId is "dism" or "sfc" && r.Outcome == CollectionOutcome.Completed);
        bool dnsChecked = scan.Results.Any(r => r.ModuleId == "network-probes" && r.Outcome == CollectionOutcome.Completed);
        var text = new StringBuilder("No automatic repairs apply to this scan.\r\n\r\n");
        text.Append("Hanki repairs three well-understood Windows problems automatically:\r\n");
        text.Append("• A damaged Windows component store (repaired with DISM)\r\n");
        text.Append("• Damaged protected Windows files (repaired with SFC)\r\n");
        text.Append("• Failing DNS lookups (the Windows DNS cache is refreshed)\r\n\r\n");
        if (servicingChecked && dnsChecked) text.Append("None of these showed up in this scan.\r\n");
        else {
            if (!servicingChecked) text.Append("Windows files weren't checked: the DISM and SFC checks need administrator rights. To include them, close Hanki, run it as administrator and scan again.\r\n");
            if (!dnsChecked) text.Append("DNS wasn't checked: tick Include network probes before you scan.\r\n");
        }
        text.Append("\r\nFor everything else, select a finding to see what it means and what to do next.");
        return text.ToString();
    }

    /// <summary>The finding behind a proposal, in the words of its recommendation.</summary>
    public static string Reason(DiagnosticScan scan, string actionId) =>
        scan.Results.Select(r => (Result: r, Advice: FindingAnalysis.Recommend(r)))
            .Where(x => x.Advice?.RepairActionId == actionId)
            .Select(x => $"{x.Result.Title}: {x.Advice!.Meaning}").FirstOrDefault() ?? "";

    public static string Restart(RestartRequirement restart) => restart switch {
        RestartRequirement.None => "No restart needed",
        RestartRequirement.Required => "Needs a restart",
        _ => "May need a restart"
    };

    /// <summary>What this repair sends over the network, for the network-use approval.</summary>
    public static string NetworkUse(RepairDefinition d) => d.Id switch {
        "dism-restore" => "The component store repair may download replacement files from Windows Update.",
        "dns-cache-flush" => "The DNS cache refresh runs the network check again afterwards, using the test sites listed above.",
        _ => d.Title + " uses the network."
    };

    /// <summary>Why a repair can't be chosen on this PC right now, or null.</summary>
    public static string? Blocker(RepairDefinition d, bool administrator) =>
        d.RequiresAdministrator && !administrator ? "Needs administrator rights. Close Hanki, run it as administrator and scan again." : null;

    /// <summary>When no proposed repair can run in this session, the page says so instead of opening the dialog.</summary>
    public static string? NoneAvailable(IReadOnlyCollection<RepairDefinition> proposed, bool administrator) =>
        proposed.Count > 0 && proposed.All(d => Blocker(d, administrator) is not null)
            ? $"This scan found something Hanki can repair ({string.Join(", ", proposed.Select(d => Name(d.Id)))}), but repairs need administrator rights.\r\n\r\n" +
              "Close Hanki, right-click it and choose Run as administrator, then scan again and review repairs. To fix it by hand instead, select the finding to see the manual steps."
            : null;

    /// <summary>Why the selection can't be approved yet, or null when it can.</summary>
    public static string? ApprovalProblem(IReadOnlyCollection<RepairDefinition> selected, bool networkAllowed)
    {
        if (selected.Count == 0) return "Tick the repairs you want to run.";
        var needsNetwork = selected.Where(d => d.RequiresNetwork).Select(d => Name(d.Id)).ToList();
        if (needsNetwork.Count > 0 && !networkAllowed)
            return $"The {string.Join(" and the ", needsNetwork)} {(needsNetwork.Count == 1 ? "needs" : "need")} the network. Tick Allow network use, or untick {(needsNetwork.Count == 1 ? "it" : "them")}.";
        return null;
    }

    /// <summary>Why this scan can't lead to repairs any more, or null.</summary>
    public static string? Stale(DiagnosticScan scan, bool repairsRan, DateTimeOffset now) =>
        repairsRan ? "Repairs already ran for this scan. Run a new full scan first; repairs are only offered from fresh results."
        : now - scan.Ended > FreshFor ? $"This scan is more than {FreshFor.TotalMinutes:0} minutes old. Run a new full scan first, so repairs are based on the PC's current state."
        : null;

    /// <summary>One line per repair: did it run, and did the check pass afterwards.</summary>
    public static string Outcome(RepairAttempt a) => a.State switch {
        RepairState.Executed => a.Verification switch {
            VerificationState.Fixed => "Done, and the check now passes.",
            VerificationState.Improved => "Done. The problem improved but isn't fully resolved.",
            VerificationState.Unchanged => "Done, but the check shows no change.",
            VerificationState.Worse => "Done, but the check looks worse afterwards. This needs attention.",
            VerificationState.RequiresRestart => "Done. A restart is needed to finish.",
            _ => "Done, but the result couldn't be confirmed."
        },
        RepairState.Blocked => "Not run: " + DiagnosticPrivacy.Redact(a.Explanation),
        RepairState.Failed => "Tried, but it didn't complete.",
        RepairState.Cancelled => "Cancelled before it finished.",
        RepairState.Skipped => "Skipped.",
        _ => "Not finished."
    };

    public static string Results(RepairReport report)
    {
        var text = new StringBuilder("Repair results\r\n\r\n");
        foreach (var a in report.Attempts) text.Append($"{Name(a.ActionId)}: {Outcome(a)}\r\n");
        text.Append("\r\n");
        if (report.Attempts.Any(a => a.State is RepairState.Failed or RepairState.Cancelled))
            text.Append("A repair that didn't complete may have left Windows partly changed. Read the details below before trying again.\r\n");
        text.Append(report.Attempts.Any(a => a.Verification == VerificationState.RequiresRestart)
            ? "Restart the PC to finish, then run a new full scan to confirm."
            : "Run a new full scan to see the current state of the PC.");
        text.Append("\r\n\r\nDetails\r\n").Append(RepairReportText.Format(report));
        return text.ToString();
    }
}
