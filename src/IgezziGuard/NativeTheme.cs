using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IgezziGuard;

/// <summary>Dark native chrome (title bars, scrollbars, list headers, dropdowns). Unsupported Windows builds keep their default look; High Contrast restores system rendering.</summary>
internal static class NativeTheme
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int LVM_GETHEADER = 0x101F;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;
    private const int EM_SETMARGINS = 0xD3, EC_LEFTMARGIN = 1, EC_RIGHTMARGIN = 2;
    private static readonly ConditionalWeakTable<Control, object> Hooked = new(), Padded = new();

    /// <summary>Inner left/right margins for a text box whose border was removed.</summary>
    internal static void PadText(TextBoxBase text) { if (!Padded.TryGetValue(text, out _)) Padded.Add(text, new object()); }

    internal static void Apply(Control control)
    {
        if (control is not (Form or ListView or TextBoxBase or ComboBox or ListBox or TreeView or ScrollableControl { AutoScroll: true })) return;
        if (!Hooked.TryGetValue(control, out _)) {
            Hooked.Add(control, new object());
            control.HandleCreated += (_, _) => Update(control);
            if (control is ListView list) StyleHeader(list);
        }
        if (control.IsHandleCreated) Update(control);
    }

    private static void Update(Control control)
    {
        bool dark = !SystemInformation.HighContrast;
        try {
            switch (control) {
                case Form form: TitleBar(form.Handle, dark); break;
                case ListView list:
                    SetWindowTheme(list.Handle, dark ? "DarkMode_Explorer" : null, null);
                    var header = SendMessage(list.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                    if (header != IntPtr.Zero) SetWindowTheme(header, dark ? "DarkMode_ItemsView" : null, null);
                    break;
                case TextBoxBase { Multiline: true } multiline:
                    SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : null, null);
                    if (Padded.TryGetValue(multiline, out _)) {
                        int margin = (int)(10 * multiline.DeviceDpi / 96f);
                        SendMessage(multiline.Handle, EM_SETMARGINS, (IntPtr)(EC_LEFTMARGIN | EC_RIGHTMARGIN), (IntPtr)(margin | margin << 16));
                    }
                    break;
                case ComboBox or TextBoxBase: SetWindowTheme(control.Handle, dark ? "DarkMode_CFD" : null, null); break;
                default: SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : null, null); break;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    private static void TitleBar(IntPtr handle, bool dark)
    {
        int enabled = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        if (!dark) return;
        // Windows 11 caption colors; earlier builds ignore these attributes.
        int caption = ColorRef(HankiTheme.Pine), border = ColorRef(HankiTheme.Border), text = ColorRef(HankiTheme.Text);
        DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }
    private static int ColorRef(Color c) => c.R | c.G << 8 | c.B << 16;

    // Native headers keep light text on the dark header theme, so column cells are painted here; rows stay native.
    private static void StyleHeader(ListView list)
    {
        if (list.OwnerDraw) return;
        list.OwnerDraw = true;
        list.DrawItem += (_, e) => e.DrawDefault = true;
        list.DrawSubItem += (_, e) => e.DrawDefault = true;
        list.DrawColumnHeader += (_, e) => {
            if (SystemInformation.HighContrast) { e.DrawDefault = true; return; }
            using var fill = new SolidBrush(HankiTheme.Surface); e.Graphics.FillRectangle(fill, e.Bounds);
            using var line = new Pen(HankiTheme.Border);
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top + 6, e.Bounds.Right - 1, e.Bounds.Bottom - 7);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, list.Font, Rectangle.Inflate(e.Bounds, -8, 0), HankiTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
    }
}
