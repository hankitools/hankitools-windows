namespace IgezziGuard;

/// <param name="Profile">The OBS profile's folder name.</param>
/// <param name="Use">"streaming" or "recording".</param>
/// <param name="Encoder">The encoder id OBS stores, such as "x264", "obs_x264" or "jim_nvenc".</param>
public sealed record ObsEncoder(string Profile, string Use, string Encoder);

/// <summary>
/// OBS Studio's video encoder per profile, read from its settings file (read-only). x264 encodes on the processor;
/// NVENC, AMF and Quick Sync use the graphics hardware's video engine and barely touch the processor or the game.
/// </summary>
public static class ObsSettings
{
    public static string ProfilesFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "basic", "profiles");

    /// <summary>Every profile's encoders; empty when OBS isn't installed for this user.</summary>
    public static IReadOnlyList<ObsEncoder> Read(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var found = new List<ObsEncoder>();
        foreach (var profile in Directory.EnumerateDirectories(folder).Take(50)) {
            var file = new FileInfo(Path.Combine(profile, "basic.ini"));
            if (!file.Exists || file.Length > 256 * 1024) continue;
            try { found.AddRange(Parse(Path.GetFileName(profile), File.ReadAllText(file.FullName))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return found;
    }

    /// <summary>
    /// The encoders one basic.ini uses. Simple mode keeps them under [SimpleOutput] (recording uses the streaming
    /// encoder when RecQuality is "Stream"); Advanced mode under [AdvOut] (RecEncoder "none" means the same).
    /// A missing key means OBS's own default, which Hanki doesn't guess.
    /// </summary>
    public static IReadOnlyList<ObsEncoder> Parse(string profile, string ini)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string section = "";
        foreach (var raw in ini.Split('\n')) {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) section = line[1..^1];
            else if (line.IndexOf('=') is > 0 and var at) values.TryAdd(section + "/" + line[..at].Trim(), line[(at + 1)..].Trim());
        }
        string? Get(string key) => values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;
        bool advanced = string.Equals(Get("Output/Mode"), "Advanced", StringComparison.OrdinalIgnoreCase);
        string? stream = advanced ? Get("AdvOut/Encoder") : Get("SimpleOutput/StreamEncoder");
        string? record = advanced ? Get("AdvOut/RecEncoder") : Get("SimpleOutput/RecQuality") is "Stream" ? null : Get("SimpleOutput/RecEncoder");
        var result = new List<ObsEncoder>();
        if (stream is not null) result.Add(new(profile, "streaming", stream));
        if (record is not null && !record.Equals("none", StringComparison.OrdinalIgnoreCase)) result.Add(new(profile, "recording", record));
        return result;
    }

    /// <summary>True for encoders that run on the processor, false for hardware encoders, null for ones Hanki doesn't know.</summary>
    public static bool? Software(string encoder)
    {
        var e = encoder.ToLowerInvariant();
        if (e.Contains("x264") || e.Contains("svt_av1") || e.Contains("aom_av1")) return true;
        if (e.Contains("nvenc") || e.Contains("amf") || e.StartsWith("amd", StringComparison.Ordinal) || e.Contains("qsv")) return false;
        return null;
    }
}
