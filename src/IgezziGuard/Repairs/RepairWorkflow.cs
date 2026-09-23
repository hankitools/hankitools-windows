namespace IgezziGuard;

public enum RepairRisk { Low, Moderate, High }
public enum RepairState { Skipped, Blocked, Pending, Executed, Failed, Cancelled }
public enum VerificationState { Fixed, Improved, Unchanged, Worse, Failed, RequiresRestart, NotRun }
public enum RestoreState { Created, Unavailable, Failed, Unsupported, NotRequested }
public sealed record RestoreResult(RestoreState State, string Explanation, string? Reference = null);
public sealed record RepairDefinition(string Id, string Title, string ChangeDescription, RepairRisk Risk,
    bool RequiresAdministrator, bool RequiresNetwork, bool RecommendRestorePoint, bool RequireRestorePoint,
    string[] RequiredServices, string VerificationModuleId, string RollbackInformation);
public sealed record RepairEnvironment(bool SupportedWindows, bool Administrator, bool RestartPending,
    bool Conflict, IReadOnlySet<string> AvailableServices, bool NetworkApproved);
public sealed record SafetyDecision(bool Allowed, IReadOnlyList<string> Reasons);
public sealed record RepairExecutionResult(bool Completed, bool RequiresRestart, string Explanation);
public sealed record RepairAttempt(string ActionId, DateTimeOffset Started, DateTimeOffset Ended, RepairState State,
    string Explanation, RestoreResult Protection, VerificationState Verification,
    IReadOnlyList<DiagnosticResult> Before, IReadOnlyList<DiagnosticResult> After);
public sealed record RepairReport(Guid ScanId, IReadOnlyList<RepairAttempt> Attempts);
public sealed record RepairApproval(Guid ScanId, IReadOnlySet<string> ActionIds, bool AllowWithoutRestorePoint, bool NetworkApproved);

public interface IRepairAction
{
    RepairDefinition Definition { get; }
    Task<RepairExecutionResult> ExecuteAsync(CancellationToken token);
}
public interface IRepairEnvironment { Task<RepairEnvironment> ReadAsync(CancellationToken token); }
public interface IRestoreProtection { Task<RestoreResult> CreateAsync(CancellationToken token); }
public interface IRepairAudit { Task RecordAsync(Guid scanId, RepairAttempt attempt, CancellationToken token); }

public static class RepairSafety
{
    public static SafetyDecision Evaluate(RepairDefinition d, RepairEnvironment e)
    {
        var reasons = new List<string>();
        if (!e.SupportedWindows) reasons.Add("Supported Windows client version is required.");
        if (d.RequiresAdministrator && !e.Administrator) reasons.Add("Administrator access is required; no automatic elevation was requested.");
        if (e.RestartPending) reasons.Add("Complete the pending Windows restart before repairing.");
        if (e.Conflict) reasons.Add("Another servicing operation or repair may be active.");
        if (d.RequiresNetwork && !e.NetworkApproved) reasons.Add("This action needs explicit approval to use Windows repair sources over the network.");
        foreach (var service in d.RequiredServices) if (!e.AvailableServices.Contains(service)) reasons.Add("Required service unavailable or disabled: " + service);
        return new(reasons.Count == 0, reasons);
    }
}

public static class RepairVerification
{
    public static VerificationState Compare(IReadOnlyList<DiagnosticResult> before, IReadOnlyList<DiagnosticResult> after, bool restart)
    {
        if (restart) return VerificationState.RequiresRestart;
        var targets = before.Where(r => r.Severity is FindingSeverity.Warning or FindingSeverity.Critical).ToArray();
        if (targets.Length == 0 || after.Any(r => r.Outcome != CollectionOutcome.Completed || r.Severity == FindingSeverity.Unknown)) return VerificationState.Failed;
        var pairs = targets.Select(b => (Before: b, After: after.SingleOrDefault(a => a.ModuleId == b.ModuleId && a.FindingId == b.FindingId))).ToArray();
        if (pairs.Any(p => p.After is null)) return VerificationState.Failed;
        if (pairs.Any(p => DiagnosticOrchestrator.Priority(p.After!.Severity) > DiagnosticOrchestrator.Priority(p.Before.Severity))) return VerificationState.Worse;
        if (pairs.All(p => p.After!.Severity == FindingSeverity.Healthy)) return VerificationState.Fixed;
        if (pairs.Any(p => DiagnosticOrchestrator.Priority(p.After!.Severity) < DiagnosticOrchestrator.Priority(p.Before.Severity))) return VerificationState.Improved;
        return VerificationState.Unchanged;
    }
}

