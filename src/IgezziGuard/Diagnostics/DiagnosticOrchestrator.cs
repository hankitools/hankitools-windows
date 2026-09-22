namespace IgezziGuard;

public sealed record DiagnosticScan(Guid Id, DateTimeOffset Started, DateTimeOffset Ended,
    int PlannedModules, int CompletedModules, bool Cancelled, IReadOnlyList<DiagnosticResult> Results)
{
    public bool Complete => !Cancelled && CompletedModules == PlannedModules && Results.All(r => r.Outcome == CollectionOutcome.Completed);
}
public sealed record ScanProgressUpdate(int CompletedModules, int TotalModules, string ModuleId, string Activity);

/// <summary>Explicit registration and sequential collection keep workload and cancellation predictable.</summary>
public sealed class DiagnosticOrchestrator
{
    private readonly IDiagnosticModule[] modules;
    public DiagnosticOrchestrator(IEnumerable<IDiagnosticModule> modules)
    {
        this.modules = modules.ToArray();
        if (this.modules.Any(m => string.IsNullOrWhiteSpace(m.Id)) || this.modules.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != this.modules.Length)
            throw new ArgumentException("Module identities must be nonempty and unique.");
    }
    public async Task<DiagnosticScan> ScanAsync(DiagnosticContext context, IProgress<ScanProgressUpdate>? progress, CancellationToken token)
    {
        var started = DateTimeOffset.UtcNow; var results = new List<DiagnosticResult>(); int completed = 0;
        foreach (var module in modules) {
            if (token.IsCancellationRequested) break;
            progress?.Report(new(completed, modules.Length, module.Id, module.DisplayName));
            var updates = new InlineProgress<DiagnosticProgress>(p => progress?.Report(new(completed, modules.Length, module.Id, p.Activity)));
            var collected = await DiagnosticExecution.RunAsync(module, context, updates, token).ConfigureAwait(false);
            results.AddRange(collected);
            if (!collected.Any(r => r.Outcome == CollectionOutcome.Cancelled)) completed++;
            progress?.Report(new(completed, modules.Length, module.Id, "Check finished"));
        }
        return new(Guid.NewGuid(), started, DateTimeOffset.UtcNow, modules.Length, completed, token.IsCancellationRequested,
            results.OrderByDescending(r => Priority(r.Severity)).ThenBy(r => r.ModuleId, StringComparer.Ordinal).ThenBy(r => r.FindingId, StringComparer.Ordinal).ToArray());
    }
    internal static int Priority(FindingSeverity severity) => severity switch {
        FindingSeverity.Critical => 4, FindingSeverity.Warning => 3, FindingSeverity.Unknown => 2,
        FindingSeverity.Informational => 1, _ => 0
    };
}
internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
