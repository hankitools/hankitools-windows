using System.Globalization;
using System.Text.RegularExpressions;
using IgezziGuard;

internal static class LocalizationChecks
{
    internal static void Run(string root)
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception("Localization: " + message); }
        var baseline = Localizer.ReadCatalog("en");
        Check(baseline.Count > 100, "embedded English catalog must be present");
        var scanFlow = baseline["Scan → review findings → choose what to do. The scan is read-only and no report is uploaded. Some checks or repair actions need administrator access and may be unavailable without it. Hanki never elevates automatically."];
        Check(scanFlow.Contains("Scan → review findings → choose what to do", StringComparison.Ordinal)
            && scanFlow.Contains("read-only", StringComparison.Ordinal)
            && scanFlow.Contains("no report is uploaded", StringComparison.Ordinal)
            && scanFlow.Contains("never elevates automatically", StringComparison.Ordinal), "first-run flow explains scan, review, choice and permissions");
        var recovery = baseline["Recovery stores the previous and new values for supported setting changes. Disabled Startup-folder files are moved into a backup under %LOCALAPPDATA%\\IgezziGuard. Select an entry and choose Review / undo to restore it. Hanki refuses when the current setting no longer matches the saved change. Registry startup entries are undone in Maintain → Startup entries. Restore recycled files through the Windows Recycle Bin; Recovery does not roll back file cleanup. Windows file repairs have no per-file Hanki undo; Windows restore points are not full backups. Uninstall removes Hanki's application files but leaves history and recovery data in %LOCALAPPDATA%\\IgezziGuard."];
        Check(recovery.Contains("Startup-folder files", StringComparison.Ordinal)
            && recovery.Contains("Review / undo", StringComparison.Ordinal)
            && recovery.Contains("Registry startup entries", StringComparison.Ordinal)
            && recovery.Contains("Windows Recycle Bin", StringComparison.Ordinal)
            && recovery.Contains("does not roll back file cleanup", StringComparison.Ordinal)
            && recovery.Contains("no per-file Hanki undo", StringComparison.Ordinal)
            && recovery.Contains("leaves history and recovery data", StringComparison.Ordinal), "recovery copy states backup, restore limits and uninstall retention");
        var onboardingKeys = new[] {
            "Before continuing: supported setting changes save their previous values in Recovery; disabled Startup-folder files are backed up there. Use Recovery → Review / undo; Hanki refuses if the setting no longer matches. Windows file repairs have no per-file Hanki undo, and restore points are not full backups. Uninstall leaves local history and recovery data in %LOCALAPPDATA%\\IgezziGuard.",
            "Getting started",
            "Recovery stores the previous and new values for supported setting changes. Disabled Startup-folder files are moved into a backup under %LOCALAPPDATA%\\IgezziGuard. Select an entry and choose Review / undo to restore it. Hanki refuses when the current setting no longer matches the saved change. Registry startup entries are undone in Maintain → Startup entries. Restore recycled files through the Windows Recycle Bin; Recovery does not roll back file cleanup. Windows file repairs have no per-file Hanki undo; Windows restore points are not full backups. Uninstall removes Hanki's application files but leaves history and recovery data in %LOCALAPPDATA%\\IgezziGuard.",
            "Read each finding, then choose an action—or leave things as they are. Some checks may be unavailable without administrator access.",
            "Scan → review findings → choose what to do. The scan is read-only and no report is uploaded. Some checks or repair actions need administrator access and may be unavailable without it. Hanki never elevates automatically.",
            "What it changes"
        };
        Check(Localizer.Languages.Select(l => l.Code).SequenceEqual(new[] { "en", "fi", "de", "es", "fr", "it", "ja", "ko", "nl", "pl", "pt-BR", "zh-Hans" }), "website language set");
        static string Slots(string value) => string.Join(",", Regex.Matches(value, @"\{\d+(?:[^}]*)\}").Select(m => m.Value).Order());
        foreach (var language in Localizer.Languages) {
            var catalog = Localizer.ReadCatalog(language.Code);
            Check(catalog.Keys.Order().SequenceEqual(baseline.Keys.Order()), language.Code + " catalog keys differ");
            foreach (var (key, value) in catalog) {
                Check(!string.IsNullOrWhiteSpace(value), language.Code + " empty: " + key);
                Check(Slots(key) == Slots(value), language.Code + " placeholders: " + key);
                Check(!value.Contains('\uFFFD'), language.Code + " damaged Unicode");
            }
            if (language.Code != "en") foreach (var key in onboardingKeys)
                Check(catalog[key] != baseline[key], language.Code + " onboarding/recovery translation missing: " + key);
        }
        foreach (var (input, expected) in new[] { ("fi-FI", "fi"), ("de-AT", "de"), ("es-MX", "es"), ("fr-CA", "fr"), ("en-GB", "en"),
            ("pt-BR", "pt-BR"), ("pt-PT", "pt-BR"), ("zh-CN", "zh-Hans"), ("zh-SG", "zh-Hans"), ("zh-Hans-CN", "zh-Hans"),
            ("zh-TW", "en"), ("zh-Hant", "en"), ("sv-SE", "en"), ("", "en") })
            Check(Localizer.Resolve(input) == expected, input + " culture resolution");
        string path = Path.Combine(root, "preferences", "language.json");
        Check(Localizer.ReadPreference(path, "fi-FI") == "fi", "first run follows Windows");
        Localizer.SavePreference(path, "ja");
        Check(Localizer.ReadPreference(path, "fi-FI") == "ja", "explicit preference survives restart");
        Localizer.SavePreference(path, null);
        Check(Localizer.ReadPreference(path, "fi-FI") == "fi", "automatic preference follows Windows again");
        File.WriteAllText(path, "{invalid");
        Check(Localizer.ReadPreference(path, "de-DE") == "de", "malformed preference fallback");
        File.WriteAllText(path, "{\"Language\":\"not-supported\"}");
        Check(Localizer.ReadPreference(path, "ko-KR") == "ko", "unknown preference fallback");
        try { Localizer.SavePreference(path, "../../invalid"); throw new Exception("Invalid locale accepted"); }
        catch (ArgumentException) { }
        Check(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length == 0, "atomic save leaves no temporary files");
        var region = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        var defaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        try {
            Localizer.SetLanguage("fi");
            Check(Localizer.T("Home") == "Etusivu", "translation lookup");
            Check(Localizer.T("unknown source") == "unknown source", "English fallback");
            Check(Localizer.Format("Open {0}", "GPU") == "Avaa GPU", "formatted messages");
            Check(Localizer.Route("Diagnose  /  Crash timeline") == "Vianmääritys  /  Kaatumisaikajana", "localized route display");
            Check(Localizer.SearchKeywords("My PC is slow").Contains("hidas", StringComparison.OrdinalIgnoreCase),
                "localized search aliases remain available to Find a tool");
            Check(Navigation.Find("Diagnose")?.Page == "Diagnose", "stable navigation IDs");
            Check(CultureInfo.CurrentCulture.Equals(region), "regional number formatting must not change");
            foreach (var language in Localizer.Languages) { Localizer.SetLanguage(language.Code); Check(Localizer.T("Language") != "", "all catalogs load"); }
        } finally { Localizer.SetLanguage("en"); CultureInfo.CurrentUICulture = ui; CultureInfo.DefaultThreadCurrentUICulture = defaultUi; }
    }
}
