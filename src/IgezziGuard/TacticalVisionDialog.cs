namespace IgezziGuard;

internal sealed class TacticalVisionDialog : Form
{
    private readonly CheckBox enabled;
    private readonly NumericUpDown strength;
    internal int SelectedStrength => enabled.Checked ? (int)strength.Value : 0;

    internal TacticalVisionDialog(GameEntry game)
    {
        Text = "Tactical Vision · " + game.Name;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(24), Dock = DockStyle.Fill };
        var description = new Label { AutoSize = true, MaximumSize = new Size(510, 0), Margin = new Padding(0, 0, 0, 18), Text =
            "Boost color saturation while this game is focused. NVIDIA Digital Vibrance affects the entire display containing the game. Your previous colors return when you switch away, exit the game, or close Hanki.\n\nKeep Hanki open. Requires an SDR display connected directly to NVIDIA; HDR is not supported. This changes color, not FPS." };
        enabled = new CheckBox { Text = "Enable Tactical Vision for this game", Checked = game.TacticalVision > 0, AutoSize = true, Margin = new Padding(0, 0, 0, 18) };
        var label = new Label { Text = "Digital Vibrance % (50 = neutral, 100 = maximum)", AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        strength = new NumericUpDown { Minimum = 51, Maximum = 100, Value = game.TacticalVision is >= 51 and <= 100 ? game.TacticalVision : 70, Width = 100, AccessibleName = "Digital Vibrance percentage", Enabled = enabled.Checked, Margin = new Padding(0, 0, 0, 18) };
        enabled.CheckedChanged += (_, _) => strength.Enabled = enabled.Checked;
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var save = new HankiButton { Text = Localizer.T("Save"), Primary = true, AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new HankiButton { Text = Localizer.T("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([save, cancel]);
        layout.Controls.AddRange([description, enabled, label, strength, buttons]);
        Controls.Add(layout);
        AcceptButton = save; CancelButton = cancel;
        HankiTheme.Apply(this);
    }
}
