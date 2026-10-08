using System.Globalization;
namespace IgezziGuard;

/// <summary>
/// Removing an app the way Windows Settings does: its own registered uninstaller, one app at a time. When an app's
/// files are already gone, only its leftover registration is removed, after a backup.
/// </summary>
public static class AppRemoval
{
    /// <summary>A command line split into the program and its arguments, or null when it's empty.</summary>
    public static (string File, string Arguments)? ParseCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = command.Trim();
        if (text[0] == '"') {
            int end = text.IndexOf('"', 1);
            return end <= 1 ? null : (text[1..end], text[(end + 1)..].Trim());
        }
        // Unquoted paths often contain spaces; the program ends at ".exe".
        int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe >= 0) return (text[..(exe + 4)], text[(exe + 4)..].Trim());
        int space = text.IndexOf(' ');
        return space < 0 ? (text, "") : (text[..space], text[(space + 1)..].Trim());
    }

    /// <summary>A Windows Installer product code, such as "{23170F69-40C1-2702-2301-000001000000}".</summary>
    public static bool IsProductCode(string key) => Guid.TryParseExact(key, "B", out _);

    /// <summary>
    /// What to run: msiexec /x for Windows Installer products (their registered command often opens "modify"), the
    /// registered command otherwise. Null, with the reason, when the app can't be uninstalled this way.
    /// </summary>
    public static (string File, string Arguments)? UninstallCommand(InstalledApp app, out string? reason)
    {
        reason = null;
        if (app.NoRemove) { reason = "This app is registered as not removable, so Windows Settings can't remove it either."; return null; }
        if (app.WindowsInstaller && IsProductCode(app.Key)) return ("msiexec.exe", "/x " + app.Key);
        if (ParseCommand(app.Uninstall) is { } command) return command;
        reason = "This app doesn't register an uninstaller. Try Windows Settings → Installed apps, or the app's own setup program.";
        return null;
    }

    /// <summary>
    /// Whether an app's registration outlived its files. Windows Installer products ask Windows (msiState −1: not
    /// installed). Others need both signs: the uninstaller is missing and the install folder is missing or unknown.
    /// Programs given without a folder (msiexec, rundll32) count as present, so the answer errs towards "installed".
    /// </summary>
    public static bool IsLeftover(bool windowsInstaller, int? msiState, string? uninstallFile, bool uninstallFileExists, string? installLocation, bool installLocationExists)
    {
        if (windowsInstaller && msiState is { } state) return state == -1;
        if (!string.IsNullOrWhiteSpace(installLocation) && installLocationExists) return false;
        return !string.IsNullOrWhiteSpace(uninstallFile) && !uninstallFileExists;
    }

    /// <summary>The registration's key for reg.exe, with its registry view, or null when it can't be addressed safely.</summary>
    public static (string Key, string View)? RegistryKey(InstalledApp app)
    {
        string? root = app.Hive switch { "LocalMachine" => "HKLM", "CurrentUser" => "HKCU", _ => null };
        if (root is null || app.Key.Length == 0 || app.Key.IndexOfAny(new[] { '\\', '"', '\0', '/' }) >= 0 || app.Key.Trim() != app.Key) return null;
        return ($@"{root}\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{app.Key}", app.View == "Registry32" ? "/reg:32" : "/reg:64");
    }

    /// <summary>A file name for the .reg backup of a registration.</summary>
    public static string BackupName(InstalledApp app, DateTime now)
    {
        var name = new string(app.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (name.Length > 40) name = name[..40].TrimEnd('-');
        return $"app-entry-{(name.Length == 0 ? "app" : name)}-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.reg";
    }
}
