using System.Text;
namespace IgezziGuard;

/// <summary>
/// Gaming → NVIDIA: the global 3D settings with presets, your own presets and single-setting edits (HANKI-GPU-113).
/// Every change goes through the review dialog and Recovery; nothing is written until you approve it.
/// </summary>
public sealed class NvidiaPanel : ToolPage
{
    private readonly ComboBox preset = new() { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "NVIDIA preset", Margin = new Padding(0, 6, 8, 0) };
    private readonly GamingState state;
    private IReadOnlyList<NvidiaGlobalSetting> current = [];
    private IReadOnlyList<NvidiaUserPreset> saved = [];
    private IReadOnlyList<NvidiaPreset> presets = [];
    private bool loaded;

    internal NvidiaPanel(GamingState state) : base("NVIDIA's global 3D settings: the ones NVIDIA Control Panel lists under Manage 3D settings → Global Settings. They apply to every game that has no value of its own. Choose a preset or edit single settings. You review each change first, Hanki saves the current value in Recovery so you can undo it, and only settings from NVIDIA's public SDK that your driver itself names as expected are offered.")
    {
        this.state = state;
        Bar.Controls.Add(preset);
        Button("Review preset", ReviewPreset);
        Button("Edit settings…", Edit);
        Button("Save current as preset…", SavePreset);
        Button("Delete my preset", DeletePreset);
        Button("Refresh", async () => await ReadSettings());
        preset.SelectedIndexChanged += (_, _) => { if (!IsBusy && Selected is { } p && current.Count > 0) Output.Text = Explain(p); };
        VisibleChanged += async (_, _) => { if (Visible && !loaded) { loaded = true; await ReadSettings(); } };
    }

    private NvidiaPreset? Selected => preset.SelectedIndex >= 0 && preset.SelectedIndex < presets.Count ? presets[preset.SelectedIndex] : null;
    private double RefreshHz => state.Graphics?.Displays.Where(d => d.Primary).Select(d => d.Current.RefreshHz).DefaultIfEmpty(60).First() ?? 60;
    private const string NotReady = "NVIDIA settings aren't available on this PC. They need an NVIDIA graphics card with its driver installed.";

    private async Task ReadSettings()
    {
        await Run(async token => await Task.Run(() => {
            state.Graphics ??= GraphicsProbe.Collect();
            try { current = Nvidia.ReadGlobal(); }
            catch (NvidiaException ex) { current = []; return Diagnosis.From(ex.Message, [new("NVIDIA settings", NotReady + " " + ex.Message, CardStatus.Unknown)], "NVIDIA settings unavailable"); }
            try { saved = NvidiaPresets.Read(NvidiaPresets.StorePath); } catch (IOException ex) { saved = []; return Summary(ex.Message); }
            return Summary(null);
        }, token));
        FillPresets();
    }
    /// <summary>Re-reads the settings after a change without replacing the change report.</summary>
    private async Task Reload()
    {
        try { current = await Task.Run(Nvidia.ReadGlobal); } catch (NvidiaException) { current = []; }
    }
    private void FillPresets(string? select = null)
    {
        presets = NvidiaPresets.BuiltIn(RefreshHz).Concat(saved.Select(NvidiaPresets.FromUser)).ToArray();
        preset.Items.Clear();
        foreach (var p in presets) preset.Items.Add(p.BuiltIn ? p.Name : "My preset: " + p.Name);
        int index = select is null ? 0 : presets.ToList().FindIndex(p => !p.BuiltIn && p.Name == select);
        if (preset.Items.Count > 0) preset.SelectedIndex = Math.Max(0, index);
    }

