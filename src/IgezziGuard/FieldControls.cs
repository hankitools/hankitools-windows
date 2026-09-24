using System.Drawing.Drawing2D;

namespace IgezziGuard;

/// <summary>A rounded search box with a magnifier, replacing a bordered native text box.</summary>
internal sealed class SearchField : Panel
{
    internal readonly TextBox Box = new() { BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
    /// <summary>The field's fill; the text box inside takes the same color.</summary>
    internal Color Fill { get; init; } = HankiTheme.Canvas;
    public SearchField(string placeholder, int width)
    {
        Box.PlaceholderText = placeholder; Box.AccessibleName = placeholder;
        Width = width; Height = 34; Cursor = Cursors.IBeam;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate(); Box.LostFocus += (_, _) => Invalidate();
        Click += (_, _) => Box.Focus();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        // Center the single-line box vertically, after the icon.
        float s = DeviceDpi / 96f;
        int top = Math.Max(0, (Height - Box.PreferredHeight) / 2);
        float icon = Math.Min(20 * s, Height * 0.4f);
        Padding = new Padding((int)(icon * 2.2f), top, (int)(10 * s), 0);
        base.OnLayout(e);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics; float s = DeviceDpi / 96f;
        if (SystemInformation.HighContrast) { base.OnPaintBackground(e); ControlPaint.DrawBorder(g, ClientRectangle, SystemColors.WindowText, ButtonBorderStyle.Solid); return; }
        g.Clear(Parent?.BackColor ?? HankiTheme.Surface); g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = HankiButton.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), HankiTheme.ControlRadius * s)) {
            using var fill = new SolidBrush(Fill); g.FillPath(fill, path);
            using var pen = new Pen(Box.Focused ? HankiTheme.Accent : HankiTheme.Border, Box.Focused ? 1.5f * s : 1); g.DrawPath(pen, path);
        }
        float icon = Math.Min(20 * s, Height * 0.4f);
        ToolIcon.Draw(g, new RectangleF(icon * 0.7f, (Height - icon) / 2, icon, icon), "Search", HankiTheme.Muted);
    }
}

/// <summary>A one-pixel divider that becomes a moving accent line while work is running.</summary>
internal sealed class ProgressLine : Control
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    private float phase;
    private bool busy;
    public ProgressLine()
    {
        Height = 2; AccessibleRole = AccessibleRole.ProgressBar; AccessibleName = "Progress";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        timer.Tick += (_, _) => { phase = (phase + 0.012f) % 1.4f; Invalidate(); };
    }
    public bool Busy
    {
        get => busy;
        set { busy = value; AccessibleDescription = value ? "Working" : ""; if (value && !SystemInformation.HighContrast) timer.Start(); else timer.Stop(); phase = 0; Invalidate(); }
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; bool hc = SystemInformation.HighContrast;
        g.Clear(Parent?.BackColor ?? HankiTheme.Surface);
        using (var line = new SolidBrush(hc ? SystemColors.ControlText : HankiTheme.Hairline)) g.FillRectangle(line, 0, Height - 1, Width, 1);
        if (!busy || hc) return;
        // A segment a third of the width sweeping left to right.
        float width = Width / 3f, x = (phase - 0.4f) * Width;
        using var accent = new SolidBrush(HankiTheme.Accent);
        g.FillRectangle(accent, x, 0, width, Height);
    }
}
