using System.Text.Json;

namespace IgezziGuard;

public sealed record TrackedApp(string Name, string Executable, DateTimeOffset Added,
    DateTimeOffset? LastSeen, double ObservedSeconds);

public static class UsageReview
{
    public static string Describe(TrackedApp? app, DateTimeOffset now)
    {
        if (app is null) return "Unknown — no executable mapped";
        var reference = app.LastSeen ?? app.Added;
        if (now - reference >= TimeSpan.FromDays(30) && app.ObservedSeconds >= 20 * 3600)
            return "Review: not observed for 30+ days (not proof of non-use)";
        return app.LastSeen is { } seen ? "Observed running: " + seen.ToLocalTime().ToString("g") : "Unknown — not yet observed running";
    }
    public static void Save(string path, List<TrackedApp> apps)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(apps)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
