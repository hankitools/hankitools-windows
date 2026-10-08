using System.Text.Json;
namespace IgezziGuard;

/// <param name="Module">The part of the program that failed (Application Error 1000); null or empty for a program that stopped responding.</param>
internal sealed record AppCrashEvent(string App, string? Module, bool Hang, DateTimeOffset Time);
internal sealed record AppCrashFacts(int Days, AppCrashEvent[]? Events, string? Notes);

/// <summary>
/// Which apps keep crashing (HANKI-FIX-120): the same Application Error and Application Hang records Reliability
/// Monitor shows, counted per app. Read-only, standard user.
/// </summary>
internal static class AppCrashes
{
    internal const int Days = 14, Repeated = 3;
    internal const string Script = """
        $notes=@();$items=@()
        try{$e=@(Get-WinEvent -FilterHashtable @{LogName='Application';ProviderName='Application Error','Application Hang';Id=1000,1002;StartTime=(Get-Date).AddDays(-14)} -MaxEvents 500 -ErrorAction Stop)
            $items=@($e|ForEach-Object{[pscustomobject]@{App=[string]$_.Properties[0].Value;Module=[string]$(if($_.Id -eq 1000){$_.Properties[3].Value});Hang=($_.Id -eq 1002);Time=$_.TimeCreated.ToUniversalTime().ToString('o')}})
        }catch{if($_.FullyQualifiedErrorId -notmatch 'NoMatchingEventsFound'){$notes+='crash history'}}
        [pscustomobject]@{Days=14;Events=$items;Notes=($notes -join ', ')}|ConvertTo-Json -Depth 4 -Compress
        """;
    internal static AppCrashFacts Parse(string json) =>
        JsonSerializer.Deserialize<AppCrashFacts>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("App crash history unavailable.");

    /// <summary>Graphics driver parts that games and apps crash in: user-mode drivers of NVIDIA, AMD and Intel.</summary>
    internal static bool GraphicsDriver(string? module) => module is not null && System.Text.RegularExpressions.Regex.IsMatch(module,
        @"^(nvwgf2um|nvd3dum|nvoglv|nvlddmkm|nvgpucomp|nvcuda|atiumd|atidxx|atio6axx|amdxx|amdxc|amdvlk|igd10|igd12|igdumd|igxel|igc|igvk|ig\w+icd)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    internal static IReadOnlyList<HealthItem> Evaluate(AppCrashFacts facts, DateTimeOffset now)
    {
        if ((facts.Notes ?? "").Contains("crash history", StringComparison.Ordinal))
            return [new("apps", "App crashes", CardStatus.Unknown, "Windows' record of app crashes couldn't be read.", "Application log unavailable")];
        var events = (facts.Events ?? []).Where(e => !string.IsNullOrWhiteSpace(e.App)).ToArray();
        if (events.Length == 0)
            return [new("apps", "App crashes", CardStatus.Good, $"No app crashed or stopped responding in the last {facts.Days} days.", "Application Error 1000 / Application Hang 1002: none")];
        var items = new List<HealthItem>();
        var apps = events.GroupBy(e => e.App, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ToArray();
        foreach (var app in apps.Where(g => g.Count() >= Repeated).Take(5)) {
            int crashes = app.Count(e => !e.Hang), hangs = app.Count(e => e.Hang);
            var latest = app.Max(e => e.Time);
            string name = app.Key, what = crashes > 0 && hangs > 0 ? $"crashed {Times(crashes)} and stopped responding {Times(hangs)}"
                : crashes > 0 ? $"crashed {Times(crashes)}" : $"stopped responding {Times(hangs)}";
            var module = app.Where(e => !e.Hang && !string.IsNullOrEmpty(e.Module)).GroupBy(e => e.Module!, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
            string advice = crashes == 0
                ? "An app that hangs is often waiting on a slow disk, the network or a stuck add-on. Update it, and check the disk and memory results in this scan."
                : GraphicsDriver(module) ? $"Most crashes happened in the graphics driver ({module}). Update the graphics driver from NVIDIA, AMD or Intel, or reinstall it with a clean install."
                : module is not null && module.Equals(name, StringComparison.OrdinalIgnoreCase) ? "The crashes happen in the app itself. Update it, or repair or reinstall it."
                : "Update the app; if it keeps crashing, repair or reinstall it. Its own support pages or logs can say more.";
            items.Add(new("app:" + name.ToLowerInvariant(), crashes > 0 ? $"{name} keeps crashing" : $"{name} keeps stopping responding", CardStatus.Review,
                $"{name} {what} in the last {facts.Days} days, most recently {latest.ToLocalTime():g}. {advice}",
                $"{crashes} Application Error, {hangs} Application Hang; most common module: {module ?? "not recorded"}"));
        }
        var occasional = apps.Where(g => g.Count() < Repeated).ToArray();
        if (occasional.Length > 0)
            items.Add(new("apps-occasional", items.Count == 0 ? "Occasional app crashes" : "Other app crashes", CardStatus.Info,
                $"In the last {facts.Days} days: " + string.Join(", ", occasional.Take(6).Select(g => $"{g.Key} {Times(g.Count())}")) + (occasional.Length > 6 ? $" and {occasional.Length - 6} more" : "") +
                ". A crash now and then is usually nothing to worry about.",
                string.Join("; ", occasional.Select(g => $"{g.Key}: {g.Count()}"))));
        return items;
    }
    private static string Times(int count) => count switch { 1 => "once", 2 => "twice", _ => $"{count} times" };
}

/// <summary>Full scan module: repeated app crashes and hangs from Windows' own records.</summary>
internal sealed class AppCrashDiagnostic(IDiagnosticProbe? source = null) : IDiagnosticModule
{
    private readonly IDiagnosticProbe source = source ?? new WindowsDiagnosticProbe();
    public string Id => "app-crashes";
    public string DisplayName => "App crashes";
    public DiagnosticCategory Category => DiagnosticCategory.Windows;
    public DiagnosticRequirements Requirements => new();
    public async Task<IReadOnlyList<DiagnosticResult>> CollectAsync(DiagnosticContext context, IProgress<DiagnosticProgress>? progress, CancellationToken token)
    {
        var start = DateTimeOffset.UtcNow; progress?.Report(new(Id, "Reading app crash history"));
        var facts = AppCrashes.Parse(await source.ReadAsync(AppCrashes.Script, 60, token));
        return HealthItems.Findings(Id, Category, AppCrashes.Evaluate(facts, DateTimeOffset.UtcNow), start, DateTimeOffset.UtcNow);
    }
}
