using System.Text.Json;
using IgezziGuard;
internal static class LaterPhaseChecks
{
    internal static async Task Run(string root)
    {
        void Check(bool ok,string text)=>DiagnosticChecks.Check(ok,text);
        var now=DateTimeOffset.UtcNow;
        var xml=System.Xml.Linq.XDocument.Parse(ScheduledHealthChecks.TaskXml(Path.Combine(root,"Hanki & Tools.exe"),"fixture-user",HealthCheckFrequency.Weekly));
        System.Xml.Linq.XNamespace ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
        Check(xml.Descendants(ns+"RunLevel").Single().Value=="LeastPrivilege"&&xml.Descendants(ns+"Arguments").Single().Value=="--scheduled-health-check","schedule uses least privilege and only read-only entry point");
        Check(xml.Descendants(ns+"Command").Single().Value.EndsWith("Hanki & Tools.exe"),"schedule XML escapes executable path safely");
        var result=new DiagnosticResult("fixture","private-device-id",DiagnosticCategory.Windows,CollectionOutcome.Completed,FindingSeverity.Warning,"private-name","private-path",now,now,"password=secret","token=secret",metadata:new Dictionary<string,string>{{"machine","private-machine"}});
        var payload=ReviewedContext.Prepare([result]);Check(!payload.Contains("private")&&!payload.Contains("secret"),"shared payload allowlist excludes all raw strings");
        var provider=new FixtureExplanation();var service=new OptionalExplanations(provider);
        var consent=new PayloadConsent(ReviewedContext.Fingerprint(payload),provider.Destination,now);
        Check(!(await service.ExplainAsync([result],false,consent,CancellationToken.None)).UsedProvider&&provider.Calls==0,"disabled AI sends nothing");
        Check(!(await service.ExplainAsync([result],true,null,CancellationToken.None)).UsedProvider&&provider.Calls==0,"unapproved AI sends nothing");
        Check(!(await service.ExplainAsync([result],true,consent with {Sha256="wrong"},CancellationToken.None)).UsedProvider&&provider.Calls==0,"edited AI request invalidates consent");
        Check((await service.ExplainAsync([result],true,consent,CancellationToken.None)).UsedProvider&&provider.Calls==1,"approved minimized AI request reaches fake only");
        provider.Fail=true;Check(!(await service.ExplainAsync([result],true,consent,CancellationToken.None)).UsedProvider,"AI failure retains deterministic guidance");
        var cloud=new FixtureSharing();var sharing=new ReviewedReportSharing(cloud);
        var link=await sharing.ShareAsync([result],consent,now.AddHours(1),CancellationToken.None);await sharing.RevokeAsync(link,CancellationToken.None);
        Check(cloud.Calls==1&&cloud.Revoked,"sharing expiry and revocation provider boundary");
        try{await sharing.ShareAsync([result],consent with{ApprovedAt=now.AddHours(-1)},now.AddHours(1),CancellationToken.None);throw new Exception("Stale sharing consent accepted");}catch(InvalidOperationException){}
        Check(cloud.Calls==1,"stale consent no upload");
        var scan=new DiagnosticScan(Guid.NewGuid(),now,now,1,1,false,[result]);var technician=new TechnicianSessions(new EditionEntitlements(HankiEdition.Technician));
        var session=technician.Begin("Job 123","Device A",scan);var report=technician.Export(session,new("Fixture business","Fixture contact"),false);
        Check(!report.Contains("password=")&&report.Contains("No Hanki repair"),"customer report separates findings from repairs and omits technical data by default");
        technician.SaveLocal(Path.Combine(root,"technician.json"),session);
        Check(!File.ReadAllText(Path.Combine(root,"technician.json")).Contains("private-device-id"),"technician persisted evidence minimized");
        var seat=new TechnicianSeat("subject","organization","seat",1,now.AddDays(1),now.AddDays(2));var device=new TechnicianWorkstation("workstation","seat",false);
        Check(TechnicianSeatPolicy.CanWorkOffline(seat,device,now)&&!TechnicianSeatPolicy.CanRegister(seat,[device],now),"seat workstation limit independent of customer jobs");
        Check(!TechnicianSeatPolicy.CanWorkOffline(seat,device with{Revoked=true},now)&&!TechnicianSeatPolicy.CanWorkOffline(seat,device,now.AddDays(3)),"revoked or expired technician grace blocked");
        ActivationProduct Product(string description,int status=1,uint code=0)=>new(Guid.NewGuid().ToString(),"Windows fixture",description,status,code,0,null,null);
        foreach(var description in new[]{"RETAIL channel","OEM_DM channel","VOLUME_KMSCLIENT channel"}) {
            var rows=ActivationRules.Map(new("Windows fixture","Stopped","Manual",2,[Product(description)],[],""),now,now);
            Check(rows[0].Severity==FindingSeverity.Informational&&rows[0].Evidence.Contains(ActivationRules.Channel(description)),"activated Windows channel "+description);
            Check(rows.Single(r=>r.FindingId=="service").Severity==FindingSeverity.Informational,"stopped trigger-start licensing service not automatically failed");
        }
        Check(ActivationRules.Map(new("Windows fixture",null,null,null,[Product("RETAIL",0)],[],""),now,now)[0].Severity==FindingSeverity.Warning,"unactivated Windows warning");
        Check(ActivationRules.Explain(0xC004F210).Category=="Edition mismatch"&&ActivationRules.Explain(0xC004F074).Category=="KMS reachability","known edition and KMS activation codes");
        Check(ActivationRules.Explain(0xDEADBEEF).Category=="Unknown"&&ActivationRules.Explain(0xDEADBEEF).Code=="0xDEADBEEF","unknown activation code preserved");
        Check(ActivationRules.Map(new(null,null,null,null,null,null,null),now,now).All(r=>r.Severity!=FindingSeverity.Healthy),"missing activation data never healthy");
        const string key="ABCDE-FGHIJ-KLMNO-PQRST-UVWXY";
        var privacyRows=ActivationRules.Map(new(key,null,null,null,[Product("localized unknown",0) with{Name=key}],[],""),now,now);
        Check(!JsonSerializer.Serialize(privacyRows).Contains(key),"unexpected full key masked before display or persistence");
        Check(!ActivationDiagnostic.Script.Contains("OA3xOriginalProductKey")&&!ActivationDiagnostic.Script.Contains("SELECT *"),"activation queries never request full-key properties");
        var fake=new FixtureProbe {Json=JsonSerializer.Serialize(new ActivationSnapshot("Windows fixture","Running","Auto",1,[Product("VOLUME_KMSCLIENT")],[],""))};
        var kms=await DiagnosticExecution.RunAsync(new ActivationDiagnostic(fake),new(true,false,false),null,CancellationToken.None);
        Check(kms.Single(r=>r.FindingId=="kms-network").Outcome==CollectionOutcome.Unavailable,"KMS network checks require consent");
        fake.Json=JsonSerializer.Serialize(new ActivationSnapshot("Windows fixture","Running","Auto",1,[Product("RETAIL")],[],""));
        Check(!(await DiagnosticExecution.RunAsync(new ActivationDiagnostic(fake),new(true,false,true),null,CancellationToken.None)).Any(r=>r.FindingId=="kms-network"),"Retail activation never triggers KMS probes");
        Check(!ActivationRules.Host("host';evil")&&ActivationRules.Host("kms.example.test"),"KMS host validation rejects script metacharacters");
    }
}
internal sealed class FixtureExplanation:IExplanationProvider
{
    public int Calls;public bool Fail;public Uri Destination=>new("https://fixture.invalid/reports");
    public Task<string> ExplainAsync(string json,CancellationToken token){Calls++;if(Fail)throw new IOException("fixture provider failure");return Task.FromResult("Fixture explanation only.");}
}
internal sealed class FixtureSharing:IReportSharingProvider
{
    public int Calls;public bool Revoked;public Uri Destination=>new("https://fixture.invalid/reports");
    public Task<SharedReportLink> UploadAsync(string payload,DateTimeOffset expires,CancellationToken token){Calls++;return Task.FromResult(new SharedReportLink(new("https://fixture.invalid/r/random"),expires,"fixture-revocation"));}
    public Task RevokeAsync(string handle,CancellationToken token){Revoked=true;return Task.CompletedTask;}
}
