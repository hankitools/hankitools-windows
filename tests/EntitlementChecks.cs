using IgezziGuard;
internal static class EntitlementChecks
{
    internal static async Task Run()
    {
        void Check(bool ok,string text)=>DiagnosticChecks.Check(ok,text);
        var community=new EditionEntitlements(HankiEdition.Community);var pro=new EditionEntitlements(HankiEdition.Pro);
        foreach(var feature in new[]{HankiCapability.ExistingTools,HankiCapability.FullSystemScan,HankiCapability.RawFindings,HankiCapability.ManualGuidance,HankiCapability.LocalHistory})Check(community.Allows(feature),"Community preserves "+feature);
        Check(!community.Allows(HankiCapability.AutomaticRepair)&&pro.Allows(HankiCapability.AutomaticRepair),"automation additive entitlement");
        Check(!pro.Allows(HankiCapability.CustomerReports)&&new EditionEntitlements(HankiEdition.Technician).Allows(HankiCapability.CustomerReports),"technician capabilities separate");
        var now=DateTimeOffset.UtcNow;var license=new ValidatedLicense(HankiEdition.Pro,LicenseState.Active,now.AddDays(1),now.AddDays(2),"fixture-device");
        Check(new LicenseEntitlements(license,()=>now).Allows(HankiCapability.AutomaticRepair),"active validated license");
        foreach(var state in new[]{LicenseState.Revoked,LicenseState.Expired,LicenseState.Unavailable}){
            var source=new LicenseEntitlements(license with {State=state},()=>now);Check(!source.Allows(HankiCapability.AutomaticRepair)&&source.Allows(HankiCapability.FullSystemScan),"license fallback "+state);
        }
        Check(new LicenseEntitlements(license with {State=LicenseState.OfflineGrace},()=>now.AddDays(1.5)).Allows(HankiCapability.AutomaticRepair),"bounded offline grace");
        Check(!new LicenseEntitlements(license with {State=LicenseState.OfflineGrace},()=>now.AddDays(3)).Allows(HankiCapability.AutomaticRepair),"expired grace cannot unlock");
#if !DEBUG
        Environment.SetEnvironmentVariable("HANKI_DEVELOPMENT_EDITION","Technician");
        Check(!EntitlementComposition.Current().Allows(HankiCapability.AutomaticRepair),"Release build ignores development entitlement switch");
        Environment.SetEnvironmentVariable("HANKI_DEVELOPMENT_EDITION",null);
#endif
        var secrets=new FixtureSecretStore();var provider=new FixtureIdentity();var session=new IdentitySession(provider,secrets);
        Check(session.Current.State==IdentityState.Anonymous,"anonymous identity default");
        await session.SignInAsync(CancellationToken.None);Check(session.Current.State==IdentityState.Authenticated&&secrets.Read() is not null,"authenticated session persisted through secret abstraction");
        provider.State=IdentityState.Revoked;await session.RefreshAsync(CancellationToken.None);Check(session.Current.State==IdentityState.Revoked&&secrets.Read() is null,"revoked identity clears credentials");
        provider.State=IdentityState.Authenticated;await session.SignInAsync(CancellationToken.None);await session.LogoutAsync(CancellationToken.None);Check(secrets.Read() is null&&session.Current.State==IdentityState.Anonymous,"logout clears local credentials");
        Check(!new SessionCredential("fixture-secret").ToString().Contains("fixture-secret"),"credential not included in generated text");
    }
}
internal sealed class FixtureSecretStore:ISessionSecretStore
{
    private SessionCredential? value;public SessionCredential? Read()=>value;public void Write(SessionCredential credential)=>value=credential;public void Clear()=>value=null;
}
internal sealed class FixtureIdentity:IIdentityProvider
{
    public IdentityState State=IdentityState.Authenticated;
    private (IdentityContext,SessionCredential) Result()=>new(new(State,"fixture-subject",Expires:DateTimeOffset.UtcNow.AddMinutes(5)),new("fixture-refresh"));
    public Task<(IdentityContext Identity,SessionCredential Refresh)> AuthenticateInBrowserAsync(CancellationToken t)=>Task.FromResult(Result());
    public Task<(IdentityContext Identity,SessionCredential Refresh)> RefreshAsync(SessionCredential c,CancellationToken t)=>Task.FromResult(Result());
    public Task RevokeAsync(SessionCredential c,CancellationToken t)=>Task.CompletedTask;
}
