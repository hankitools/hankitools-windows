using System.ComponentModel;
using System.Diagnostics;

namespace IgezziGuard;

internal static class DesktopShortcuts
{
    public static void Open(IWin32Window owner, string shortcut, string? selectedFile = null)
    {
        try
        {
            // Absolute Windows paths: never search PATH or execute user-supplied shell commands.
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var start = new ProcessStartInfo { UseShellExecute = true, WorkingDirectory = windows };
            switch (shortcut)
            {
                case "task-manager":
                    start.FileName = Path.Combine(system, "Taskmgr.exe");
                    break;
                case "settings":
                    start.FileName = "ms-settings:";
                    break;
                case "event-viewer":
                    start.FileName = Path.Combine(system, "mmc.exe");
                    start.ArgumentList.Add(Path.Combine(system, "eventvwr.msc"));
                    break;
                case "startup":
                    start.FileName = "ms-settings:startupapps";
                    break;
                case "pagefile":
                    start.FileName = Path.Combine(system, "SystemPropertiesPerformance.exe");
                    break;
                case "defrag":
                    start.FileName = Path.Combine(system, "dfrgui.exe"); // Optimize Drives
                    break;
                case "chatgpt":
                    start.FileName = "https://chatgpt.com/"; // No report, prompt or identifiers in URL.
                    break;
                case "installed-apps":
                    start.FileName = "ms-settings:appsfeatures";
                    break;
                case "powershell":
                    start.FileName = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
                    start.Arguments = "-NoLogo -NoProfile";
                    start.Verb = "runas";
                    break;
                case "cmd":
                    start.FileName = Path.Combine(system, "cmd.exe");
                    start.Arguments = "/d";
                    start.Verb = "runas";
                    break;
                case "explorer":
                    start.FileName = Path.Combine(windows, "explorer.exe");
                    if (selectedFile is not null)
                    {
                        var path = Path.GetFullPath(selectedFile);
                        if (path.Contains('"') || !File.Exists(path)) throw new IOException("The selected file no longer exists.");
                        start.Arguments = "/select,\"" + path + "\"";
                    }
                    break;
                default: throw new ArgumentException("Unknown shortcut.");
            }
            using var process = Process.Start(start);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User declined UAC. Respect that choice without retrying.
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(owner, ex.Message, "Could not open shortcut", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
