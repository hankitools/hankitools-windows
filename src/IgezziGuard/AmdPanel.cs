using System.Text;
namespace IgezziGuard;

/// <summary>
/// Gaming → AMD Radeon: Radeon 3D settings through AMD's driver interface (HANKI-GPU-103, HANKI-GAME-203). Every change
/// goes through the review dialog and Recovery. Not yet tested on AMD hardware.
/// </summary>
public sealed class AmdPanel : ToolPage
{
    private readonly GamingState state;
    private AmdGpuSettings? current;
    private bool loaded;
    private const string Needs = "Radeon settings need an AMD Radeon graphics card with AMD Software: Adrenalin Edition installed.";
    private const string NotReady = "Radeon settings aren't available on this PC. " + Needs;

    internal AmdPanel(GamingState state) : base("Radeon settings from AMD Software: Anti-Lag, Chill, Boost, Image Sharpening, Enhanced Sync, Wait for Vertical Refresh, Frame Rate Target Control and Anisotropic Filtering. You review each change first, and Hanki saves the current value in Recovery so you can undo it. Not yet tested on AMD hardware: please report anything that looks wrong.")
    {
        this.state = state;
        Button("Edit settings…", Edit);
        Button("Refresh", async () => await ReadSettings());
        VisibleChanged += async (_, _) => { if (Visible && !loaded) { loaded = true; await ReadSettings(); } };
    }
    private uint RefreshHz => (uint)Math.Round(state.Graphics?.Displays.Where(d => d.Primary).Select(d => d.Current.RefreshHz).DefaultIfEmpty(60).First() ?? 60);

    private async Task ReadSettings()
    {
        await Run(async token => await Task.Run(() => {
            state.Graphics ??= GraphicsProbe.Collect();
            try { current = Amd.ReadSettings(); }
            catch (AmdException ex) { current = null; return Diagnosis.From(ex.Message, [new("Radeon settings", $"{ex.Message} {Needs}", CardStatus.Unknown)], "Radeon settings unavailable"); }
            var report = new StringBuilder($"Radeon settings • {current.GpuName} • {DateTimeOffset.Now:g}\r\nRead-only; nothing was changed.\r\n\r\n");
            foreach (var s in current.Settings) report.AppendLine($"{AmdSettings.Name(s.Kind)}: {s.Text}" + (s.RangeMin is { } lo && s.RangeMax is { } hi ? $" (driver range {lo}–{hi})" : ""));
            report.AppendLine("\r\nSettings your card or driver doesn't support aren't listed. Tune my PC applies the matching Radeon settings for each choice.");
            return Diagnosis.From(report.ToString(), [
                new(current.GpuName, string.Join(" · ", current.Settings.Select(s => $"{AmdSettings.Name(s.Kind)}: {s.Text}")), CardStatus.Info),
                new("Change them", "Edit settings… changes single settings. Tune my PC on the Performance page sets them for gaming, creative work or low power.", CardStatus.Info)
            ], $"Radeon settings for {current.GpuName}");
        }, token));
    }

    private async void Edit()
    {
        if (current is not { } gpu) { Output.Text = NotReady; return; }
        var rows = gpu.Settings.Select(s => (Setting: s, Choices: AmdSettings.Choices(s, RefreshHz))).ToArray();
        var picked = ChoiceEditorDialog.Show(this, "Radeon settings", "Choose a value for each setting you want to change. You review the changes next; nothing is written yet.",
            rows.Select(r => (AmdSettings.Name(r.Setting.Kind), r.Setting.Text, r.Choices.Select(c => c.Label).ToArray())).ToArray());
        if (picked is null) { Output.Text = "Nothing was changed."; return; }
        var changes = rows.Select((r, i) => picked[i] > 0 ? AmdSettings.ChangeTo(gpu, r.Setting.Kind, r.Choices[picked[i]].State, "Chosen in the settings editor.") : null).OfType<ProposedChange>().ToArray();
        if (changes.Length == 0) { Output.Text = "No setting was changed in the editor."; return; }
        await ChangeReview.ReviewAndApply(this, changes, $"Radeon settings · {gpu.GpuName}", "Radeon settings edited", text => Output.Text = text);
        try { current = await Task.Run(Amd.ReadSettings); } catch (AmdException) { current = null; }
    }
}

/// <summary>A list of settings, each with a drop-down whose first item is the current value. Returns the chosen index per row (0 = unchanged), or null when cancelled.</summary>
internal static class ChoiceEditorDialog
{
    internal static int[]? Show(IWin32Window owner, string title, string intro, IReadOnlyList<(string Name, string Now, string[] Choices)> rows)
    {
        using var dialog = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20) };
        var outer = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        outer.Controls.Add(new Label { Text = intro, AutoSize = true, MaximumSize = new Size(620, 0), Tag = "intro", Margin = new Padding(0, 0, 0, 10) });
        var scroll = new Panel { AutoScroll = true, Width = 650, Height = Math.Min(520, 40 + rows.Count * 48), Margin = Padding.Empty };
        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Location = Point.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        var boxes = new List<ComboBox>();
        foreach (var (name, now, choices) in rows) {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, AccessibleName = name, Margin = new Padding(0, 6, 0, 6) };
            box.Items.AddRange(choices.Cast<object>().ToArray());
            if (box.Items.Count > 0) box.SelectedIndex = 0;
            table.Controls.Add(new Label { Text = $"{name}\r\nNow: {now}", AutoSize = true, MaximumSize = new Size(290, 0), UseMnemonic = false, Margin = new Padding(0, 4, 8, 4) });
            table.Controls.Add(box);
            boxes.Add(box);
        }
        scroll.Controls.Add(table);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        var review = new HankiButton { Text = "Review changes", Primary = true, AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new HankiButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([review, cancel]);
        outer.Controls.AddRange([scroll, buttons]);
        dialog.Controls.Add(outer); dialog.AcceptButton = review; dialog.CancelButton = cancel;
        HankiTheme.Apply(dialog);
        return dialog.ShowDialog(owner) == DialogResult.OK ? boxes.Select(b => Math.Max(0, b.SelectedIndex)).ToArray() : null;
    }
}
