namespace IgezziGuard;

/// <summary>
/// Fix My PC's gaming check (HANKI-GAME-214, HANKI-GPU-112). It runs the read-only Gaming Health Scan and reports
/// only significant, high-impact configuration problems, so minor preferences don't clutter system diagnostics.
/// Nothing is changed from Fix My PC: each finding points to Performance → Gaming, where changes go through the
/// normal review, Recovery and Performance sessions. Overclocking is never part of it.
/// </summary>
internal sealed class GamingDiagnostic : IDiagnosticModule
{
    public string Id => GamingHealth.ModuleId;
    public string DisplayName => "Gaming configuration";
    public DiagnosticCategory Category => DiagnosticCategory.Performance;
    public DiagnosticRequirements Requirements => new();
    public Task<IReadOnlyList<DiagnosticResult>> CollectAsync(DiagnosticContext context, IProgress<DiagnosticProgress>? progress, CancellationToken token) => Task.Run(() => {
        progress?.Report(new(Id, "Reading display, GPU and gaming settings"));
        var graphics = GraphicsProbe.Collect();
        var windows = WindowsGamingProbe.Collect();
        NvidiaProfileView? nvidia = null;
        if (graphics.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia)) { try { nvidia = Nvidia.ReadProfiles([]).Global; } catch (NvidiaException) { } }
        return Significant(GamingHealth.Evaluate(graphics, windows, nvidia, DateTimeOffset.UtcNow));
    }, token);

    /// <summary>High-impact findings only; otherwise one "no significant problems" result.</summary>
    internal static IReadOnlyList<DiagnosticResult> Significant(IReadOnlyList<DiagnosticResult> findings)
    {
        var significant = findings.Where(GamingHealth.BelongsInFixMyPc).ToArray();
        if (significant.Length > 0) return significant;
        var now = findings.Count > 0 ? findings[0].Ended : DateTimeOffset.UtcNow;
        return [new DiagnosticResult(GamingHealth.ModuleId, "summary", DiagnosticCategory.Performance, CollectionOutcome.Completed, FindingSeverity.Healthy, "Gaming configuration",
            "No significant gaming configuration problems. Tune my PC → Gaming has the full check and optimization goals.", now, now, confidence: FindingConfidence.Confirmed)];
    }
}
