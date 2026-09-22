namespace IgezziGuard;

/// <summary>Set both sides of each color pair; never depend on parent color inheritance.</summary>
internal static class HankiTheme
{
    internal static readonly Color Canvas = Color.FromArgb(18, 22, 28);
    internal static readonly Color Surface = Color.FromArgb(24, 30, 38);
    internal static readonly Color Raised = Color.FromArgb(33, 43, 56);
    internal static readonly Color Text = Color.FromArgb(242, 241, 237);
    internal static readonly Color Muted = Color.FromArgb(175, 188, 204);
    internal static readonly Color Accent = Color.FromArgb(101, 181, 255);
    internal static readonly Color PrimaryFill = Color.FromArgb(0, 105, 220);
    internal static readonly Color Border = Color.FromArgb(65, 82, 103);

    internal static readonly Color Pine = Color.FromArgb(12, 16, 22);

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
        root.ForeColor = highContrast ? SystemColors.ControlText : root.Tag as string == "intro" ? Muted : Text;
        switch (root)
        {
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
                root.BackColor = highContrast ? SystemColors.Window : Surface;
                root.ForeColor = highContrast ? SystemColors.WindowText : Text;
                break;
            case CheckBox check:
                check.UseVisualStyleBackColor = false;
                break;
        }
        foreach (Control child in root.Controls) Apply(child);
    }
}
