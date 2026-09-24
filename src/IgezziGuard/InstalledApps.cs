using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Security;

namespace IgezziGuard;

internal static class InstalledApps
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    // msi.h: INSTALLSTATE MsiQueryProductState(LPCWSTR szProduct); -1 = INSTALLSTATE_UNKNOWN (not installed).
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern int MsiQueryProductState(string product);

    public static (List<InstalledApp> Apps, int Errors) Read(CancellationToken token)
    {
        var apps = new List<InstalledApp>(); int errors = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Registry32 })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(UninstallPath, false);
                if (uninstall is null) continue;
                foreach (var id in uninstall.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var entry = uninstall.OpenSubKey(id, false);
                        if (entry is null || entry.GetValue("SystemComponent") is int flag && flag == 1) continue;
                        if (Entry(entry, id, hive, view) is not { } app) continue;
                        // Same entry can be visible in both registry views. Keep user/machine scopes distinct.
                        if (!seen.Add($"{hive}|{id}|{app.Name}|{app.Version}|{app.Publisher}")) continue;
                        apps.Add(app);
                    }
                    catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { errors++; }
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { errors++; }
        }
        return (apps, errors);
    }

    private static InstalledApp? Entry(RegistryKey entry, string id, RegistryHive hive, RegistryView view)
    {
        var name = entry.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(name)) return null;
        var uninstall = entry.GetValue("UninstallString") as string;
        var location = entry.GetValue("InstallLocation") as string;
        bool msi = entry.GetValue("WindowsInstaller") is int w && w == 1;
        return new(name, entry.GetValue("Publisher") as string ?? "Unknown", entry.GetValue("DisplayVersion") as string ?? "Unknown",
            ReviewParsing.InstallDate(entry.GetValue("InstallDate") as string), ReviewParsing.EstimatedBytes(entry.GetValue("EstimatedSize")), $"{hive} / {view}",
            hive.ToString(), view.ToString(), id, uninstall, location, msi, entry.GetValue("NoRemove") is int n && n == 1, Leftover(msi, id, uninstall, location));
    }

    /// <summary>Whether the app's files are gone, checked the way <see cref="AppRemoval.IsLeftover"/> describes.</summary>
    private static bool Leftover(bool msi, string id, string? uninstall, string? location)
    {
        int? state = null;
        if (msi && AppRemoval.IsProductCode(id)) {
            try { state = MsiQueryProductState(id); } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
        string? file = AppRemoval.ParseCommand(uninstall) is { } command ? Environment.ExpandEnvironmentVariables(command.File) : null;
        bool fileExists = file is null || !Path.IsPathRooted(file) || File.Exists(file);
        string? folder = string.IsNullOrWhiteSpace(location) ? null : Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
        return AppRemoval.IsLeftover(msi, state, file, fileExists, folder, folder is not null && Directory.Exists(folder));
    }

    /// <summary>Reads one registration again, or null when it's gone.</summary>
    public static InstalledApp? Reread(InstalledApp app)
    {
        if (!Enum.TryParse<RegistryHive>(app.Hive, out var hive) || !Enum.TryParse<RegistryView>(app.View, out var view)) return null;
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var entry = baseKey.OpenSubKey(UninstallPath + "\\" + app.Key, false);
        return entry is null ? null : Entry(entry, app.Key, hive, view);
    }

    /// <summary>
    /// Starts the app's own uninstaller and waits for it to close. Windows shows its administrator prompt when the
    /// uninstaller asks for one. Throws Win32Exception 1223 when that prompt is declined.
    /// </summary>
    public static async Task RunUninstaller((string File, string Arguments) command, CancellationToken token)
    {
        var file = Environment.ExpandEnvironmentVariables(command.File);
        var start = new ProcessStartInfo(file, command.Arguments) { UseShellExecute = true };
        if (Path.IsPathRooted(file) && Path.GetDirectoryName(file) is { } folder && Directory.Exists(folder)) start.WorkingDirectory = folder;
        using var process = Process.Start(start);
        if (process is not null) await process.WaitForExitAsync(token);
    }

    /// <summary>
    /// Removes a leftover registration after saving it to a .reg file (double-click it to put the entry back).
    /// Entries for all users need administrator rights: then only reg.exe's delete runs elevated, after Windows asks.
    /// Returns the backup's path.
    /// </summary>
    public static string RemoveLeftover(InstalledApp app)
    {
        var current = Reread(app) ?? throw new IOException("The entry is already gone.");
        if (!current.Leftover) throw new IOException("This app's files are present again, so its entry was kept. Uninstall it instead.");
        var key = AppRemoval.RegistryKey(current) ?? throw new IOException("This entry can't be addressed safely, so it was left alone.");
        var folder = Path.Combine(SecurityPaths.Root, "app-entry-backups");
        Directory.CreateDirectory(folder);
        var backup = Path.Combine(folder, AppRemoval.BackupName(current, DateTime.Now));
        var export = new ProcessStartInfo("reg.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "export", key.Key, backup, "/y", key.View }) export.ArgumentList.Add(arg);
        using (var p = Process.Start(export) ?? throw new IOException("reg.exe couldn't be started.")) {
            if (!p.WaitForExit(30_000) || p.ExitCode != 0 || !File.Exists(backup)) throw new IOException("The entry couldn't be backed up, so it was left alone.");
        }
        try {
            using var baseKey = RegistryKey.OpenBaseKey(Enum.Parse<RegistryHive>(current.Hive), Enum.Parse<RegistryView>(current.View));
            using var uninstall = baseKey.OpenSubKey(UninstallPath, writable: true) ?? throw new UnauthorizedAccessException();
            uninstall.DeleteSubKeyTree(current.Key, throwOnMissingSubKey: false);
        } catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException) {
            // Needs administrator rights: Windows asks before reg.exe deletes this one key.
            var delete = new ProcessStartInfo("reg.exe", $"delete \"{key.Key}\" /f {key.View}") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using var p = Process.Start(delete) ?? throw new IOException("reg.exe couldn't be started.");
            if (!p.WaitForExit(60_000)) throw new IOException("Removing the entry didn't finish in time.");
        }
        if (Reread(current) is not null) throw new IOException("The entry is still there. The backup was kept: " + backup);
        return backup;
    }
}
