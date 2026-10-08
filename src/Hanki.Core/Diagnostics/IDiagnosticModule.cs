namespace IgezziGuard;

public sealed record DiagnosticRequirements(bool Administrator = false, bool ExternalContact = false,
    string Disclosure = "", bool WindowsOnly = true);
public sealed record DiagnosticContext(bool IsWindows, bool IsAdministrator, bool AllowExternalContact);
public sealed record DiagnosticProgress(string ModuleId, string Activity, int? Completed = null, int? Total = null);

/// <summary>Read-only collection. No repairs, elevation, UI controls or implicit external consent.</summary>
public interface IDiagnosticModule
{
    string Id { get; }
    string DisplayName { get; }
    DiagnosticCategory Category { get; }
    DiagnosticRequirements Requirements { get; }
    Task<IReadOnlyList<DiagnosticResult>> CollectAsync(DiagnosticContext context,
        IProgress<DiagnosticProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>Common failure boundary, also usable for a single module without an orchestrator.</summary>
public static class DiagnosticExecution
{
    public static async Task<IReadOnlyList<DiagnosticResult>> RunAsync(IDiagnosticModule module,
        DiagnosticContext context, IProgress<DiagnosticProgress>? progress, CancellationToken token)
    {
        var started = DateTimeOffset.UtcNow;
        DiagnosticResult State(CollectionOutcome outcome, string explanation) => new(module.Id, "collection", module.Category,
            outcome, FindingSeverity.Unknown, module.DisplayName, explanation, started, DateTimeOffset.UtcNow);
        try {
            token.ThrowIfCancellationRequested();
            var needs = module.Requirements;
            if (needs.WindowsOnly && !context.IsWindows) return [State(CollectionOutcome.Unavailable, "This check requires Windows.")];
            if (needs.Administrator && !context.IsAdministrator) return [State(CollectionOutcome.Unavailable, "Administrator access is required. Hanki has not requested elevation.")];
            if (needs.ExternalContact && !context.AllowExternalContact) return [State(CollectionOutcome.Unavailable, "External checks were not approved. " + needs.Disclosure)];
            var results = await module.CollectAsync(context, progress, token).ConfigureAwait(false);
            if (results is null || results.Count == 0 || results.Any(r => r is null || r.ModuleId != module.Id) ||
                results.Select(r => r.FindingId).Distinct(StringComparer.Ordinal).Count() != results.Count)
                return [State(CollectionOutcome.Failed, "The check returned incomplete or inconsistent results. No health conclusion is available.")];
            if (token.IsCancellationRequested) return [State(CollectionOutcome.Cancelled, "Collection was cancelled; incomplete evidence was discarded.")];
            return results.ToArray();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return [State(CollectionOutcome.Cancelled, "Collection was cancelled.")]; }
        catch (OperationCanceledException) { return [State(CollectionOutcome.Failed, "The check exceeded its time limit. Try it separately.")]; }
        catch (UnauthorizedAccessException) { return [State(CollectionOutcome.Unavailable, "Windows denied access. No health conclusion is available.")]; }
        catch (Exception) { return [State(CollectionOutcome.Failed, "The check could not complete. Open the individual tool for further investigation.")]; }
    }
}
