namespace IgezziGuard;

internal sealed class ToolLauncher : Form
{
    internal sealed record Route(string Name, Action Open)
    {
        public override string ToString() => Name;
        internal string SearchText => Name + " " + string.Join(" ", new[] {
            Name.Contains("Diagnose", StringComparison.OrdinalIgnoreCase) ? "crash blue screen bsod freeze logs troubleshoot battery restart" : "",
            Name.Contains("Fix My PC", StringComparison.OrdinalIgnoreCase) ? "full system scan repair sfc dism health check app crashes crashing clock time sync ethernet cable link speed" : "",
            Name.Contains("Performance", StringComparison.OrdinalIgnoreCase) ? "slow speed optimize monitor benchmark bottleneck stutter compare baseline" : "",
            Name.Contains("Gaming", StringComparison.OrdinalIgnoreCase) ? "game fps refresh rate hz game mode" : "",
            Name == "GPU" ? "graphics card nvidia amd intel driver vram display pcie lanes slot resizable bar rebar smart access memory" : "",
            Name.StartsWith("CPU", StringComparison.Ordinal) ? "processor power plan high performance balanced" : "",
            Name == "Memory" ? "ram pagefile virtual memory commit" : "",
            Name == "Storage" ? "ssd nvme hdd drive trim" : "",
            Name.Contains("actions", StringComparison.OrdinalIgnoreCase) || Name.Contains("sessions", StringComparison.OrdinalIgnoreCase) ? "history log saved scans" : "",
            Name.Contains("Maintain", StringComparison.OrdinalIgnoreCase) ? "cleanup storage disk space files apps startup" : "",
            Name.Contains("Connect", StringComparison.OrdinalIgnoreCase) ? "internet wifi wi-fi network dns connection" : "",
            Name.Contains("Shield", StringComparison.OrdinalIgnoreCase) ? "security defender antivirus protection" : "",
            Name.Contains("Help", StringComparison.OrdinalIgnoreCase) ? "discord contact support bug report website version updates guide" : "",
            Name.Contains("Recovery", StringComparison.OrdinalIgnoreCase) ? "undo restore revert" : ""
        });
    }
    public ToolLauncher(IReadOnlyList<Route> routes)
    {
        Text = "Find a tool"; Size = new Size(700, 520); MinimumSize = new Size(520, 380);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        Font = new Font("Segoe UI", 11); Padding = new Padding(20); KeyPreview = true;
        var query = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search tools, e.g. DNS, duplicates, pagefile…", AccessibleName = "Search tools" };
        var results = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, ItemHeight = 32, AccessibleName = "Matching tools" };
        var hint = new Label { Dock = DockStyle.Bottom, Height = 32, Text = "↑ ↓ Choose a tool    Enter Open    Esc Close", TextAlign = ContentAlignment.MiddleLeft };
        var open = new HankiButton { Text = "Open selected tool", Dock = DockStyle.Bottom, Height = 42, Primary = true };
        void Filter() {
            results.BeginUpdate(); results.Items.Clear();
            var words = query.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var route in routes.Where(r => words.All(w => r.SearchText.Contains(w, StringComparison.CurrentCultureIgnoreCase)))) results.Items.Add(route);
            results.EndUpdate(); open.Enabled = results.Items.Count > 0;
            if (open.Enabled) results.SelectedIndex = 0;
            hint.Text = open.Enabled ? $"{results.Items.Count} tools    ↑ ↓ Choose    Enter Open    Esc Close" : "No matching tools. Try a broader term, such as network.";
        }
        void Open() { if (results.SelectedItem is Route route) { Close(); route.Open(); } }
        query.TextChanged += (_, _) => Filter(); open.Click += (_, _) => Open();
        results.DoubleClick += (_, _) => Open();
        KeyDown += (_, e) => {
            if (e.KeyCode == Keys.Escape) { Close(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Enter) { Open(); e.SuppressKeyPress = true; }
            if (query.Focused && e.KeyCode is Keys.Down or Keys.Up && results.Items.Count > 0) {
                results.SelectedIndex = Math.Clamp(results.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1), 0, results.Items.Count - 1); e.SuppressKeyPress = true;
            }
        };
        var gap = new Panel { Dock = DockStyle.Top, Height = 16 };
        Controls.Add(results); Controls.Add(gap); Controls.Add(query); Controls.Add(hint); Controls.Add(open);
        HankiTheme.Apply(this); Filter(); Shown += (_, _) => query.Focus();
    }
}
