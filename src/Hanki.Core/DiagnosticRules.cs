namespace IgezziGuard;

public static class DiagnosticRules
{
    public static double? CpuPercent(ulong idleBefore, ulong kernelBefore, ulong userBefore, ulong idleAfter, ulong kernelAfter, ulong userAfter)
    {
        if (idleAfter < idleBefore || kernelAfter < kernelBefore || userAfter < userBefore) return null;
        var total = (double)(kernelAfter - kernelBefore) + (userAfter - userBefore);
        var idle = idleAfter - idleBefore;
        return total <= 0 || idle > total ? null : Math.Clamp(100 * (total - idle) / total, 0, 100);
    }
    public static string PingSummary(int sent, IReadOnlyList<long> replies) => sent <= 0 ? "No probes sent." :
        $"Replies {replies.Count}/{sent}; no-reply rate {100d * (sent - replies.Count) / sent:0.#}%. " +
        (replies.Count == 0 ? "Latency unavailable." : $"RTT min/mean/max {replies.Min()}/{replies.Average():0.#}/{replies.Max()} ms.") +
        " ICMP can be blocked or deprioritised; this is a small sample, not proof of application packet loss.";
}
