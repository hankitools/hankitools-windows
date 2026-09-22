using System.Text.Json;
using IgezziGuard;

internal static class DiagnosticChecks
{
    internal static void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL " + text); Console.WriteLine("PASS " + text); }
    internal static DiagnosticResult Result(CollectionOutcome outcome = CollectionOutcome.Completed, FindingSeverity severity = FindingSeverity.Healthy, string module = "fixture") =>
        new(module, "health", DiagnosticCategory.Windows, outcome, severity, "Fixture", "Fixture explanation", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1));
    internal static void Models()
    {
        foreach (var severity in Enum.GetValues<FindingSeverity>()) Check(Result(severity: severity).Severity == severity, "completed severity " + severity);
        foreach (var outcome in Enum.GetValues<CollectionOutcome>()) {
            var item = Result(outcome, FindingSeverity.Unknown);
            var restored = JsonSerializer.Deserialize<DiagnosticResult>(JsonSerializer.Serialize(item))!;
            Check(restored.Outcome == outcome && restored.Severity == FindingSeverity.Unknown, "outcome roundtrip " + outcome);
            if (outcome == CollectionOutcome.Completed) continue;
            try { Result(outcome); throw new Exception("Incomplete collection accepted as healthy"); } catch (ArgumentException) { }
        }
        var metadata = new Dictionary<string, string> { ["source"] = "fixture" };
        var rich = new DiagnosticResult("module", "finding", DiagnosticCategory.Storage, CollectionOutcome.Partial, FindingSeverity.Warning,
            "Capacity", "Review space", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "measured", "one drive inaccessible",
            FindingConfidence.Likely, "review-storage", "future-action", metadata);
        metadata["source"] = "mutated";
        var copy = JsonSerializer.Deserialize<DiagnosticResult>(JsonSerializer.Serialize(rich))!;
        Check(copy.Metadata["source"] == "fixture" && copy.Coverage == rich.Coverage && copy.AutomatedRepairAvailable, "evidence metadata coverage and references roundtrip");
        Check(Result().RepairActionId is null && !Result().AutomatedRepairAvailable, "repair absent by default");
    }
    internal static async Task WindowsModules()
    {
        var now = DateTimeOffset.UtcNow;
        DiagnosticResult Map(string state, double? free = null, double? total = null) => DiagnosticMapping.Map("storage", DiagnosticCategory.Storage, new("capacity", state, "fixture", free, total), now, now);
        Check(Map("info", 4, 100).Severity == FindingSeverity.Critical && Map("info", 5, 100).Severity == FindingSeverity.Warning && Map("info", 10, 100).Severity == FindingSeverity.Healthy, "capacity threshold boundaries");
        Check(Map("info", null, 100).Severity == FindingSeverity.Unknown && Map("info", 110, 100).Severity == FindingSeverity.Unknown, "invalid capacity remains unknown");
        foreach(var state in new[]{"failed", "unavailable", "unknown"}) Check(Map(state).Severity == FindingSeverity.Unknown, "collector state not disk failure: " + state);
        var provider = DiagnosticMapping.Map("security", DiagnosticCategory.Security, new("antivirus", "registered", "third party"), now, now);
        Check(provider.Severity == FindingSeverity.Informational, "third-party registration not unprotected verdict");
        var repairable = DiagnosticMapping.Map("dism", DiagnosticCategory.Windows, new("health", "repairable", "ImageHealthState"), now, now);
        Check(repairable.Outcome == CollectionOutcome.Completed && repairable.Severity == FindingSeverity.Warning, "corruption distinct from command failure");
        var fake = new FixtureProbe();
        foreach(var module in WindowsDiagnosticCatalog.Create(fake)) {
            fake.Json = "[{\"Id\":\"fixture\",\"State\":\"unknown\",\"Evidence\":\"bounded evidence\"}]";
            var results = await DiagnosticExecution.RunAsync(module, new(true,true,false), null, CancellationToken.None);
            Check(results.Single().Outcome == CollectionOutcome.Completed && results.Single().Severity == FindingSeverity.Unknown, "structured Windows adapter " + module.Id);
            Check(!fake.Script.Contains("-RestoreHealth") && !fake.Script.Contains("/scannow"), "diagnostic does not request repair: " + module.Id);
        }
        fake.Json = "broken JSON";
        var failed = await DiagnosticExecution.RunAsync(WindowsDiagnosticCatalog.Create(fake)[0], new(true,true,false), null, CancellationToken.None);
        Check(failed.Single().Outcome == CollectionOutcome.Failed, "malformed collector output fails safely");
    }
    internal static async Task Modules()
    {
        var context = new DiagnosticContext(true, true, false);
        var events = new List<DiagnosticProgress>();
        var good = new FakeModule("good", (p, t) => { p?.Report(new("good", "Reading fixture")); return Task.FromResult<IReadOnlyList<DiagnosticResult>>([Result(module: "good")]); });
        var progress = new InlineProgress<DiagnosticProgress>(events.Add);
        var collected = await DiagnosticExecution.RunAsync(good, context, progress, CancellationToken.None);
        Check(collected.Single().Severity == FindingSeverity.Healthy && events.Single().Activity == "Reading fixture", "independent module success and meaningful progress");
        var bad = new FakeModule("bad", (_, _) => throw new IOException("private path must not reach UI"));
        Check((await DiagnosticExecution.RunAsync(bad, context, null, CancellationToken.None)).Single().Outcome == CollectionOutcome.Failed, "module failure isolated");
        Check((await DiagnosticExecution.RunAsync(good, context with { IsWindows = false }, null, CancellationToken.None)).Single().Outcome == CollectionOutcome.Unavailable, "unsupported platform unavailable");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Check((await DiagnosticExecution.RunAsync(good, context, null, cts.Token)).Single().Outcome == CollectionOutcome.Cancelled, "cancelled module unknown");
        var scan = await new DiagnosticOrchestrator([bad, good]).ScanAsync(context, null, CancellationToken.None);
        Check(scan.Results.Count == 2 && scan.CompletedModules == 2 && !scan.Complete, "failed module does not abort next check");
        Check((await new DiagnosticOrchestrator([]).ScanAsync(context, null, CancellationToken.None)).Complete, "empty scan completes without invented findings");
        Check((await new DiagnosticOrchestrator([good]).ScanAsync(context, null, cts.Token)).Results.Count == 0, "pre-cancelled scan starts no module");
        foreach (var outcome in new[] { CollectionOutcome.Partial, CollectionOutcome.Unavailable, CollectionOutcome.Failed, CollectionOutcome.Cancelled }) {
            var m = new FakeModule("fixture", (_, _) => Task.FromResult<IReadOnlyList<DiagnosticResult>>([Result(outcome, FindingSeverity.Unknown)]));
            Check((await DiagnosticExecution.RunAsync(m, context, null, CancellationToken.None)).Single().Outcome == outcome, "module preserves " + outcome);
        }
        var forward = await new DiagnosticOrchestrator([good, bad]).ScanAsync(context, null, CancellationToken.None);
        Check(scan.Results.Select(r => r.ModuleId).SequenceEqual(forward.Results.Select(r => r.ModuleId)), "stable aggregate order");
        using var mid = new CancellationTokenSource();
        var first = new FakeModule("first", (_, _) => { mid.Cancel(); return Task.FromResult<IReadOnlyList<DiagnosticResult>>([Result(module: "first")]); });
        Check((await new DiagnosticOrchestrator([first, good]).ScanAsync(context, null, mid.Token)).Results.Count == 1, "cancellation skips later modules");
        var external = new FakeModule("external", (_, _) => throw new Exception("must not run")) { Requirements = new(ExternalContact: true) };
        Check((await DiagnosticExecution.RunAsync(external, context, null, CancellationToken.None)).Single().Outcome == CollectionOutcome.Unavailable, "external collection requires explicit consent");
    }
}
internal sealed class FakeModule(string id, Func<IProgress<DiagnosticProgress>?, CancellationToken, Task<IReadOnlyList<DiagnosticResult>>> collect) : IDiagnosticModule
{
    public string Id => id;
    public string DisplayName => id;
    public DiagnosticCategory Category => DiagnosticCategory.Windows;
    public DiagnosticRequirements Requirements { get; init; } = new();
    public Task<IReadOnlyList<DiagnosticResult>> CollectAsync(DiagnosticContext c, IProgress<DiagnosticProgress>? p, CancellationToken t) => collect(p, t);
}

internal sealed class FixtureProbe : IDiagnosticProbe
{
    public string Json = "[]", Script = "";
    public Task<string> ReadAsync(string script, int timeoutSeconds, CancellationToken token) { token.ThrowIfCancellationRequested(); Script = script; return Task.FromResult(Json); }
}
