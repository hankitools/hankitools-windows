using System.Globalization;
using System.Text.Json;

namespace IgezziGuard;

/// <summary>Offline UI translations. English source text is the fallback; route IDs and diagnostic evidence are never translated here.</summary>
internal static class Localizer
{
    internal sealed record Language(string Code, string NativeName)
    {
        public override string ToString() => NativeName;
    }

    internal static readonly IReadOnlyList<Language> Languages = Array.AsReadOnly(new[] {
        new Language("en", "English"), new Language("fi", "Suomi"),
        new Language("de", "Deutsch"), new Language("es", "Español"),
        new Language("fr", "Français"), new Language("it", "Italiano"),
        new Language("ja", "日本語"), new Language("ko", "한국어"),
        new Language("nl", "Nederlands"), new Language("pl", "Polski"),
        new Language("pt-BR", "Português (Brasil)"), new Language("zh-Hans", "简体中文")
    });
    internal static string CurrentLanguage { get; private set; } = "en";
    private static IReadOnlyDictionary<string, string> messages = new Dictionary<string, string>();
    internal static string PreferencePath => Path.Combine(SecurityPaths.Root, "language.json");

    internal static string Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "en";
        var exact = Languages.FirstOrDefault(l => l.Code.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Code;
        // Do not silently present Simplified Chinese to a Traditional Chinese Windows user.
        if (name.Equals("zh", StringComparison.OrdinalIgnoreCase) || name.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase)
            || name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) || name.Equals("zh-SG", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "en";
        if (name.Equals("pt", StringComparison.OrdinalIgnoreCase) || name.StartsWith("pt-", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
        var neutral = name.Split('-')[0];
        return Languages.FirstOrDefault(l => l.Code.Equals(neutral, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";
    }

    internal static string ReadPreference(string path, string systemLanguage)
        => ReadSavedLanguage(path) ?? Resolve(systemLanguage);

    internal static string? ReadSavedLanguage(string path)
    {
        try {
            var saved = JsonSerializer.Deserialize<Preference>(File.ReadAllText(path));
            if (saved?.Language is { } code && Languages.Any(l => l.Code == code)) return code;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return null;
    }

    internal static void Initialize() => SetLanguage(ReadPreference(PreferencePath, CultureInfo.CurrentUICulture.Name));

    internal static void SetLanguage(string language)
    {
        CurrentLanguage = Resolve(language);
        messages = ReadCatalog(CurrentLanguage);
        // Leave CurrentCulture alone: Windows regional formatting and command parsing must not change.
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(CurrentLanguage);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
    }

    internal static IReadOnlyDictionary<string, string> ReadCatalog(string language)
    {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream($"Hanki.Localization.{language}.json");
        if (stream is null) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
    }

    internal static void SavePreference(string path, string? language)
    {
        if (language is not null && !Languages.Any(l => l.Code == language)) throw new ArgumentException("Unsupported UI language.", nameof(language));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preference(language)));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static string T(string source) => messages.TryGetValue(source, out var translated) && !string.IsNullOrWhiteSpace(translated) ? translated : source;
    internal static string Format(string source, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, T(source), arguments);
    internal static string Route(string route) => string.Join("  /  ", route.Split("  /  ").Select(T));
    internal static string SearchKeywords(string english)
        => english + " " + string.Join(" ", HomeSearch.Suggestions.Where(s => english.Contains(s, StringComparison.OrdinalIgnoreCase)).Select(T));
    private sealed record Preference(string? Language);
}
