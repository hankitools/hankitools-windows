using Microsoft.Win32;
using System.Security;

namespace IgezziGuard;

internal static class InstalledApps
{
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
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false);
                if (uninstall is null) continue;
                foreach (var id in uninstall.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var entry = uninstall.OpenSubKey(id, false);
                        if (entry is null || entry.GetValue("SystemComponent") is int flag && flag == 1) continue;
                        var name = entry.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var publisher = entry.GetValue("Publisher") as string ?? "Unknown";
                        var version = entry.GetValue("DisplayVersion") as string ?? "Unknown";
                        // Same entry can be visible in both registry views. Keep user/machine scopes distinct.
                        if (!seen.Add($"{hive}|{id}|{name}|{version}|{publisher}")) continue;
                        apps.Add(new(name, publisher, version, ReviewParsing.InstallDate(entry.GetValue("InstallDate") as string),
                            ReviewParsing.EstimatedBytes(entry.GetValue("EstimatedSize")), $"{hive} / {view}"));
                    }
                    catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { errors++; }
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { errors++; }
        }
        return (apps, errors);
    }
}
