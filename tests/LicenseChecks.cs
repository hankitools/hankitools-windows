using System.Net;
using System.Text;
using System.Text.Json;
using IgezziGuard;

// Hanki Pro / Technician licence keys against a fake Polar API: no network, no Windows credential store.
internal static class LicenseChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    private static readonly LicenseStoreConfig Store = new(new Uri("https://polar.test/"), "org-1",
        new Dictionary<string, HankiEdition> { ["benefit-pro"] = HankiEdition.Pro, ["benefit-tech"] = HankiEdition.Technician });
    private static string Key(string benefit, string? expires = null) => JsonSerializer.Serialize(new {
        id = "key-1", status = "granted", benefit_id = benefit, display_key = "****-AB12", expires_at = expires,
        customer = new { email = "buyer@example.com", name = "Buyer Name" }, activation = new { id = "act-1" } });
    private const string Activation = """{"id":"act-1","license_key_id":"key-1","label":"Windows PC"}""";

    internal static async Task Run()
    {
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        (PolarLicenseProvider Provider, FakePolar Polar, MemoryLicenseStore Saved) Make(string benefit = "benefit-pro", LicenseStoreConfig? config = null, string? expires = null)
        {
            var polar = new FakePolar { Respond = (path, _) => path.EndsWith("/activate") ? (HttpStatusCode.OK, Activation)
                : path.EndsWith("/validate") ? (HttpStatusCode.OK, Key(benefit, expires)) : (HttpStatusCode.NoContent, null) };
            var saved = new MemoryLicenseStore();
            return (new PolarLicenseProvider(saved, config ?? Store, new HttpClient(polar), () => now), polar, saved);
        }
        async Task<string> Fails(Func<Task> action) { try { await action(); return "(no error)"; } catch (LicenseException ex) { return ex.Message; } }

        var (closed, closedPolar, _) = Make(config: LicenseStoreConfig.Production);
        Check((await Fails(() => closed.ActivateAsync("KEY-1", default))).Contains("aren't on sale") && closedPolar.Requests.Count == 0, "licence: nothing is sent while the store isn't open");

        var (pro, proPolar, proSaved) = Make();
        var stored = await pro.ActivateAsync("  HANKI-PRO-1234  ", default);
        Check(stored.Edition == HankiEdition.Pro && proPolar.Requests.Select(r => r.Path).SequenceEqual(["/v1/customer-portal/license-keys/activate", "/v1/customer-portal/license-keys/validate"]), "licence: activation then validation");
        Check(proPolar.Requests[0].Body.Contains("\"key\":\"HANKI-PRO-1234\"") && proPolar.Requests[0].Body.Contains("\"organization_id\":\"org-1\"") && proPolar.Requests[1].Body.Contains("\"activation_id\":\"act-1\""), "licence: trimmed key, organization and activation are sent");
        Check(!proPolar.Requests.Any(r => r.Body.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase) || r.Body.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase)), "licence: the PC and user names are not sent");
        Check(proSaved.Json is { } json && !json.Contains("buyer@example.com") && !json.Contains("Buyer Name"), "licence: customer details from Polar are not kept");
        var proLicense = new LicenseEntitlements(PolarLicenseProvider.Validated(proSaved.Value), () => now.AddYears(3));
        Check(proLicense.Allows(HankiCapability.AutomaticRepair) && proLicense.Allows(HankiCapability.ScheduledChecks) && !proLicense.Allows(HankiCapability.CustomerReports), "licence: Pro unlocks repairs and scheduling, not customer reports");
        now = now.AddDays(100);
        await pro.RefreshAsync(default);
        Check(proPolar.Requests.Count == 2, "licence: a one-time Pro key is never checked again automatically");

        foreach (var (text, bad) in new[] { ("", "empty"), ("HANKI PRO", "with a space"), (new string('A', 201), "too long") })
            Check((await Fails(() => Make().Provider.ActivateAsync(text, default))).Contains("exactly as it appears"), "licence: key " + bad + " is refused before sending");
        foreach (var (status, expected) in new[] { (HttpStatusCode.NotFound, "doesn't recognise"), (HttpStatusCode.Forbidden, "can't be activated here"),
            (HttpStatusCode.UnprocessableEntity, "doesn't look like"), (HttpStatusCode.InternalServerError, "HTTP 500") }) {
            var (provider, polar, saved) = Make(); polar.Respond = (_, _) => (status, null);
            Check((await Fails(() => provider.ActivateAsync("KEY-1", default))).Contains(expected) && saved.Value is null, $"licence: activation answer {(int)status} is explained and nothing is stored");
        }
        var (offline, offlinePolar, _) = Make(); offlinePolar.Offline = true;
        Check(await Fails(() => offline.ActivateAsync("KEY-1", default)) == PolarLicenseProvider.Offline, "licence: no connection is explained");

        var (other, otherPolar, otherSaved) = Make("benefit-someone-else");
        Check((await Fails(() => other.ActivateAsync("KEY-1", default))).Contains("different product") && otherSaved.Value is null
            && otherPolar.Requests[^1].Path.EndsWith("/deactivate"), "licence: a key for another product is refused and its activation released");

        var (technician, techPolar, techSaved) = Make("benefit-tech");
        await technician.ActivateAsync("HANKI-TECH-1", default);
        IEntitlements Now() => new LicenseEntitlements(PolarLicenseProvider.Validated(techSaved.Value), () => now);
        Check(Now().Allows(HankiCapability.CustomerReports), "licence: Technician unlocks customer reports");
        now = now.AddDays(3); await technician.RefreshAsync(default);
        Check(techPolar.Requests.Count == 2, "licence: Technician isn't checked again within a week");
        now = now.AddDays(5); await technician.RefreshAsync(default);
        Check(techPolar.Requests.Count == 3 && techSaved.Value!.LastChecked == now, "licence: Technician is checked again after a week");
        techPolar.Offline = true; now = now.AddDays(20); await technician.RefreshAsync(default);
        Check(Now().Allows(HankiCapability.CustomerReports) && techSaved.Value is not null, "licence: offline, Technician keeps working");
        now = now.AddDays(11); await technician.RefreshAsync(default);
        Check(!Now().Allows(HankiCapability.CustomerReports) && Now().Allows(HankiCapability.FullSystemScan), "licence: Technician stops after 30 days without a check; free tools stay");
        techPolar.Offline = false; await technician.RefreshAsync(default);
        Check(Now().Allows(HankiCapability.CustomerReports), "licence: a successful check brings Technician back");
        techPolar.Respond = (_, _) => (HttpStatusCode.NotFound, null); now = now.AddDays(8); await technician.RefreshAsync(default);
        Check(techSaved.Value is null && !Now().Allows(HankiCapability.AutomaticRepair), "licence: a cancelled subscription is removed at the next check");

        var (expiring, _, expiringSaved) = Make("benefit-tech", expires: now.AddDays(10).ToString("o"));
        await expiring.ActivateAsync("KEY-1", default);
        Check(PolarLicenseProvider.Validated(expiringSaved.Value)!.Expires == now.AddDays(10), "licence: a key's own expiry date is respected");

        var (removed, removedPolar, removedSaved) = Make();
        await removed.ActivateAsync("KEY-1", default);
        await removed.RevokeDeviceAsync(default);
        Check(removedSaved.Value is null && removedPolar.Requests[^1].Path.EndsWith("/deactivate") && removedPolar.Requests[^1].Body.Contains("act-1"), "licence: removing frees the activation on Polar");
        var (unreachable, unreachablePolar, unreachableSaved) = Make();
        await unreachable.ActivateAsync("KEY-1", default); unreachablePolar.Offline = true;
        Check(await Fails(() => unreachable.RevokeDeviceAsync(default)) == PolarLicenseProvider.Offline && unreachableSaved.Value is null, "licence: offline removal still removes it from this PC and says so");

        AppLicensing.Start(pro);
        Check(EntitlementComposition.Current().Allows(HankiCapability.AutomaticRepair), "licence: the app's entitlements follow the stored licence");
        AppLicensing.Use(null);
        Check(!EntitlementComposition.Current().Allows(HankiCapability.AutomaticRepair), "licence: removing the licence returns to the free edition");
    }

}

internal sealed class FakePolar : HttpMessageHandler
{
    public readonly List<(string Path, string Body)> Requests = [];
    public Func<string, string, (HttpStatusCode Status, string? Json)> Respond = (_, _) => (HttpStatusCode.NotFound, null);
    public bool Offline;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
        Requests.Add((request.RequestUri!.AbsolutePath, body));
        if (Offline) throw new HttpRequestException("Fixture: no connection.");
        var (status, json) = Respond(request.RequestUri.AbsolutePath, body);
        return new HttpResponseMessage(status) { Content = new StringContent(json ?? "", Encoding.UTF8, "application/json") };
    }
}
internal sealed class MemoryLicenseStore : ILicenseStore
{
    public StoredLicense? Value; public string? Json;
    public StoredLicense? Read() => Value;
    public void Write(StoredLicense license) { Value = license; Json = JsonSerializer.Serialize(license); }
    public void Clear() => Value = null;
}
