using Microsoft.Win32;

namespace IgezziGuard;

internal sealed class UserRunBackend(RegistryHive hive = RegistryHive.CurrentUser, RegistryView view = RegistryView.Default) : IStartupBackend
{
    internal const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public StartupValue? Read(string name)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var key = root.OpenSubKey(KeyPath);
        if (key is null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
        return new(name, key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string
            ?? throw new IOException("Unsupported Run value type."), (int)key.GetValueKind(name));
    }
    public void Remove(string name) { using var root = RegistryKey.OpenBaseKey(hive, view); using var key = root.OpenSubKey(KeyPath, true) ?? throw new IOException("Run key missing."); key.DeleteValue(name, true); key.Flush(); }
    public void Restore(StartupValue value) {
        using var root = RegistryKey.OpenBaseKey(hive, view); using var key = root.CreateSubKey(KeyPath);
        if (key.GetValueNames().Contains(value.Name, StringComparer.OrdinalIgnoreCase)) throw new IOException("Value appeared before restore. Refresh history.");
        key.SetValue(value.Name, value.Command, (RegistryValueKind)value.Kind); key.Flush();
    }
    public List<StartupValue> All() {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var key = root.OpenSubKey(KeyPath);
        return key is null ? [] : key.GetValueNames().Select(Read).OfType<StartupValue>().ToList();
    }
}

public sealed class StartupPanel : UserControl
{
    private UserRunBackend backend = new();
    private StartupActions actions;
    private readonly ListView entries = List();
    private readonly ListView history = List();
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 85 };
    public StartupPanel()
    {
        Dock = DockStyle.Fill;
        actions = new(backend, Path.Combine(SecurityPaths.Root, "startup-actions.json"));
        var pages = new HankiTabs { Dock = DockStyle.Fill };
        var startup = new TabPage("Startup entries"); var journal = new TabPage("Action history / undo");
        entries.Columns.Add("Name", 220); entries.Columns.Add("Registered command (never executed by Hanki)", 600);
        history.Columns.Add("Time", 165); history.Columns.Add("Name", 200); history.Columns.Add("State", 150); history.Columns.Add("Saved command", 420);
        var scope = new ComboBox { Width = 210, DropDownStyle = ComboBoxStyle.DropDownList };
        scope.Items.AddRange(["Current user", "All users (64-bit)", "All users (32-bit)"]); scope.SelectedIndex = 0;
        scope.SelectedIndexChanged += (_, _) => {
            backend = scope.SelectedIndex == 0 ? new() : new(RegistryHive.LocalMachine, scope.SelectedIndex == 1 ? RegistryView.Registry64 : RegistryView.Registry32);
            actions = new(backend, Path.Combine(SecurityPaths.Root, scope.SelectedIndex == 0 ? "startup-actions.json" : $"startup-machine-{scope.SelectedIndex}.json"));
            entries.Items.Clear(); history.Items.Clear(); Run(RefreshData);
        };
        var refresh = new HankiButton { Text = "Refresh entries / history", Primary = true, AutoSize = true };
        var disable = new HankiButton { Text = "Review / disable selected…", AutoSize = true };
        var windows = new HankiButton { Text = "All startup apps in Windows…", Appearance = HankiButtonStyle.Quiet, AutoSize = true };
        var undo = new HankiButton { Text = "Review / restore selected…", AutoSize = true };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true }; bar.Controls.AddRange([scope, refresh, disable, windows]);
        var undoBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true }; undoBar.Controls.Add(undo);
        startup.Controls.Add(entries); startup.Controls.Add(bar); journal.Controls.Add(history); journal.Controls.Add(undoBar);
        pages.TabPages.AddRange([startup, journal]); Controls.Add(pages); Controls.Add(status);
        status.Text = "Click Refresh. User and machine Run registry entries are managed by scope; listed does not mean enabled in Windows Startup settings.\nDisabling removes the selected registration for future sign-ins, not the running process. Machine-wide changes may require administrator rights. Startup-folder files are in their own tab; scheduled tasks remain in Windows. Backups contain command paths and must be kept for undo.";
        refresh.Click += (_, _) => Run(RefreshData);
        windows.Click += (_, _) => DesktopShortcuts.Open(this, "startup");
        disable.Click += (_, _) => {
            if (entries.SelectedItems.Count != 1 || entries.SelectedItems[0].Tag is not StartupValue value) return;
            if (!Confirm($"Remove this startup registration in the selected scope?\n\n{value.Name}\n{value.Command}\n\nHanki will save the exact value for undo. No program is stopped or uninstalled. Disabling a security, sync or accessibility app may stop its expected function at your next sign-in.")) return;
            Run(() => { actions.Disable(value); RefreshData(); });
        };
        undo.Click += (_, _) => {
            if (history.SelectedItems.Count != 1 || history.SelectedItems[0].Tag is not StartupAction action) return;
            if (!Confirm($"Restore the saved startup registration?\n\n{action.Original.Name}\n{action.Original.Command}\n\nThis permits Windows to launch it at sign-in, subject to Windows Startup settings. Hanki does not launch it now. A conflicting current value will not be overwritten.")) return;
            Run(() => { actions.Undo(action.Id); RefreshData(); });
        };
    }
    private bool Confirm(string text) => MessageBox.Show(this, text, "Review startup change", MessageBoxButtons.OKCancel,
        MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.OK;
    private void Run(Action work) { try { work(); } catch (Exception ex) { MessageBox.Show(this, ex.Message + "\nRefresh and inspect Action history before retrying.", "Startup action stopped"); } }
    private void RefreshData() {
        var values = backend.All(); var saved = actions.ReadHistory();
        entries.Items.Clear(); history.Items.Clear();
        foreach (var v in values) entries.Items.Add(new ListViewItem([v.Name, v.Command]) { Tag = v });
        foreach (var a in saved.OrderByDescending(a => a.At)) history.Items.Add(new ListViewItem([a.At.ToString("g"), a.Original.Name, a.State, a.Original.Command]) { Tag = a });
    }
    private static ListView List() => new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
}
