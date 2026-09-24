using Microsoft.Win32;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IgezziGuard;

internal sealed class WindowsSettings : ISettingBackend
{
    internal static ChangeJournal Journal() => new(new WindowsSettings(), Path.Combine(SecurityPaths.Root, "recovery.json"));
    internal static string PowerExe => Path.Combine(Environment.SystemDirectory, "powercfg.exe");
    public async Task<string> Read(string kind, string target, CancellationToken token)
    {
        // Hanki Performance changes share this journal, so Recovery can undo them too.
        if (PerformanceSettings.Handles(kind)) return await Task.Run(() => Safely(() => PerformanceSettings.Read(kind, target)), token);
        switch (kind) {
            case "Power plan":
                if (target != "Active") throw new IOException("Unknown power target.");
                var text = await WindowsCommand.Run(PowerExe, ["/getactivescheme"], token);
                var match = Regex.Match(text, @"\b[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\b");
                return match.Success ? Guid.Parse(match.Value).ToString() : throw new IOException("Active power plan unavailable.");
            case "IPv4 DNS":
                Adapter(target);
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + Guid.Parse(target).ToString("B"))) {
                    if (key is null) throw new IOException("Adapter DNS configuration unavailable.");
                    var raw = key.GetValue("NameServer");
                    if (raw is not null && raw is not string) throw new IOException("Unrecognized DNS configuration type; no changes permitted.");
                    var value = raw as string ?? "";
                    return NormalizeDns(value);
                }
            case "Startup file": return StartupState(target);
            default: throw new IOException("Unsupported recovery action.");
        }
    }
    public async Task Write(string kind, string target, string value, CancellationToken token)
    {
        if (PerformanceSettings.Handles(kind)) { await Task.Run(() => Safely(() => { PerformanceSettings.Write(kind, target, value); return ""; }), token); return; }
        switch (kind) {
            case "Power plan":
                if (target != "Active" || !Guid.TryParse(value, out var plan)) throw new IOException("Invalid power target.");
                await WindowsCommand.Run(PowerExe, ["/setactive", plan.ToString()], token); break;
            case "IPv4 DNS":
                var adapter = Adapter(target); var index = adapter.GetIPProperties().GetIPv4Properties()?.Index ?? throw new IOException("No IPv4 interface.");
                value = NormalizeDns(value);
                var action = value.Length == 0 ? "-ResetServerAddresses" : "-ServerAddresses @(" + string.Join(",", value.Split(',').Select(WindowsCommand.Quote)) + ")";
                await WindowsCommand.PowerShell($"$dns=@(Get-DnsClientServerAddress -InterfaceIndex {index} -AddressFamily IPv4); if($dns.Count -ne 1){{throw 'Expected exactly one IPv4 DNS instance'}}; Set-DnsClientServerAddress -InputObject $dns[0] {action}", token);
                break;
            case "Startup file":
                ValidateStartupPath(target); var current = StartupState(target);
                if (value.StartsWith("Enabled:", StringComparison.Ordinal) && current == "Disabled:" + value[8..]) File.Move(DisabledPath(target), target);
                else if (value.StartsWith("Disabled:", StringComparison.Ordinal) && current == "Enabled:" + value[9..]) { Directory.CreateDirectory(Path.GetDirectoryName(DisabledPath(target))!); File.Move(target, DisabledPath(target)); }
                else throw new IOException("Startup file changed or destination occupied. Move refused.");
                break;
            default: throw new IOException("Unsupported setting.");
        }
    }
    /// <summary>Driver and registry failures surface as IOException, which the journal and pages already handle.</summary>
    private static string Safely(Func<string> action)
    {
        try { return action(); }
        catch (NvidiaException ex) { throw new IOException(ex.Message, ex); }
        catch (AmdException ex) { throw new IOException(ex.Message, ex); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { throw new IOException("Windows didn't allow the change: " + ex.Message, ex); }
    }
    internal static NetworkInterface Adapter(string id) => NetworkInterface.GetAllNetworkInterfaces().SingleOrDefault(a => Guid.TryParse(a.Id, out var guid) && guid == Guid.Parse(id)) ?? throw new IOException("Adapter no longer present.");
    internal static string NormalizeDns(string text) {
        var addresses = text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(s => IPAddress.Parse(s)).ToArray();
        if (addresses.Length > 8 || addresses.Any(a => a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)) throw new IOException("Only IPv4 DNS addresses supported here.");
        return string.Join(",", addresses.Select(a => a.ToString()));
    }
    internal static string DisabledPath(string path) => Path.Combine(SecurityPaths.Root, "disabled-startup", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))) + ".disabled";
    internal static void ValidateStartupPath(string path) {
        var full = Path.GetFullPath(path); var parent = Path.GetDirectoryName(full);
        if (!new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) }.Any(p => !string.IsNullOrEmpty(p) && string.Equals(p, parent, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Only top-level files in the Windows startup folders are supported.");
        CleanupPolicy.RejectReparseAncestors(parent!);
        if (File.Exists(full)) CleanupPolicy.RejectReparseAncestors(full);
    }
    private static string StartupState(string path) {
        ValidateStartupPath(path); var disabled = DisabledPath(path);
        if (File.Exists(path) && File.Exists(disabled)) throw new IOException("Both startup and backup files exist. Inspect manually; nothing overwritten.");
        var file = File.Exists(path) ? path : File.Exists(disabled) ? disabled : throw new IOException("Startup file and backup are both missing.");
        CleanupPolicy.RejectReparseAncestors(file);
        if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new IOException("Startup file exceeds 4 MiB limit.");
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (file == path ? "Enabled:" : "Disabled:") + Convert.ToHexString(SHA256.HashData(stream));
    }
}

