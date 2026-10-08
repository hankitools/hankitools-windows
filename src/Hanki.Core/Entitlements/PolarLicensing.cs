using System.Net;
using System.Text;
using System.Text.Json;
namespace IgezziGuard;

/// <summary>
/// Polar (polar.sh) sells Hanki Pro and Technician and issues their licence keys. Its licence-key endpoints are
/// public, so the app carries no secret. Pro is checked once, when the key is entered; Technician is a subscription,
/// so it is checked again about once a week and keeps working offline for up to 30 days after the last check.
/// </summary>
public sealed record LicenseStoreConfig(Uri ApiBase, string OrganizationId, IReadOnlyDictionary<string, HankiEdition> Benefits)
{
    public bool OnSale => OrganizationId.Length > 0 && Benefits.Count > 0;
    // Filled in when the store opens: the Polar organization id and the licence-key benefit id of each product.
    public static readonly LicenseStoreConfig Production = new(new Uri("https://api.polar.sh/"), "", new Dictionary<string, HankiEdition>());
    public static LicenseStoreConfig Current()
    {
#if DEBUG
        // Polar sandbox testing, compiled out of Release: HANKI_POLAR_ORG plus HANKI_POLAR_PRO / HANKI_POLAR_TECHNICIAN benefit ids.
        static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
        if (Env("HANKI_POLAR_ORG") is { } organization) {
            var benefits = new Dictionary<string, HankiEdition>(StringComparer.OrdinalIgnoreCase);
            if (Env("HANKI_POLAR_PRO") is { } pro) benefits[pro] = HankiEdition.Pro;
            if (Env("HANKI_POLAR_TECHNICIAN") is { } technician) benefits[technician] = HankiEdition.Technician;
            return new(new Uri(Env("HANKI_POLAR_API") ?? "https://sandbox-api.polar.sh/"), organization, benefits);
        }
#endif
        return Production;
    }
}

/// <summary>What Hanki keeps about a licence: no name, email or other customer details from Polar.</summary>
public sealed record StoredLicense(string Key, string ActivationId, HankiEdition Edition, string DisplayKey, DateTimeOffset LastChecked, DateTimeOffset? KeyExpires);
public interface ILicenseStore { StoredLicense? Read(); void Write(StoredLicense license); void Clear(); }
/// <summary>A licence problem worded for the person using the app.</summary>
public sealed class LicenseException(string message) : Exception(message);

