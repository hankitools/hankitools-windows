using IgezziGuard;

internal static class RepairChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    internal static async Task Run(string root)
    {
        var now = DateTimeOffset.UtcNow;
        DiagnosticResult Result(FindingSeverity severity) => new("sfc", "integrity", DiagnosticCategory.Windows, CollectionOutcome.Completed, severity, "Integrity", "fixture", now, now);
        var before = Result(FindingSeverity.Warning); var after = Result(FindingSeverity.Healthy);
        var scan = new DiagnosticScan(Guid.NewGuid(), now, now, 1, 1, false, [before]);
        var definition = new RepairDefinition("sfc-repair", "Test repair", "fixture only", RepairRisk.Moderate, true, false, true, false, ["service"], "sfc", "fixture undo");
        var env = new RepairEnvironment(true, true, false, false, new HashSet<string>{"service"}, false);
        foreach (var blocked in new[] { env with {SupportedWindows=false}, env with {Administrator=false}, env with {RestartPending=true}, env with {Conflict=true}, env with {AvailableServices=new HashSet<string>()} })
            Check(!RepairSafety.Evaluate(definition, blocked).Allowed, "repair prerequisite blocks " + blocked);
        Check(RepairSafety.Evaluate(definition, env).Allowed, "repair prerequisites pass");
        Check(RepairVerification.Compare([before],[after],false)==VerificationState.Fixed, "verification fixed only from matched diagnostic");
        Check(RepairVerification.Compare([before],[before],false)==VerificationState.Unchanged, "successful command with unchanged evidence not fixed");
        Check(RepairVerification.Compare([before],[],false)==VerificationState.Failed, "missing verification is failure");
        Check(RepairVerification.Compare([before],[],true)==VerificationState.RequiresRestart, "restart verification deferred");
        Check(RepairVerification.Compare([before],[Result(FindingSeverity.Critical)],false)==VerificationState.Worse, "worsened verification visible");
        Check(RepairVerification.Compare([Result(FindingSeverity.Critical)],[before],false)==VerificationState.Improved, "partial improvement visible");
        var approval = new RepairApproval(scan.Id, new HashSet<string>{"sfc-repair"}, false, false);
        async Task<(RepairReport Report,int Calls)> Execute(RestoreState restoreState, bool failAudit=false, bool allowWithout=false, bool denied=false) {
            var action = new FakeRepair(definition); int reads=0;
            var module = new FakeModule("sfc", (_,_)=>Task.FromResult<IReadOnlyList<DiagnosticResult>>([++reads==1?before:after]));
            var flow = new RepairWorkflow([action],[module],new FixtureEnvironment(denied?env with {Administrator=false}:env),new FixtureRestore(restoreState),new FixtureAudit(failAudit));
            var report=await flow.RunAsync(scan,approval with {AllowWithoutRestorePoint=allowWithout},new(true,true,false),null,CancellationToken.None);
            return (report,action.Calls);
        }
        var success=await Execute(RestoreState.Created);Check(success.Calls==1 && success.Report.Attempts.Single().Verification==VerificationState.Fixed,"approved action executes then verifies");
        var unprotected=await Execute(RestoreState.Failed);Check(unprotected.Calls==0 && unprotected.Report.Attempts.Single().State==RepairState.Blocked,"failed restore point blocks without acknowledgement");
        Check((await Execute(RestoreState.Unavailable,allowWithout:true)).Calls==1,"explicit unprotected approval honored for optional protection");
        Check((await Execute(RestoreState.Created,failAudit:true)).Calls==0,"failed pending journal prevents mutation");
        Check((await Execute(RestoreState.Created,denied:true)).Calls==0,"non-admin repair never executes");
        var fakeAction=new FakeRepair(definition);
        var empty=new RepairWorkflow([fakeAction],[],new FixtureEnvironment(env),new FixtureRestore(RestoreState.Created),new FixtureAudit(false));
        Check((await empty.RunAsync(scan,approval with {ActionIds=new HashSet<string>()},new(true,true,false),null,CancellationToken.None)).Attempts.Count==0 && fakeAction.Calls==0,"no approval means no actions");
        try {await empty.RunAsync(scan,approval with {ScanId=Guid.NewGuid()},new(true,true,false),null,CancellationToken.None);throw new Exception("Wrong scan accepted");}catch(InvalidOperationException){}
        var path=Path.Combine(root,"diagnostics.json");var history=new DiagnosticHistory(path);history.Add(scan);
        Check(history.Read().Single().Results.Single().Evidence.Length==0,"local history excludes raw evidence");
        Check(DiagnosticHistory.Compare(scan,scan with {Results=[after]}).Single().Contains("Resolved"),"stable finding comparison");
        File.WriteAllText(path,"broken");try{history.Add(scan);throw new Exception("Corrupt history overwritten");}catch(IOException){}
        Check(File.ReadAllText(path)=="broken","corrupt diagnostic history preserved");
        var audit=new RepairAudit(Path.Combine(root,"repair-audit.json"));await audit.RecordAsync(scan.Id,success.Report.Attempts.Single(),CancellationToken.None);
        Check(audit.Read().Single().Attempt.Verification==VerificationState.Fixed,"repair verification persisted locally");
    }
}
internal sealed class FakeRepair(RepairDefinition definition) : IRepairAction
{
    public int Calls;
    public RepairDefinition Definition=>definition;
    public Task<RepairExecutionResult> ExecuteAsync(CancellationToken t){t.ThrowIfCancellationRequested();Calls++;return Task.FromResult(new RepairExecutionResult(true,false,"fixture completed"));}
}
internal sealed class FixtureEnvironment(RepairEnvironment environment) : IRepairEnvironment { public Task<RepairEnvironment> ReadAsync(CancellationToken t)=>Task.FromResult(environment); }
internal sealed class FixtureRestore(RestoreState state) : IRestoreProtection {public Task<RestoreResult> CreateAsync(CancellationToken t)=>Task.FromResult(new RestoreResult(state,"fixture protection"));}
internal sealed class FixtureAudit(bool fail) : IRepairAudit {public Task RecordAsync(Guid id,RepairAttempt attempt,CancellationToken t){if(fail)throw new IOException("fixture write failure");return Task.CompletedTask;}}
