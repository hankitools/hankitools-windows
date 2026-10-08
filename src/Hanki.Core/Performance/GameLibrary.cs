using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace IgezziGuard;

/// <param name="Hidden">Removed by the user; a rescan doesn't bring it back.</param>
public sealed record GameEntry(Guid Id, string Name, string Executable, string Source, GamingGoal Goal, bool Hidden = false, int TacticalVision = 0);
public sealed record GameLibraryDocument(int Version, IReadOnlyList<GameEntry> Games);

/// <summary>
/// Installed games (HANKI-GAME-205): found locally from Steam, Epic, GOG and known publishers' uninstall records, or
/// added by picking an .exe. Nothing is looked up online. Settings are applied per game wherever possible.
/// </summary>
public static partial class GameLibrary
{
    public const int Limit = 500;

    // ---- Pure helpers (tested) ------------------------------------------------------------------------------
    /// <summary>Library folder paths from Steam's libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> SteamLibraries(string vdf) =>
        PathPattern().Matches(vdf).Select(m => m.Groups[1].Value.Replace(@"\\", @"\")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    /// <summary>A value such as "name" or "installdir" from a Steam appmanifest .acf file.</summary>
    public static string? AcfValue(string acf, string key) =>
        Regex.Match(acf, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value.Replace(@"\\", @"\") : null;
    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)] private static partial Regex PathPattern();

    /// <summary>Executables that ship with games but aren't the game: installers, launchers, crash reporters, anti-cheat.</summary>
    [GeneratedRegex(@"(unins|setup|install|crash|report|redist|vc_?redist|dxsetup|directx|launcher|helper|update|patch|anticheat|easyanticheat|battleye|beservice|be_service|prereq|cleanup|diagnostic|uninstall|touchup|cefprocess|webhelper|errorhandler|bugsplat|dotnet|vcredist|oalinst|physx|ue4prereq|epicwebhelper)", RegexOptions.IgnoreCase)]
    private static partial Regex NotAGame();
    public static bool LooksLikeGame(string executable) => !NotAGame().IsMatch(Path.GetFileNameWithoutExtension(executable));

    /// <summary>
    /// The game's main executable among candidates: one the graphics driver has a profile for wins (NVIDIA knows
    /// game executables), otherwise the largest, which is almost always the game rather than a helper.
    /// </summary>
    public static string? MainExecutable(IEnumerable<(string Path, long Size)> candidates, Func<string, bool>? knownToDriver = null)
    {
        var games = candidates.Where(c => LooksLikeGame(c.Path)).OrderByDescending(c => c.Size).ToArray();
        if (games.Length == 0) return null;
        if (knownToDriver is not null) foreach (var c in games.Take(12)) if (knownToDriver(Path.GetFileName(c.Path))) return c.Path;
        return games[0].Path;
    }

    /// <summary>Adds newly found games, keeps the user's choices, and never re-adds a game the user removed.</summary>
    public static IReadOnlyList<GameEntry> Merge(IReadOnlyList<GameEntry> existing, IEnumerable<GameEntry> found)
    {
        var result = existing.ToList();
        foreach (var game in found)
            if (!result.Any(g => g.Executable.Equals(game.Executable, StringComparison.OrdinalIgnoreCase))) result.Add(game);
        return result.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Take(Limit).ToArray();
    }

    // ---- Storage -------------------------------------------------------------------------------------------
    internal static string StorePath => Path.Combine(SecurityPaths.Root, "games.json");
    public static IReadOnlyList<GameEntry> Read(string path)
    {
        var document = LocalJson.Read<GameLibraryDocument>(path);
        if (document is null) return [];
        if (document.Version != 1 || document.Games is null || document.Games.Count > Limit || document.Games.Any(g => g?.Executable is null || g.Name is null))
            throw new IOException("The game list is damaged or from a newer version. Original file preserved.");
        return document.Games;
    }
    public static void Write(string path, IReadOnlyList<GameEntry> games)
    {
        using var gate = LocalJson.Lock(path);
        LocalJson.Write(path, new GameLibraryDocument(1, games.Take(Limit).ToArray()));
    }

    // ---- Detection (reads local launcher files and the registry) ------------------------------------------------
    internal static IReadOnlyList<GameEntry> Detect(Func<string, bool>? knownToDriver, CancellationToken token)
    {
        var found = new List<GameEntry>();
        void Add(string name, string? folder, string? executable, string source) {
            token.ThrowIfCancellationRequested();
            string? exe = executable is not null && File.Exists(executable) ? executable : folder is not null && Directory.Exists(folder) ? MainExecutable(Candidates(folder), knownToDriver) : null;
            if (exe is not null && !string.IsNullOrWhiteSpace(name)) found.Add(new GameEntry(Guid.NewGuid(), name.Trim(), exe, source, GamingGoal.Balanced));
        }
        foreach (var (name, folder) in Steam()) Add(name, folder, null, "Steam");
        foreach (var (name, folder, exe) in Epic()) Add(name, folder, exe, "Epic Games");
        foreach (var (name, exe) in Gog()) Add(name, null, exe, "GOG");
        foreach (var (name, folder, publisher) in Publishers()) Add(name, folder, null, publisher);
        return found.GroupBy(g => g.Executable, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
    }

    /// <summary>Executables up to two folders deep, bounded so a huge folder can't stall the scan.</summary>
    private static IEnumerable<(string Path, long Size)> Candidates(string folder)
    {
        var result = new List<(string, long)>();
        void Scan(string directory, int depth) {
            if (result.Count > 400) return;
            try {
                foreach (var file in Directory.EnumerateFiles(directory, "*.exe")) { try { result.Add((file, new FileInfo(file).Length)); } catch (IOException) { } }
                if (depth < 2) foreach (var child in Directory.EnumerateDirectories(directory).Take(40)) {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                    Scan(child, depth + 1);
                }
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Scan(folder, 0);
        return result;
    }

    private static IEnumerable<(string Name, string Folder)> Steam()
    {
        string? steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
        if (steam is null) yield break;
        var libraries = new List<string> { steam.Replace('/', '\\') };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf)) { try { libraries.AddRange(SteamLibraries(File.ReadAllText(vdf))); } catch (IOException) { } }
        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase)) {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            IEnumerable<string> manifests;
            try { manifests = Directory.EnumerateFiles(apps, "appmanifest_*.acf").ToArray(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var manifest in manifests) {
                string acf; try { acf = File.ReadAllText(manifest); } catch (IOException) { continue; }
                if (AcfValue(acf, "appid") is "228980") continue; // Steamworks Common Redistributables
                if (AcfValue(acf, "name") is { } name && AcfValue(acf, "installdir") is { } dir) yield return (name, Path.Combine(apps, "common", dir));
            }
        }
    }
    private static IEnumerable<(string Name, string Folder, string? Exe)> Epic()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(folder)) yield break;
        foreach (var file in Directory.EnumerateFiles(folder, "*.item")) {
            JsonElement root;
            try { using var doc = JsonDocument.Parse(File.ReadAllText(file)); root = doc.RootElement.Clone(); } catch (Exception ex) when (ex is IOException or JsonException) { continue; }
            string? S(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (S("DisplayName") is { } name && S("InstallLocation") is { } location)
                yield return (name, location, S("LaunchExecutable") is { Length: > 0 } exe ? Path.Combine(location, exe) : null);
        }
    }
    private static IEnumerable<(string Name, string Exe)> Gog()
    {
        using var games = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        foreach (var id in games?.GetSubKeyNames() ?? []) {
            using var key = games!.OpenSubKey(id);
            if (key?.GetValue("gameName") is string name && key.GetValue("exe") is string exe) yield return (name, exe);
        }
    }
    /// <summary>Games from publishers whose launchers register an uninstall entry (Battle.net, Riot, EA, Ubisoft, Rockstar).</summary>
    private static IEnumerable<(string Name, string Folder, string Publisher)> Publishers()
    {
        string[] publishers = ["Blizzard Entertainment", "Riot Games", "Electronic Arts", "Ubisoft", "Rockstar Games"];
        foreach (var (hive, path) in new[] { (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"), (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"), (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall") }) {
            using var root = hive.OpenSubKey(path);
            foreach (var id in root?.GetSubKeyNames() ?? []) {
                using var key = root!.OpenSubKey(id);
                if (key?.GetValue("Publisher") is not string publisher || key.GetValue("DisplayName") is not string name || key.GetValue("InstallLocation") is not string folder) continue;
                var match = publishers.FirstOrDefault(p => publisher.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                // Skip the launchers themselves.
                if (match is null || name.Contains("Battle.net", StringComparison.OrdinalIgnoreCase) || name.Contains("Riot Client", StringComparison.OrdinalIgnoreCase) || name.Contains(" app", StringComparison.OrdinalIgnoreCase) || name.Contains("Connect", StringComparison.OrdinalIgnoreCase) || name.Contains("Launcher", StringComparison.OrdinalIgnoreCase)) continue;
                yield return (name, folder.Trim('"'), match);
            }
        }
    }
}
