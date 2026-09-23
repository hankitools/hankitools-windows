namespace IgezziGuard;

public enum IdentityState { Anonymous, Authenticated, Expired, Revoked }
public sealed record IdentityContext(IdentityState State, string? Subject = null, string? DeviceRegistration = null,
    string? OrganizationId = null, string? SeatId = null, DateTimeOffset? Expires = null);
// Secret is intentionally a class without a generated ToString containing its value.
public sealed class SessionCredential(string value) { public string Value { get; } = value; public override string ToString() => "[session credential]"; }
public interface ISessionSecretStore { SessionCredential? Read(); void Write(SessionCredential credential); void Clear(); }
public interface IIdentityProvider
{
    // Provider must use system-browser OAuth/PKCE or equivalent, validate callback state, issuer and nonce.
    Task<(IdentityContext Identity, SessionCredential Refresh)> AuthenticateInBrowserAsync(CancellationToken token);
    Task<(IdentityContext Identity, SessionCredential Refresh)> RefreshAsync(SessionCredential credential, CancellationToken token);
    Task RevokeAsync(SessionCredential credential, CancellationToken token);
}
public sealed class IdentitySession(IIdentityProvider provider, ISessionSecretStore store)
{
    public IdentityContext Current { get; private set; } = new(IdentityState.Anonymous);
    public async Task SignInAsync(CancellationToken token)
    {
        var result = await provider.AuthenticateInBrowserAsync(token);
        Accept(result.Identity, result.Refresh);
    }
    private void Accept(IdentityContext identity, SessionCredential credential)
    {
        if (identity.State != IdentityState.Authenticated || string.IsNullOrWhiteSpace(identity.Subject) || identity.Expires is not { } expires || expires <= DateTimeOffset.UtcNow)
        { store.Clear(); Current = new(identity.State == IdentityState.Revoked ? IdentityState.Revoked : IdentityState.Expired); return; }
        Current = new(IdentityState.Anonymous);
        try { store.Write(credential); Current = identity; }
        catch { store.Clear(); throw; }
    }
    public async Task RefreshAsync(CancellationToken token)
    {
        var credential = store.Read();
        if (credential is null) { Current = new(IdentityState.Anonymous); return; }
        try { var result = await provider.RefreshAsync(credential, token); Accept(result.Identity, result.Refresh); }
        catch (OperationCanceledException) { throw; }
        catch { store.Clear(); Current = new(IdentityState.Expired); }
    }
    public async Task LogoutAsync(CancellationToken token)
    {
        var credential = store.Read();
        store.Clear(); Current = new(IdentityState.Anonymous);
        // Local credential is gone even if revocation is offline; provider must enforce server expiry/revocation.
        if (credential is not null) await provider.RevokeAsync(credential, token);
    }
}
