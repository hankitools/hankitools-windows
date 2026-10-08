namespace IgezziGuard;

public enum HankiCapability { ExistingTools, FullSystemScan, RawFindings, ManualGuidance, LocalHistory, AutomaticRepair, ScheduledChecks, TechnicianSessions, CustomerReports }
public enum HankiEdition { Community, Pro, Technician }
public interface IEntitlements { bool Allows(HankiCapability capability); }
public sealed class EditionEntitlements(HankiEdition edition) : IEntitlements
{
    public bool Allows(HankiCapability capability) => capability switch {
        HankiCapability.ExistingTools or HankiCapability.FullSystemScan or HankiCapability.RawFindings or HankiCapability.ManualGuidance or HankiCapability.LocalHistory => true,
        HankiCapability.AutomaticRepair or HankiCapability.ScheduledChecks => edition is HankiEdition.Pro or HankiEdition.Technician,
        HankiCapability.TechnicianSessions or HankiCapability.CustomerReports => edition == HankiEdition.Technician,
        _ => false
    };
}
public static class EntitlementComposition
{
    public static IEntitlements Current()
    {
#if DEBUG
        // Compiled out of Release, including the environment variable lookup itself.
        if (Enum.TryParse<HankiEdition>(Environment.GetEnvironmentVariable("HANKI_DEVELOPMENT_EDITION"), true, out var edition) && Enum.IsDefined(edition))
            return new EditionEntitlements(edition);
#endif
        return (IEntitlements?)AppLicensing.Session ?? new EditionEntitlements(HankiEdition.Community);
    }
}
public enum LicenseState { Active, OfflineGrace, Expired, Revoked, Unavailable }
/// <summary>Only a trusted provider after signature/issuer/audience/device validation may supply these claims.</summary>
public sealed record ValidatedLicense(HankiEdition Edition, LicenseState State, DateTimeOffset Expires,
    DateTimeOffset GraceUntil, string DeviceRegistration, string? OrganizationId = null, string? SeatId = null);
public interface ILicenseProvider
{
    Task<ValidatedLicense?> RefreshAsync(CancellationToken token);
    Task RevokeDeviceAsync(CancellationToken token);
}
public sealed class LicenseEntitlements(ValidatedLicense? license, Func<DateTimeOffset> clock) : IEntitlements
{
    public bool Allows(HankiCapability capability)
    {
        var now = clock();
        bool valid = license is not null && !string.IsNullOrWhiteSpace(license.DeviceRegistration) &&
            (license.State == LicenseState.Active && now < license.Expires || license.State == LicenseState.OfflineGrace && now < license.GraceUntil && license.GraceUntil >= license.Expires);
        return new EditionEntitlements(valid ? license!.Edition : HankiEdition.Community).Allows(capability);
    }
}
/// <summary>
/// The app wires the Polar provider (AppLicensing). The licence is kept in Windows Credential Manager, never in an editable
/// file; Hanki is MIT-licensed, so the check serves honest buyers and is not copy protection.
/// </summary>
public sealed class LicensingSession(ILicenseProvider provider) : IEntitlements
{
    private ValidatedLicense? license;
    /// <summary>Uses an already stored licence without asking the provider (startup, or right after activation).</summary>
    public void Use(ValidatedLicense? stored) => license = stored;
    public bool Allows(HankiCapability capability) => new LicenseEntitlements(license, () => DateTimeOffset.UtcNow).Allows(capability);
    public async Task RefreshAsync(CancellationToken token)
    {
        try { license = await provider.RefreshAsync(token); }
        catch (OperationCanceledException) { throw; }
        catch { license = null; }
    }
    public async Task LogoutAsync(CancellationToken token) { license = null; await provider.RevokeDeviceAsync(token); }
}