public sealed class RecoveryPanel : ToolPage
{
    private readonly ComboBox entries = new() { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
    private List<SettingChange> records = [];
    public RecoveryPanel() : base("Recovery covers power plans, IPv4 DNS and startup-folder files. Current state must match a saved state before undo. Registry startup actions remain under Startup / undo. Recycled files are restored through Windows Recycle Bin; no claim of automatic file rollback.") {
        Bar.Controls.Add(entries);
        Button("Refresh history", RefreshHistory);
        Button("Review / undo", async () => {
            if (entries.SelectedIndex < 0) return; var e = records[entries.SelectedIndex];
            if (!Review($"Restore {e.Kind}: {e.Target}?\nCurrent expected: {e.After}\nRestore: {e.Before}\n\nExternal changes will block undo. Restoring DNS can briefly affect name resolution; a restored startup file may run on next sign-in.")) return;
            await Run(async token => { await WindowsSettings.Journal().Undo(e.Id, token); return "Restore verified. Refresh history for the updated state."; });
        });
    }
    private void RefreshHistory() { try { records = WindowsSettings.Journal().Read().OrderByDescending(e => e.At).ToList(); entries.Items.Clear(); foreach (var e in records) entries.Items.Add($"{e.At.ToLocalTime():g} / {e.Kind} / {e.Status}"); Output.Text = string.Join("\r\n\r\n", records.Select(e => $"{e.At:O} {e.Kind} {e.Target}\r\n{e.Before} → {e.After}\r\n{e.Status}")); } catch (Exception ex) { Output.Text = ex.Message; } }
}

public sealed class TuningPanel : ToolPage
{
    public TuningPanel() : base("Reversible tuning: choose an installed Windows power plan, then measure the same workload before and after. Higher performance can increase power use, heat and fan noise; it does not guarantee faster games. No service, registry optimizer or security changes.") {
        Button("Inspect available plans", async () => await Run(t => WindowsCommand.Run(WindowsSettings.PowerExe, ["/list"], t)));
        Button("Balanced", () => Apply("381b4222-f694-41f0-9685-ff5bb260df2e"));
        Button("High performance", () => Apply("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));
    }
    private async void Apply(string plan) {
        try {
            var before = await new WindowsSettings().Read("Power plan", "Active", CancellationToken.None);
            if (!Review($"Change active power plan?\nBefore: {before}\nAfter: {plan}\n\nOnly installed plans can be activated. Original plan will be recorded in Recovery. High performance can increase heat and battery consumption.")) return;
            await Run(async t => { await WindowsSettings.Journal().Apply("Power plan", "Active", before, plan, t); return "Power plan changed and verified. Undo is available in Recovery. Repeat the same workload before comparing measurements."; });
        } catch (Exception ex) { Output.Text = ex.Message; }
    }
}

public sealed class StartupFoldersPanel : ToolPage
{
    internal static bool IsFolderMetadata(string path) =>
        Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
        (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) == (FileAttributes.Hidden | FileAttributes.System);
    private readonly ComboBox files = new() { Width = 600, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly List<string> paths = [];
    public StartupFoldersPanel() : base("Manage files in current-user and all-users Startup folders. Disabling moves the file into Hanki's local backup folder; Recovery restores it without overwriting a newer file. All-users changes may need administrator rights. No shortcut is executed. Scheduled tasks and services are outside this tool.") {
        Bar.Controls.Add(files); Button("Read startup folders", () => { try {
            paths.Clear(); files.Items.Clear();
            foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) }) if (Directory.Exists(folder)) {
                // desktop.ini and other hidden system files hold folder settings; they are not startup programs.
                CleanupPolicy.RejectReparseAncestors(folder);
                foreach (var file in Directory.EnumerateFiles(folder).Where(f => !IsFolderMetadata(f))) { paths.Add(file); files.Items.Add(file); }
            }
            Output.Text = $"{paths.Count} startup files found. Select and review one; nothing selected automatically.";
        } catch (Exception ex) { Output.Text = ex.Message; } });
        Button("Review / disable file", async () => { if (files.SelectedIndex < 0) return; var path = paths[files.SelectedIndex]; try {
            var before = await new WindowsSettings().Read("Startup file", path, CancellationToken.None);
            if (!Review($"Move this startup file out of the Startup folder?\n{path}\n\nIt will stop launching through this folder at future sign-ins. Restore from Recovery. Keep Hanki's local backup folder until restored.")) return;
            await Run(async t => { await WindowsSettings.Journal().Apply("Startup file", path, before, "Disabled:" + before[8..], t); return "Startup file disabled. Restore through Recovery. Refresh this inventory."; });
        } catch (Exception ex) { Output.Text = ex.Message; } });
    }
}
