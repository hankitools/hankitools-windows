using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IgezziGuard;

/// <summary>Dark native chrome (title bars, scrollbars, list headers, dropdowns). Unsupported Windows builds keep their default look; High Contrast restores system rendering.</summary>
internal static class NativeTheme
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    private const int WM_PAINT = 0x000F, WM_NCPAINT = 0x0085, WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008, WM_ENABLE = 0x000A, WM_PRINT = 0x0317, WM_PRINTCLIENT = 0x0318;
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
                case ComboBox combo:
                    SetWindowTheme(control.Handle, dark ? "DarkMode_CFD" : null, null);
                    if (dark && combo.DropDownStyle == ComboBoxStyle.DropDownList) _ = new ComboFrame(combo);
                    break;
                case TextBoxBase text:
                    SetWindowTheme(control.Handle, dark ? "DarkMode_CFD" : null, null);
                    if (dark && text.BorderStyle != BorderStyle.None) _ = new EditFrame(text);
                    break;
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

    /// <summary>Dark, owner-drawn items for drop-down lists (the closed box is drawn by <see cref="ComboFrame"/>).</summary>
    internal static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox box) return;
        bool hc = SystemInformation.HighContrast, selected = (e.State & DrawItemState.Selected) != 0;
        var back = hc ? (selected ? SystemColors.Highlight : SystemColors.Window) : selected ? HankiTheme.Raised : HankiTheme.Surface;
        var fore = hc ? (selected ? SystemColors.HighlightText : SystemColors.WindowText) : HankiTheme.Text;
        using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
        if (e.Index < 0 || e.Index >= box.Items.Count) return;
        int pad = (int)(8 * box.DeviceDpi / 96f);
        TextRenderer.DrawText(e.Graphics, box.GetItemText(box.Items[e.Index]), box.Font, new Rectangle(e.Bounds.X + pad, e.Bounds.Y, e.Bounds.Width - pad * 2, e.Bounds.Height), fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    /// <summary>
    /// Draws a closed drop-down list as a rounded field with a chevron, over the native box after it paints.
    /// The native control still handles input, focus and the list; High Contrast keeps the native look.
    /// </summary>
    private sealed class ComboFrame : NativeWindow
    {
        private readonly ComboBox box;
        internal ComboFrame(ComboBox box) { this.box = box; AssignHandle(box.Handle); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (SystemInformation.HighContrast || box.IsDisposed || !box.IsHandleCreated) return;
            if (m.Msg is WM_PAINT or WM_SETFOCUS or WM_KILLFOCUS or WM_ENABLE) { using var g = Graphics.FromHwnd(Handle); Paint(g); }
            // Printing (DrawToBitmap, screenshots) draws into the given device context.
            else if (m.Msg is WM_PRINT or WM_PRINTCLIENT && m.WParam != IntPtr.Zero) { using var g = Graphics.FromHdc(m.WParam); Paint(g); }
        }
        private void Paint(Graphics g)
        {
            float s = box.DeviceDpi / 96f;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(box.Parent?.BackColor ?? HankiTheme.Canvas);
            using (var path = HankiButton.Rounded(new RectangleF(0.5f, 0.5f, box.Width - 1.5f, box.Height - 1.5f), HankiTheme.ControlRadius * s)) {
                using var fill = new SolidBrush(HankiTheme.Surface); g.FillPath(fill, path);
                bool active = box.Focused || box.DroppedDown;
                using var pen = new Pen(active ? HankiTheme.Accent : HankiTheme.Border, active ? 1.5f * s : 1); g.DrawPath(pen, path);
            }
            string text = (box.SelectedIndex >= 0 ? box.GetItemText(box.SelectedItem) : box.Text) ?? "";
            var color = box.Enabled ? HankiTheme.Text : Color.FromArgb(110, 120, 134);
            TextRenderer.DrawText(g, text, box.Font, new Rectangle((int)(12 * s), 0, Math.Max(0, box.Width - (int)(40 * s)), box.Height), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            float cx = box.Width - 18 * s, cy = box.Height / 2f;
            using var chevron = new Pen(HankiTheme.Muted, 1.6f * s) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            g.DrawLines(chevron, [new PointF(cx - 4 * s, cy - 2 * s), new PointF(cx, cy + 2 * s), new PointF(cx + 4 * s, cy - 2 * s)]);
        }
    }

    /// <summary>A text box's frame in the theme's border color (accent when focused) instead of the light native edge.</summary>
    private sealed class EditFrame : NativeWindow
    {
        private readonly TextBoxBase box;
        internal EditFrame(TextBoxBase box) { this.box = box; AssignHandle(box.Handle); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (SystemInformation.HighContrast || box.IsDisposed || !box.IsHandleCreated) return;
            if (m.Msg is WM_NCPAINT or WM_SETFOCUS or WM_KILLFOCUS or WM_ENABLE) {
                var dc = GetWindowDC(Handle);
                if (dc == IntPtr.Zero) return;
                try { using var g = Graphics.FromHdc(dc); Paint(g); } finally { ReleaseDC(Handle, dc); }
            }
            // WM_PRINT's device context starts at the window's corner, like the window DC.
            else if (m.Msg == WM_PRINT && m.WParam != IntPtr.Zero && (m.LParam.ToInt64() & PRF_NONCLIENT) != 0) { using var g = Graphics.FromHdc(m.WParam); Paint(g); }
        }
        private const long PRF_NONCLIENT = 0x2;
        private void Paint(Graphics g)
        {
            int edgeX = Math.Max(1, (box.Width - box.ClientSize.Width) / 2), edgeY = Math.Max(1, (box.Height - box.ClientSize.Height) / 2);
            g.ExcludeClip(new Rectangle(edgeX, edgeY, box.ClientSize.Width, box.ClientSize.Height));
            g.Clear(box.BackColor);
            using var pen = new Pen(box.Focused ? HankiTheme.Accent : HankiTheme.Border);
            g.DrawRectangle(pen, 0, 0, box.Width - 1, box.Height - 1);
        }
    }
}