    private Diagnosis Summary(string? note)
    {
        var changed = current.Where(c => !c.IsDefault).ToArray();
        var report = new StringBuilder("NVIDIA global 3D settings • " + DateTimeOffset.Now.ToString("g") + "\r\nRead-only; nothing was changed.\r\n\r\n");
        foreach (var c in current) report.AppendLine($"{c.Setting.Name}: {c.Text}{(c.IsDefault ? " (NVIDIA default)" : " (changed from NVIDIA's default)")}");
        report.AppendLine("\r\nGames with their own NVIDIA profile value keep it. NVIDIA's “Ultra” low latency and background frame limit aren't in its public SDK, so Hanki doesn't change them.");
        if (note is not null) report.AppendLine("\r\nNote: " + note);
        var cards = new List<ResultCard> {
            new("Global settings", changed.Length == 0 ? "Every setting Hanki manages is at NVIDIA's default." :
                $"{changed.Length} of {current.Count} settings differ from NVIDIA's defaults: " + string.Join(", ", changed.Select(c => $"{c.Setting.Name} {c.Text}")) + ".", CardStatus.Info),
            new("Presets", "Choose a preset and Review preset to see exactly what it would change. Edit settings… changes single settings; Save current as preset… keeps your setup to apply again later.", CardStatus.Info),
        };
        if (note is not null) cards.Add(new("Your presets", note, CardStatus.Unknown));
        return Diagnosis.From(report.ToString(), cards, changed.Length == 0 ? "NVIDIA settings are at their defaults" : $"{changed.Length} NVIDIA {(changed.Length == 1 ? "setting differs" : "settings differ")} from the defaults");
    }
    private string Explain(NvidiaPreset p)
    {
        var changes = NvidiaPresets.Propose(p, current);
        return $"{p.Name}\r\n{p.Description}\r\n\r\n" + (changes.Count == 0 ? "Your global settings already match this preset." :
            $"It would change {changes.Count} {(changes.Count == 1 ? "setting" : "settings")}:\r\n" + string.Join("\r\n", changes.Select(c => $"• {c.Setting}: {c.Current} → {c.Recommended}{(c.Optional ? " (optional)" : "")}"))) +
            "\r\n\r\nChoose Review preset to pick which changes to apply.";
    }

    private async void ReviewPreset()
    {
        if (current.Count == 0) { Output.Text = NotReady; return; }
        if (Selected is not { } p) return;
        var changes = NvidiaPresets.Propose(p, current);
        if (changes.Count == 0) { Output.Text = $"Your global NVIDIA settings already match “{p.Name}”. Nothing to change."; return; }
        await ChangeReview.ReviewAndApply(this, changes, $"NVIDIA preset “{p.Name}” · all games", $"NVIDIA preset: {p.Name}", text => Output.Text = text);
        await Reload();
    }
    private async void Edit()
    {
        if (current.Count == 0) { Output.Text = NotReady; return; }
        var changes = NvidiaEditorDialog.Show(this, current, RefreshHz);
        if (changes is null) { Output.Text = "Nothing was changed."; return; }
        if (changes.Count == 0) { Output.Text = "No setting was changed in the editor."; return; }
        await ChangeReview.ReviewAndApply(this, changes, "NVIDIA global settings · all games", "NVIDIA settings edited", text => Output.Text = text);
        await Reload();
    }
    private void SavePreset()
    {
        if (current.Count == 0) { Output.Text = NotReady; return; }
        var name = TextPromptDialog.Ask(this, "Save as preset", "Name for a preset holding your current global NVIDIA settings:", "");
        if (name is null) return;
        if (NvidiaPresets.NameProblem(name, saved.Select(s => s.Name)) is { } problem) { Output.Text = problem; return; }
        try {
            var updated = saved.Append(NvidiaPresets.Capture(name, current)).ToArray();
            NvidiaPresets.Write(NvidiaPresets.StorePath, updated);
            saved = updated;
            FillPresets(name.Trim());
            Output.Text = $"Saved “{name.Trim()}”. It keeps the settings you or Hanki changed; the others are saved as NVIDIA's default. Apply it later with Review preset.";
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Output.Text = "The preset couldn't be saved: " + ex.Message; }
    }
    private void DeletePreset()
    {
        if (Selected is not { BuiltIn: false } p) { Output.Text = "Choose one of your own presets (“My preset: …”) to delete it. Built-in presets can't be deleted."; return; }
        if (!Review($"Delete your preset “{p.Name}”? Your NVIDIA settings aren't changed.")) return;
        try {
            var updated = saved.Where(s => s.Name != p.Name).ToArray();
            NvidiaPresets.Write(NvidiaPresets.StorePath, updated);
            saved = updated;
            FillPresets();
            Output.Text = $"Deleted “{p.Name}”.";
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Output.Text = "The preset couldn't be deleted: " + ex.Message; }
    }
}

/// <summary>A Manage 3D settings-style list: one choice per setting, starting at the current value. Only changed rows become proposed changes.</summary>
internal static class NvidiaEditorDialog
{
    internal static IReadOnlyList<ProposedChange>? Show(IWin32Window owner, IReadOnlyList<NvidiaGlobalSetting> current, double refreshHz)
    {
        var (dialog, changes) = Build(current, refreshHz);
        using (dialog) return dialog.ShowDialog(owner) == DialogResult.OK ? changes() : null;
    }
    internal static (Form Dialog, Func<IReadOnlyList<ProposedChange>> Changes) Build(IReadOnlyList<NvidiaGlobalSetting> current, double refreshHz)
    {
        var dialog = new Form { Text = "NVIDIA global settings", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20) };
        var outer = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        outer.Controls.Add(new Label { Text = "Choose a value for each setting you want to change. These apply to every game without its own value. You review the changes next; nothing is written yet.",
            AutoSize = true, MaximumSize = new Size(620, 0), Tag = "intro", Margin = new Padding(0, 0, 0, 10) });
        var scroll = new Panel { AutoScroll = true, Width = 650, Height = Math.Min(520, 40 + current.Count * 44), Margin = Padding.Empty };
        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Location = Point.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
        var rows = new List<(NvidiaGlobalSetting Setting, ComboBox Box, IReadOnlyList<(string Label, uint? Value)> Choices, int Initial)>();
        foreach (var c in current) {
            var choices = NvidiaPresets.Choices(c.Setting, refreshHz, c).ToList();
            int initial = c.IsDefault ? 0 : choices.FindIndex(x => x.Value == c.Effective);
            if (initial < 0) { choices.Add(("Current: " + c.Text, c.Effective)); initial = choices.Count - 1; }
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270, AccessibleName = c.Setting.Name, Margin = new Padding(0, 6, 0, 6) };
            foreach (var (label, _) in choices) box.Items.Add(label);
            box.SelectedIndex = initial;
            table.Controls.Add(new Label { Text = $"{c.Setting.Name}\r\nNow: {c.Text}{(c.IsDefault ? " (NVIDIA default)" : "")}", AutoSize = true, MaximumSize = new Size(320, 0), UseMnemonic = false, Margin = new Padding(0, 4, 8, 4) });
            table.Controls.Add(box);
            rows.Add((c, box, choices, initial));
        }
        scroll.Controls.Add(table);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        var review = new HankiButton { Text = "Review changes", Primary = true, AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new HankiButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([review, cancel]);
        outer.Controls.AddRange([scroll, buttons]);
        dialog.Controls.Add(outer); dialog.AcceptButton = review; dialog.CancelButton = cancel;
        HankiTheme.Apply(dialog);
        return (dialog, () => rows.Where(r => r.Box.SelectedIndex != r.Initial && r.Box.SelectedIndex >= 0)
            .Select(r => NvidiaPresets.Change(r.Setting, r.Choices[r.Box.SelectedIndex].Value, "Chosen in the settings editor."))
            .OfType<ProposedChange>().ToArray());
    }
}

/// <summary>A one-line text question with OK and Cancel.</summary>
internal static class TextPromptDialog
{
    internal static string? Ask(IWin32Window owner, string title, string prompt, string initial)
    {
        using var dialog = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20) };
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        var text = new TextBox { Width = 380, Text = initial, AccessibleName = prompt, MaxLength = 200 };
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        var ok = new HankiButton { Text = "OK", Primary = true, AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new HankiButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([ok, cancel]);
        layout.Controls.AddRange([new Label { Text = prompt, AutoSize = true, MaximumSize = new Size(380, 0), UseMnemonic = false, Margin = new Padding(0, 0, 0, 6) }, text, buttons]);
        dialog.Controls.Add(layout); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        HankiTheme.Apply(dialog);
        return dialog.ShowDialog(owner) == DialogResult.OK ? text.Text : null;
    }
}
