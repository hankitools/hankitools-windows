using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace IgezziGuard;

// Closed allowlist: titles, free-form evidence, recommendation text, IDs and metadata never enter this payload.
public sealed record SharedFinding(DiagnosticCategory Category, CollectionOutcome Outcome, FindingSeverity Severity);
public sealed record MinimizedDiagnosticReport(int Version, IReadOnlyList<SharedFinding> Findings);
public sealed record PayloadConsent(string Sha256, Uri Destination, DateTimeOffset ApprovedAt);
public static class ReviewedContext
{
    public static string Prepare(IEnumerable<DiagnosticResult> results) => JsonSerializer.Serialize(new MinimizedDiagnosticReport(1,
        results.Take(200).Select(r=>new SharedFinding(r.Category,r.Outcome,r.Severity)).ToArray()));
    public static string Fingerprint(string payload)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    public static bool Valid(string payload,PayloadConsent consent,Uri destination,DateTimeOffset now) =>
        destination.Scheme==Uri.UriSchemeHttps && string.IsNullOrEmpty(destination.UserInfo) && consent.Destination==destination &&
        consent.ApprovedAt<=now && now-consent.ApprovedAt<=TimeSpan.FromMinutes(10) && consent.Sha256==Fingerprint(payload);
}
public interface IExplanationProvider
{
    Uri Destination {get;}
    Task<string> ExplainAsync(string minimizedJson,CancellationToken token);
}
public sealed record ExplanationResponse(string Text,bool UsedProvider,string Status);
public sealed class OptionalExplanations(IExplanationProvider provider)
{
    public async Task<ExplanationResponse> ExplainAsync(IReadOnlyList<DiagnosticResult> findings,bool enabled,PayloadConsent? consent,CancellationToken token)
    {
        var fallback=string.Join("\r\n\r\n",findings.Select(r=>FindingAnalysis.Recommend(r)?.ManualAction??"Review coverage in the individual diagnostic tool. No repair decision follows from missing evidence."));
        var payload=ReviewedContext.Prepare(findings);
        if(!enabled||consent is null||!ReviewedContext.Valid(payload,consent,provider.Destination,DateTimeOffset.UtcNow))return new(fallback,false,"External explanations disabled or exact request not approved.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try {
            var answer=await provider.ExplainAsync(payload,timeout.Token).WaitAsync(timeout.Token);
            if(string.IsNullOrWhiteSpace(answer)||answer.Length>10000||answer.Any(c=>char.IsControl(c)&&c is not '\r' and not '\n' and not '\t'))return new(fallback,false,"Provider response invalid; local guidance retained.");
            // Text only: never parsed for action IDs, commands, entitlements or repair eligibility.
            return new(answer,true,"Optional explanation; deterministic findings and repair rules remain authoritative.");
        }catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
        catch{return new(fallback,false,"Provider unavailable, timed out or rate-limited; local guidance retained. No retry or extra request made.");}
    }
}
public sealed record SharedReportLink(Uri Url,DateTimeOffset Expires,string RevocationHandle);
public interface IReportSharingProvider
{
    Uri Destination {get;}
    Task<SharedReportLink> UploadAsync(string minimizedJson,DateTimeOffset expires,CancellationToken token);
    Task RevokeAsync(string revocationHandle,CancellationToken token);
}
/// <summary>Provider-neutral reviewed client. No production backend is configured or called by the app.</summary>
public sealed class ReviewedReportSharing(IReportSharingProvider provider)
{
    public async Task<SharedReportLink> ShareAsync(IReadOnlyList<DiagnosticResult> findings,PayloadConsent consent,DateTimeOffset expires,CancellationToken token)
    {
        var payload=ReviewedContext.Prepare(findings);var now=DateTimeOffset.UtcNow;
        if(!ReviewedContext.Valid(payload,consent,provider.Destination,now))throw new InvalidOperationException("Review and approve the exact minimized report and destination.");
        if(expires<=now||expires>now.AddDays(7))throw new ArgumentException("Share expiry must be within seven days.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var link=await provider.UploadAsync(payload,expires,timeout.Token).WaitAsync(timeout.Token);
        if(link.Url.Scheme!=Uri.UriSchemeHttps||!string.IsNullOrEmpty(link.Url.UserInfo)||link.Url.Authority!=provider.Destination.Authority||link.Expires>expires||link.Expires<=now||string.IsNullOrWhiteSpace(link.RevocationHandle))
            throw new IOException("Provider returned an invalid sharing result.");
        return link;
    }
    public async Task RevokeAsync(SharedReportLink link,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await provider.RevokeAsync(link.RevocationHandle,timeout.Token).WaitAsync(timeout.Token);
    }
}