/// <summary>Approval, fresh safety, journal-before-write and diagnostic verification; never executes report text.</summary>
public sealed class RepairWorkflow(IEnumerable<IRepairAction> actions, IEnumerable<IDiagnosticModule> modules,
    IRepairEnvironment environment, IRestoreProtection restore, IRepairAudit audit, IEntitlements entitlements)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly Dictionary<string, IRepairAction> actions = actions.ToDictionary(a => a.Definition.Id, StringComparer.Ordinal);
    private readonly Dictionary<string, IDiagnosticModule> modules = modules.ToDictionary(a => a.Id, StringComparer.Ordinal);
    public async Task<RepairReport> RunAsync(DiagnosticScan scan, RepairApproval approval, DiagnosticContext context,
        IProgress<string>? progress, CancellationToken token)
    {
        if (!entitlements.Allows(HankiCapability.AutomaticRepair)) throw new InvalidOperationException("Automatic repair is unavailable for this edition. Manual Community tools remain available.");
        if (approval.ScanId != scan.Id || scan.Ended > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - scan.Ended > TimeSpan.FromMinutes(30))
            throw new InvalidOperationException("Run a fresh scan and approve its proposed actions.");
        var selected = approval.ActionIds.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var eligible = scan.Results.Select(FindingAnalysis.Recommend).Where(r => r?.RepairActionId is not null).Select(r => r!.RepairActionId!).ToHashSet(StringComparer.Ordinal);
        if (selected.Any(id => !eligible.Contains(id) || !actions.ContainsKey(id))) throw new InvalidOperationException("Approval contains an unsupported action for this scan.");
        var attempts = new List<RepairAttempt>();
        await Gate.WaitAsync(token);
        try {
            foreach (var id in selected) {
                var action = actions[id]; var d = action.Definition; var started = DateTimeOffset.UtcNow;
                var before = scan.Results.Where(r => r.ModuleId == d.VerificationModuleId).ToArray();
                var protection = new RestoreResult(RestoreState.NotRequested, "No restore point requested.");
                RepairAttempt Make(RepairState state, string text, VerificationState verification = VerificationState.NotRun, IReadOnlyList<DiagnosticResult>? after = null) =>
                    new(id, started, DateTimeOffset.UtcNow, state, text, protection, verification, before, after ?? []);
                RepairAttempt attempt;
                if (token.IsCancellationRequested) { attempts.Add(Make(RepairState.Skipped, "Cancelled before this action started.")); continue; }
                progress?.Report("Checking prerequisites: " + d.Title);
                try {
                    var current = await environment.ReadAsync(token);
                    var safety = RepairSafety.Evaluate(d, current with { NetworkApproved = approval.NetworkApproved });
                    if (!safety.Allowed) attempt = Make(RepairState.Blocked, string.Join(" ", safety.Reasons));
                    else if (!modules.TryGetValue(d.VerificationModuleId, out var verificationModule)) attempt = Make(RepairState.Blocked, "Verification diagnostic is unavailable.");
                    else {
                        // Re-check the actual diagnostic immediately before repair; a stale scan cannot authorize unrelated work.
                        var fresh = await DiagnosticExecution.RunAsync(verificationModule, context, null, token);
                        if (!fresh.Any(r => FindingAnalysis.Recommend(r)?.RepairActionId == id) || fresh.Any(r => r.Outcome != CollectionOutcome.Completed))
                            attempt = Make(RepairState.Blocked, "The current evidence no longer supports this action. Run another scan.");
                        else {
                            before = fresh.ToArray();
                            if (d.RecommendRestorePoint || d.RequireRestorePoint) protection = await restore.CreateAsync(token);
                            if (protection.State != RestoreState.Created && (d.RequireRestorePoint || d.RecommendRestorePoint && !approval.AllowWithoutRestorePoint))
                                attempt = Make(RepairState.Blocked, "Restore protection was not confirmed. Review its status and explicitly approve any supported unprotected action.");
                            else {
                                token.ThrowIfCancellationRequested();
                                // Persist Pending before changes. Failure here prevents execution.
                                await audit.RecordAsync(scan.Id, Make(RepairState.Pending, "Approved action is about to execute; completion not yet recorded."), token);
                                safety = RepairSafety.Evaluate(d, (await environment.ReadAsync(token)) with { NetworkApproved = approval.NetworkApproved });
                                if (!safety.Allowed) attempt = Make(RepairState.Blocked, string.Join(" ", safety.Reasons));
                                else {
                                    progress?.Report("Applying approved action: " + d.Title);
                                    var execution = await action.ExecuteAsync(token);
                                    if (!execution.Completed) attempt = Make(RepairState.Failed, execution.Explanation);
                                    else {
                                        progress?.Report("Verifying: " + d.Title);
                                        var after = execution.RequiresRestart ? Array.Empty<DiagnosticResult>() : await DiagnosticExecution.RunAsync(verificationModule, context, null, token);
                                        attempt = Make(RepairState.Executed, execution.Explanation, RepairVerification.Compare(before, after, execution.RequiresRestart), after);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { attempt = Make(RepairState.Cancelled, "Cancelled; completed changes may remain. Review Windows state and audit history."); }
                catch (Exception) { attempt = Make(RepairState.Failed, "The action could not complete. Changes may remain; inspect Windows state before retrying."); }
                attempts.Add(attempt);
                // A terminal journal failure is visible and stops further mutations, leaving Pending evidence intact.
                try { await audit.RecordAsync(scan.Id, attempt, CancellationToken.None); }
                catch (Exception) { attempts[^1] = attempt with { Explanation = attempt.Explanation + " Final audit could not be saved. Further actions stopped." }; break; }
            }
        } finally { Gate.Release(); }
        return new(scan.Id, attempts.ToArray());
    }
}
