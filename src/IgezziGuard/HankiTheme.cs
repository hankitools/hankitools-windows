namespace IgezziGuard;

/// <summary>Set both sides of each color pair; never depend on parent color inheritance.</summary>
internal static class HankiTheme
{
    internal static readonly Color Canvas = Color.FromArgb(16, 19, 24);
    internal static readonly Color Surface = Color.FromArgb(24, 28, 35);
    internal static readonly Color Raised = Color.FromArgb(33, 39, 48);
    internal static readonly Color Text = Color.FromArgb(240, 243, 247);
    internal static readonly Color Muted = Color.FromArgb(160, 171, 186);
    internal static readonly Color Accent = Color.FromArgb(101, 181, 255);
    // Hanki Performance uses the same design with its own accent, so the current area is always recognisable.
    internal static readonly Color PerformanceAccent = Color.FromArgb(186, 150, 255);
    internal static Color AreaAccent(ProductArea area) => area == ProductArea.Performance ? PerformanceAccent : Accent;
    internal static readonly Color PrimaryFill = Color.FromArgb(0, 105, 220);
    internal static readonly Color PrimaryHover = Color.FromArgb(18, 122, 238);
    internal static readonly Color Border = Color.FromArgb(44, 51, 62);
    /// <summary>The soft edge of cards and tiles: visible enough to separate surfaces, quieter than control borders.</summary>
    internal static readonly Color Hairline = Color.FromArgb(34, 39, 48);
    /// <summary>Corner radius of cards and tiles, and of buttons and fields (logical pixels).</summary>
    internal const float CardRadius = 12, ControlRadius = 8;

    internal static readonly Color Pine = Color.FromArgb(12, 14, 18);

    // Status colors describe collected evidence, never an overall PC-health score.
    internal static readonly Color Success = Color.FromArgb(74, 196, 120);
    internal static readonly Color Warning = Color.FromArgb(240, 176, 64);
    internal static readonly Color Critical = Color.FromArgb(240, 96, 96);

    internal static Color SeverityColor(FindingSeverity severity, CollectionOutcome outcome) =>
        SystemInformation.HighContrast ? SystemColors.ControlText :
        outcome is CollectionOutcome.Failed or CollectionOutcome.Unavailable or CollectionOutcome.Cancelled ? Muted : severity switch {
            FindingSeverity.Critical => Critical, FindingSeverity.Warning => Warning,
            FindingSeverity.Healthy => Success, FindingSeverity.Informational => Accent, _ => Muted };

    internal static Color StatusColor(CardStatus status) => status switch {
        CardStatus.Good => Success, CardStatus.Review => Warning, CardStatus.Problem => Critical, CardStatus.Unknown => Muted, _ => Accent };

    internal static string SeverityLabel(FindingSeverity severity, CollectionOutcome outcome) => outcome switch {
        CollectionOutcome.Failed => "Failed", CollectionOutcome.Unavailable => "Unavailable", CollectionOutcome.Cancelled => "Cancelled",
        _ => severity switch { FindingSeverity.Critical => "Critical", FindingSeverity.Warning => "Warning",
            FindingSeverity.Healthy => "OK", FindingSeverity.Informational => "Info", _ => "Unknown" } };

    private static bool IsPine(Control root)
    {
        for (Control? current = root; current is not null; current = current.Parent)
            if (current.Tag as string == "pine") return true;
        return false;
    }

    public static void Apply(Control root)
    {
        bool highContrast = SystemInformation.HighContrast;
        // Native TabControl can report a light system BackColor despite our setter.
        // Never propagate that value into dark-themed pages, panels or labels.
        bool pine = IsPine(root);
        bool cardSurface = root.Tag as string == "card" || root.Parent?.Tag as string == "card";
        root.BackColor = highContrast ? SystemColors.Control : pine ? Pine : cardSurface ? Surface : Canvas;
        root.ForeColor = highContrast ? SystemColors.ControlText : root.Tag switch {
            "intro" => Muted, "accent" => Accent, "accent-performance" => PerformanceAccent,
            "status-good" => Success, "status-review" => Warning, "status-problem" => Critical, "status-unknown" => Muted,
            _ => Text };
        switch (root)
        {
            case Label label:
                // Labels such as "Help & community" are text, not mnemonics.
                label.UseMnemonic = false;
                break;
            case Button button:
                button.UseVisualStyleBackColor = false;
                button.BackColor = highContrast ? SystemColors.Control : Raised;
                button.ForeColor = highContrast ? SystemColors.ControlText : Text;
                if (button is HankiButton modern) modern.FlatStyle = highContrast ? FlatStyle.Standard : FlatStyle.Flat;
                break;
            case TabControl tabs:
                // Native tab headers use the Windows surface, not the dark page surface.
                tabs.BackColor = highContrast ? SystemColors.Control : Canvas;
                tabs.ForeColor = highContrast ? SystemColors.ControlText : Text;
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                page.BackColor = highContrast ? SystemColors.Control : Canvas;
                break;
            case TextBoxBase:
            case ListView:
            case ListBox:
            case NumericUpDown:
            case ComboBox:
            case DateTimePicker:
                // A search field paints its own rounded frame on the canvas color; the box inside matches it.
                root.BackColor = highContrast ? SystemColors.Window : root.Parent is SearchField ? Canvas : Surface;
                root.ForeColor = highContrast ? SystemColors.WindowText : Text;
                // A plain single border ignores the dark theme; the themed client edge follows it.
                if (root is TextBox { BorderStyle: BorderStyle.FixedSingle } text) text.BorderStyle = BorderStyle.Fixed3D;
                if (!highContrast) {
                    // Drop-down lists: owner-drawn dark items at the height of the buttons beside them.
                    if (root is ComboBox { DropDownStyle: ComboBoxStyle.DropDownList, DrawMode: DrawMode.Normal } combo) {
                        combo.DrawMode = DrawMode.OwnerDrawFixed; combo.ItemHeight = (int)(28 * combo.DeviceDpi / 96f); combo.DrawItem += NativeTheme.DrawComboItem;
                    }
                    // Lists sit on their own surface; the white native edge goes.
                    if (root is ListView { BorderStyle: not BorderStyle.None } list) list.BorderStyle = BorderStyle.None;
                    if (root is ListBox { BorderStyle: not BorderStyle.None } listBox) listBox.BorderStyle = BorderStyle.None;
                    if (root is UpDownBase { BorderStyle: BorderStyle.Fixed3D } upDown) upDown.BorderStyle = BorderStyle.FixedSingle;
                }
                // Multiline edges and scrollbars cannot both be dark; use a filled, borderless box with inner margins.
                if (!highContrast && root is TextBoxBase { Multiline: true } multiline && multiline.BorderStyle != BorderStyle.None) {
                    multiline.BorderStyle = BorderStyle.None; NativeTheme.PadText(multiline);
                }
                break;
            case CheckBox check:
                check.UseVisualStyleBackColor = false;
                // The standard glyph stays white on dark surfaces; the flat glyph follows the theme.
                check.FlatStyle = highContrast ? FlatStyle.Standard : FlatStyle.Flat;
                check.FlatAppearance.BorderColor = Muted;
                check.FlatAppearance.CheckedBackColor = check.BackColor;
                check.FlatAppearance.MouseOverBackColor = Raised;
                break;
        }
        NativeTheme.Apply(root);
        foreach (Control child in root.Controls) Apply(child);
    }
}
