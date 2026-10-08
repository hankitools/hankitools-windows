namespace IgezziGuard.Shell;

/// <summary>
/// The full Fix my PC scan and its repair review, with no UI of its own: the native page, the footer's running list and
/// "Cancel tasks" all use this one object. It mirrors what <see cref="FullScanPanel"/> does, with the same read-only wording.
/// </summary>
internal sealed class FullScanController
{
    private readonly IEntitlements entitlements = EntitlementComposition.Current();
    private readonly DiagnosticHistory history = new(Path.Combine(SecurityPaths.Root, "diagnostic-history.json"));
    private CancellationTokenSource? pending;

    /// <summary>The newest scan of this session, or null until one has run.</summary>
    internal DiagnosticScan? Latest { get; private set; }
    /// <summary>Repairs run from <see cref="Latest"/>; they stop further proposals from it and go into the customer report.</summary>
    internal RepairReport? Repairs { get; private set; }
    internal bool IsBusy { get; private set; }
    /// <summary>What the scan is doing now, or the outcome of the last operation.</summary>
    internal string Message { get; private set; } = "";
    /// <summary>Completed and planned checks while a scan runs, otherwise null.</summary>
    internal (int Done, int Total)? Progress { get; private set; }
    internal string Activity { get; private set; } = "";
    internal bool IsAdministrator => FullScanPanel.Context(false).IsAdministrator;
    internal bool CanReportForCustomers => entitlements.Allows(HankiCapability.CustomerReports);
    internal bool CanRepair => entitlements.Allows(HankiCapability.AutomaticRepair);
    /// <summary>Raised on the UI thread whenever any property above changed.</summary>
    internal event Action? Changed;

    private void Notify() => Changed?.Invoke();
    internal void Cancel() { try { pending?.Cancel(); } catch (ObjectDisposedException) { } }

    /// <summary>Runs a full scan. <paramref name="confirmProbes"/> is asked only when network probes are included; false stops before anything runs.</summary>
    internal async Task StartAsync(bool includeNetworkProbes, Func<bool> confirmProbes)
    {
        if (IsBusy) return;
        if (includeNetworkProbes && !confirmProbes()) return;
        Latest = null; Repairs = null;
        var context = FullScanPanel.Context(includeNetworkProbes);
        var progress = new Progress<ScanProgressUpdate>(p => { if (IsBusy) { Progress = (p.CompletedModules, p.TotalModules); Activity = p.Activity; Notify(); } });
        await RunAsync(async token => {
            var scan = await new DiagnosticOrchestrator(WindowsDiagnosticCatalog.Create(includeExternal: includeNetworkProbes)).ScanAsync(context, progress, token);
            Latest = scan;
            string summary = FullScanPanel.Summary(scan);
            try { history.Add(scan); } catch { summary += "\r\nHistory could not be saved. Current results remain available; existing history was preserved."; }
            return summary;
        }, readOnly: true);
    }

    /// <summary>Reviews and, after approval, runs repairs proposed by the latest scan. Returns when finished or declined.</summary>
    internal async Task ReviewRepairsAsync(IWin32Window owner)
    {
        if (IsBusy) return;
        void Say(string text) { Message = text; Notify(); }
        var latest = Latest;
        if (latest is null) { Say("Run a full scan first. Manual tools remain available in each module."); return; }
        if (!CanRepair) {
            Say("Automatic repair is part of Hanki Pro. See the Hanki Pro page in the sidebar.\r\n\r\nYour scan, findings and manual guidance stay free. Select a finding to see its manual next steps.");
            return;
        }
        if (RepairGuidance.Stale(latest, Repairs is not null, DateTimeOffset.UtcNow) is { } stale) { Say(stale); return; }
        var ids = latest.Results.Select(FindingAnalysis.Recommend).Where(r => r?.RepairActionId is not null).Select(r => r!.RepairActionId).ToHashSet();
        var actions = WindowsServicingRepair.Catalog().Where(a => ids.Contains(a.Definition.Id)).ToArray();
        if (actions.Length == 0) { Say(RepairGuidance.NoProposals(latest)); return; }
        bool administrator = IsAdministrator;
        if (RepairGuidance.NoneAvailable(actions.Select(a => a.Definition).ToList(), administrator) is { } blocked) { Say(blocked); return; }
        if (RepairReviewDialog.Show(owner, latest, actions, administrator) is not { } approved) return;
        var audit = new RepairAudit(Path.Combine(SecurityPaths.Root, "repair-audit.json"));
        var workflow = new RepairWorkflow(WindowsServicingRepair.Catalog(), WindowsDiagnosticCatalog.Create(includeExternal: true), new WindowsRepairEnvironment(), new WindowsRestoreProtection(), audit, entitlements);
        var progress = new Progress<string>(text => { if (IsBusy) Say(text); });
        RepairReport? report = null;
        await RunAsync(async token => RepairGuidance.Results(report = await workflow.RunAsync(latest, approved, FullScanPanel.Context(approved.NetworkApproved), progress, token)), readOnly: false);
        // Repair evidence is historical; a new scan is required for another proposal.
        if (report is not null && Latest == latest) Repairs = report;
        Notify();
    }

    private async Task RunAsync(Func<CancellationToken, Task<string>> work, bool readOnly)
    {
        using var cts = new CancellationTokenSource(); pending = cts;
        IsBusy = true; Progress = null; Activity = ""; Message = "Collecting results. Your previous report remains available after this operation."; Notify();
        try { Message = await Task.Run(() => work(cts.Token), cts.Token); }
        catch (OperationCanceledException) {
            Message = readOnly ? "Cancelled. Nothing on your PC was changed; incomplete results were discarded. Run it again when you're ready."
                : "Cancelled. Any completed actions remain in effect. Check Recovery and the relevant Windows status before retrying.";
        }
        catch (Exception ex) {
            Message = "Operation stopped: " + ex.Message + "\r\nFor a setting change, inspect Recovery before retrying. Access denied may require running Hanki as administrator; Hanki does not auto-elevate.";
        }
        finally { pending = null; IsBusy = false; Progress = null; Activity = ""; Notify(); }
    }
}
