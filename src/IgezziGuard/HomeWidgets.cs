using System.Drawing.Drawing2D;
namespace IgezziGuard;

/// <summary>A "Your PC at a glance" tile: icon, what it is, the value, a line of context and, for usage, a thin bar.</summary>
internal sealed class GlanceTile : Control
{
    private const int BaseHeight = 124;
    private GlanceTileModel model;
    private readonly Font labelFont = new("Segoe UI", 9.5f), valueFont = new("Segoe UI Semibold", 13f), detailFont = new("Segoe UI", 9.25f);
    private bool hover;
    internal event Action<string>? Opened;
    public GlanceTile(GlanceTileModel model)
    {
        this.model = model; TabStop = true; Cursor = Cursors.Hand; Height = BaseHeight; AccessibleRole = AccessibleRole.PushButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Click += (_, _) => Opened?.Invoke(this.model.Target);
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) { Opened?.Invoke(this.model.Target); e.Handled = true; } };
        Set(model);
    }
    internal static int HeightAtDpi(int dpi) => (int)(BaseHeight * dpi / 96f);
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Height = HeightAtDpi(DeviceDpi); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Height = HeightAtDpi(DeviceDpi); }
    internal void Set(GlanceTileModel value)
    {
        model = value; Text = value.Label; AccessibleName = Localizer.T(value.Label);
        AccessibleDescription = Localizer.T(value.Value) + (value.Detail.Length > 0 ? ". " + Localizer.T(value.Detail) : "") + (SummaryView.StatusText(value.Status) is { Length: > 0 } status ? ". " + Localizer.T(status) : "");
        Invalidate();
    }
    protected override void Dispose(bool disposing) { if (disposing) { labelFont.Dispose(); valueFont.Dispose(); detailFont.Dispose(); } base.Dispose(disposing); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; float s = DeviceDpi / 96f; bool hc = SystemInformation.HighContrast;
        using (var back = new SolidBrush(Parent?.BackColor ?? HankiTheme.Canvas)) g.FillRectangle(back, ClientRectangle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var text = hc ? SystemColors.ControlText : HankiTheme.Text; var muted = hc ? SystemColors.ControlText : HankiTheme.Muted;
        var color = hc ? SystemColors.ControlText : model.Status is CardStatus.Info or CardStatus.Unknown ? HankiTheme.Accent : HankiTheme.StatusColor(model.Status);
        using (var path = HankiButton.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), HankiTheme.CardRadius * s)) {
            using var fill = new SolidBrush(hc ? SystemColors.Control : hover ? HankiTheme.Raised : HankiTheme.Surface); g.FillPath(fill, path);
            bool ring = Focused && ShowFocusCues;
            using var pen = new Pen(hc ? SystemColors.ControlText : ring ? HankiTheme.Accent : HankiTheme.Hairline, ring ? 2 * s : 1); g.DrawPath(pen, path);
        }
        int pad = (int)(16 * s);
        var tile = new RectangleF(pad, pad, 30 * s, 30 * s);
        using (var tilePath = HankiButton.Rounded(tile, 8 * s)) { using var tint = new SolidBrush(Color.FromArgb(hc ? 0 : 30, color)); g.FillPath(tint, tilePath); }
        ToolIcon.Draw(g, RectangleF.Inflate(tile, -7 * s, -7 * s), model.Icon, color);
        const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        int textLeft = pad + (int)(40 * s);
        TextRenderer.DrawText(g, Localizer.T(model.Label), labelFont, new Rectangle(textLeft, pad, Width - textLeft - pad, (int)(30 * s)), muted, line);
        TextRenderer.DrawText(g, Localizer.T(model.Value), valueFont, new Rectangle(pad, pad + (int)(38 * s), Width - pad * 2, (int)(28 * s)), text, line);
        TextRenderer.DrawText(g, Localizer.T(model.Detail), detailFont, new Rectangle(pad, pad + (int)(66 * s), Width - pad * 2, (int)(20 * s)), muted, line);
        if (model.Percent is { } percent && !hc) {
            float y = Height - pad * 0.75f, width = Width - pad * 2;
            using var track = new SolidBrush(HankiTheme.Raised); g.FillRectangle(track, pad, y, width, 3 * s);
            using var bar = new SolidBrush(model.Status is CardStatus.Good or CardStatus.Info ? HankiTheme.Accent : color);
            g.FillRectangle(bar, pad, y, width * Math.Clamp(percent, 0, 100) / 100f, 3 * s);
        }
    }
}