public sealed class PolarLicenseProvider(ILicenseStore store, LicenseStoreConfig config, HttpClient http, Func<DateTimeOffset> clock) : ILicenseProvider
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromDays(7), WorksOfflineFor = TimeSpan.FromDays(30);
    internal const string NotOnSale = "Hanki Pro and Technician aren't on sale yet. The free edition keeps working as it is.";
    internal const string Offline = "Couldn't reach Polar, the store that checks licence keys. Check your internet connection and try again.";
    public LicenseStoreConfig Config => config;
    public StoredLicense? Stored { get { try { return store.Read(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.ComponentModel.Win32Exception) { return null; } } }

    /// <summary>The entitlement a stored licence gives, without network access.</summary>
    public static ValidatedLicense? Validated(StoredLicense? s)
    {
        if (s is null) return null;
        var ends = s.Edition == HankiEdition.Technician ? s.LastChecked + WorksOfflineFor : DateTimeOffset.MaxValue;
        if (s.KeyExpires is { } expires && expires < ends) ends = expires;
        return new(s.Edition, LicenseState.Active, ends, ends, s.ActivationId, SeatId: s.DisplayKey);
    }
    public static bool CheckDue(StoredLicense s, DateTimeOffset now) =>
        s.Edition == HankiEdition.Technician && (now - s.LastChecked >= CheckEvery || now < s.LastChecked);

    /// <summary>The stored licence, checked with Polar first when a check is due. Offline, the stored licence keeps working until it runs out.</summary>
    public async Task<ValidatedLicense?> RefreshAsync(CancellationToken token)
    {
        var stored = Stored;
        if (stored is not null && config.OnSale && CheckDue(stored, clock()))
            try { stored = await CheckAsync(stored, token); } catch (LicenseException) { }
        return Validated(stored);
    }

    /// <summary>Checks the key with Polar now. Returns null, and removes the licence from this PC, when Polar no longer accepts it.</summary>
    public async Task<StoredLicense?> CheckAsync(StoredLicense stored, CancellationToken token)
    {
        var key = await ValidateAsync(stored.Key, stored.ActivationId, token);
        if (key is null || !config.Benefits.TryGetValue(key.Value.BenefitId, out var edition)) { store.Clear(); return null; }
        var updated = stored with { Edition = edition, DisplayKey = key.Value.DisplayKey, LastChecked = clock(), KeyExpires = key.Value.Expires };
        store.Write(updated);
        return updated;
    }

    public async Task<StoredLicense> ActivateAsync(string text, CancellationToken token)
    {
        if (!config.OnSale) throw new LicenseException(NotOnSale);
        var key = NormalizeKey(text);
        var (status, body) = await PostAsync("activate", new { key, organization_id = config.OrganizationId, label = ActivationLabel(clock()) }, token);
        if (status == HttpStatusCode.NotFound) throw new LicenseException("Polar doesn't recognise that key. Check that you pasted the whole key from your receipt email.");
        if (status == HttpStatusCode.Forbidden) throw new LicenseException("This key can't be activated here. It may already be in use on as many PCs as it allows (remove it from one on its Hanki Pro page), or it may have been cancelled or refunded.");
        var activationId = Text(body, "id") ?? throw new LicenseException(Unexpected(status));
        var validated = await ValidateAsync(key, activationId, token) ?? throw new LicenseException("Polar didn't accept that key. Check your purchase on Polar, or contact hello@hanki.tools.");
        if (!config.Benefits.TryGetValue(validated.BenefitId, out var edition)) {
            try { await DeactivateAsync(key, activationId, token); } catch (LicenseException) { }
            throw new LicenseException("That key is for a different product, not Hanki Pro or Technician.");
        }
        var stored = new StoredLicense(key, activationId, edition, validated.DisplayKey, clock(), validated.Expires);
        store.Write(stored);
        return stored;
    }

    /// <summary>Removes the licence from this PC and frees its activation on Polar. The local copy is removed even when Polar can't be reached.</summary>
    public async Task RevokeDeviceAsync(CancellationToken token)
    {
        var stored = Stored;
        if (stored is null) return;
        try { if (config.OnSale) await DeactivateAsync(stored.Key, stored.ActivationId, token); }
        finally { store.Clear(); }
    }

    internal static string NormalizeKey(string text)
    {
        var key = text.Trim();
        if (key.Length is 0 or > 200 || key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new LicenseException("Paste the licence key exactly as it appears in your receipt email.");
        return key;
    }
    // Shown in the buyer's Polar purchase page; deliberately without the PC's name.
    internal static string ActivationLabel(DateTimeOffset now) => $"Windows PC, activated {now:yyyy-MM-dd}";

    private readonly record struct ValidKey(string BenefitId, string DisplayKey, DateTimeOffset? Expires);
    private async Task<ValidKey?> ValidateAsync(string key, string activationId, CancellationToken token)
    {
        var (status, body) = await PostAsync("validate", new { key, organization_id = config.OrganizationId, activation_id = activationId }, token);
        if (status == HttpStatusCode.NotFound) return null;
        if (Text(body, "status") != "granted" || Text(body, "benefit_id") is not { } benefit) throw new LicenseException(Unexpected(status));
        DateTimeOffset? expires = Text(body, "expires_at") is { } value && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
        return new ValidKey(benefit, Text(body, "display_key") ?? "", expires);
    }
    private async Task DeactivateAsync(string key, string activationId, CancellationToken token)
    {
        var (status, _) = await PostAsync("deactivate", new { key, organization_id = config.OrganizationId, activation_id = activationId }, token);
        if (status is not (HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.NotFound)) throw new LicenseException(Unexpected(status));
    }

    private async Task<(HttpStatusCode Status, JsonElement? Body)> PostAsync(string action, object body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(config.ApiBase, "v1/customer-portal/license-keys/" + action)) {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); }
        catch (HttpRequestException) { throw new LicenseException(Offline); }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { throw new LicenseException(Offline); }
        using (response) {
            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests) throw new LicenseException(Unexpected(response.StatusCode));
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity) throw new LicenseException("That doesn't look like a Hanki licence key. Paste it exactly as it appears in your receipt email.");
            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            if (bytes.Length > 64_000) throw new LicenseException(Unexpected(response.StatusCode));
            if (bytes.Length == 0 || !response.IsSuccessStatusCode) return (response.StatusCode, null);
            try { using var document = JsonDocument.Parse(bytes); return (response.StatusCode, document.RootElement.Clone()); }
            catch (JsonException) { throw new LicenseException(Unexpected(response.StatusCode)); }
        }
    }
    private static string? Text(JsonElement? body, string name) =>
        body is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string Unexpected(HttpStatusCode status) => $"Polar gave an unexpected answer (HTTP {(int)status}). Try again in a few minutes; if it keeps happening, contact hello@hanki.tools.";
}

/// <summary>The app's licence, loaded from Windows' credential store at startup without network access.</summary>
public static class AppLicensing
{
    public static PolarLicenseProvider? Provider { get; private set; }
    public static LicensingSession? Session { get; private set; }
    public static event Action? Changed;
    public static void Start(PolarLicenseProvider provider)
    {
        Provider = provider; Session = new LicensingSession(provider);
        Session.Use(PolarLicenseProvider.Validated(provider.Stored));
    }
    /// <summary>Only contacts Polar when a Technician licence is due for its weekly check.</summary>
    public static async Task RefreshAsync(CancellationToken token)
    {
        if (Session is null) return;
        await Session.RefreshAsync(token);
        Changed?.Invoke();
    }
    public static void Use(StoredLicense? license) { Session?.Use(PolarLicenseProvider.Validated(license)); Changed?.Invoke(); }
    public static string Name(HankiEdition edition) => edition switch {
        HankiEdition.Pro => "Hanki Pro", HankiEdition.Technician => "Hanki Technician", _ => "Hanki Community (free)"
    };
}
