using System.Drawing.Drawing2D;

namespace IgezziGuard;

public enum HankiButtonStyle { Secondary, Quiet, Navigation, Tab, Field, Icon }

public sealed class HankiButton : Button
{
    public string? IconKind { get; set; }
    /// <summary>Muted right-aligned hint, such as a keyboard shortcut.</summary>
    public string? Hint { get; set; }
    /// <summary>Selection color for navigation buttons of a product area; defaults to the Hanki accent.</summary>
    public Color? AreaAccent { get; set; }
    private bool primary;
    private HankiButtonStyle appearance;
    public bool Primary { get => primary; set { primary = value; UpdateSize(); Invalidate(); } }
    public HankiButtonStyle Appearance { get => appearance; set { appearance = value; UpdateSize(); Invalidate(); } }
    private void UpdateSize() {
        if (Appearance == HankiButtonStyle.Icon) { int side = (int)(36 * DeviceDpi / 96f); MinimumSize = new Size(side, side); Padding = System.Windows.Forms.Padding.Empty; return; }
        int height = Appearance is HankiButtonStyle.Quiet or HankiButtonStyle.Navigation or HankiButtonStyle.Tab ? 34 : 38;
        MinimumSize = new Size(0, (int)(height * DeviceDpi / 96f));
        Padding = Primary ? new Padding(18, 6, 18, 6) : Appearance == HankiButtonStyle.Quiet ? new Padding(8, 3, 8, 3) : new Padding(14, 5, 14, 5);
    }
    private bool selected, hover, pressed, wanted = true, folded;
    public bool Selected { get => selected; set { selected = value; AccessibleDescription = value ? Localizer.T("Current view") : ""; Invalidate(); } }
    /// <summary>Whether the page wants this button shown (its own Visible setting), apart from folding.</summary>
    internal bool Wanted => wanted;
    /// <summary>Moved into a row's "More" menu because the row is full; the button keeps its place and state.</summary>
    internal bool Folded { get => folded; set { if (folded == value) return; folded = value; base.SetVisibleCore(wanted && !folded); } }
    protected override void SetVisibleCore(bool value) { wanted = value; base.SetVisibleCore(value && !folded); }
    /// <summary>Runs the button's action from a menu, even while it's folded (PerformClick needs a visible button).</summary>
    internal void Invoke() { if (Enabled) OnClick(EventArgs.Empty); }
    public HankiButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Padding = new Padding(14, 5, 14, 5); MinimumSize = new Size(0, 38);
        Cursor = Cursors.Hand; UseVisualStyleBackColor = false;
        // Text is drawn literally ("Memory & pagefile"), so it is measured literally too.
        UseMnemonic = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateSize(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (Appearance == HankiButtonStyle.Icon) return MinimumSize;
        var size = base.GetPreferredSize(proposedSize);
        float scale = DeviceDpi / 96f;
        if (IconKind is not null) size.Width += (int)(26 * scale);
        if (Hint is not null) size.Width += TextRenderer.MeasureText(Hint, Font).Width + (int)(12 * scale);
        return size;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
        float scale = DeviceDpi / 96f;
        var g = e.Graphics;
        var surface = Parent?.BackColor ?? HankiTheme.Canvas;
        g.Clear(surface); g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var style = Appearance;
        bool active = hover || pressed;
        Color bg, fg, icon;
        Color? border = null;
        if (!Enabled) {
            bg = Primary || style is HankiButtonStyle.Secondary or HankiButtonStyle.Field ? HankiTheme.Surface : surface; fg = icon = Color.FromArgb(110, 120, 134);
            if (style is HankiButtonStyle.Secondary or HankiButtonStyle.Field && !Primary) border = HankiTheme.Border;
        } else if (Primary) {
            bg = hover && !pressed ? HankiTheme.PrimaryHover : HankiTheme.PrimaryFill; fg = icon = Color.White;
        } else switch (style) {
            case HankiButtonStyle.Navigation:
                bg = Selected || pressed ? HankiTheme.Raised : hover ? HankiTheme.Surface : surface;
                fg = Selected || active ? HankiTheme.Text : HankiTheme.Muted; icon = Selected ? AreaAccent ?? HankiTheme.Accent : fg; break;
            case HankiButtonStyle.Tab:
                bg = Selected ? HankiTheme.Raised : hover ? HankiTheme.Surface : surface; fg = icon = Selected || active ? HankiTheme.Text : HankiTheme.Muted; break;
            case HankiButtonStyle.Quiet:
                bg = active ? HankiTheme.Raised : surface; fg = icon = active ? HankiTheme.Text : HankiTheme.Accent; break;
            case HankiButtonStyle.Field:
                bg = HankiTheme.Surface; fg = icon = HankiTheme.Muted; border = active ? HankiTheme.Muted : HankiTheme.Border; break;
            case HankiButtonStyle.Icon:
                bg = active ? HankiTheme.Raised : surface; fg = icon = active ? HankiTheme.Text : HankiTheme.Muted; break;
            default:
                bg = pressed ? HankiTheme.Surface : hover ? HankiTheme.Raised : HankiTheme.Surface; fg = icon = HankiTheme.Text; border = hover ? HankiTheme.Muted : HankiTheme.Border; break;
        }
        float radius = HankiTheme.ControlRadius * scale;
        using (var path = Rounded(rect, radius)) {
            { using var brush = new SolidBrush(bg); g.FillPath(brush, path); }
            bool focusRing = Focused && ShowFocusCues;
            if (focusRing || border is not null) {
                using var pen = new Pen(focusRing ? HankiTheme.Accent : border!.Value, focusRing ? 2 * scale : 1); g.DrawPath(pen, path);
            }
        }
        if (style == HankiButtonStyle.Navigation && Selected && !Primary) {
            using var marker = new SolidBrush(AreaAccent ?? HankiTheme.Accent);
            float markerHeight = Math.Min(18 * scale, Height - 12 * scale);
            using var pill = Rounded(new RectangleF(2 * scale, (Height - markerHeight) / 2, 3 * scale, markerHeight), 1.5f * scale); g.FillPath(marker, pill);
        }
        bool leading = style is HankiButtonStyle.Navigation or HankiButtonStyle.Quiet or HankiButtonStyle.Field;
        var textBounds = Rectangle.Inflate(ClientRectangle, -(int)((leading ? 12 : 10) * scale), 0);
        if (IconKind is not null) {
            float size = 18 * scale;
            ToolIcon.Draw(g, new RectangleF(textBounds.X, (Height - size) / 2, size, size), IconKind, icon);
            textBounds.X += (int)(28 * scale); textBounds.Width -= (int)(28 * scale);
        }
        if (Hint is not null) {
            var hintSize = TextRenderer.MeasureText(Hint, Font);
            TextRenderer.DrawText(g, Hint, Font, new Rectangle(textBounds.Right - hintSize.Width, 0, hintSize.Width, Height), HankiTheme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            textBounds.Width -= hintSize.Width + (int)(8 * scale);
        }
        TextRenderer.DrawText(g, Text, Font, textBounds, fg,
            (leading ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
    internal static GraphicsPath Rounded(RectangleF r, float radius) {
        var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
}

/// <summary>Rounded surface. Tagged "card" so the theme gives direct children the surface color.</summary>
internal class RoundedPanel : Panel
{
    public RoundedPanel() { Tag = "card"; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (SystemInformation.HighContrast) { base.OnPaintBackground(e); ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.ControlText, ButtonBorderStyle.Solid); return; }
        float scale = DeviceDpi / 96f;
        e.Graphics.Clear(Parent?.BackColor ?? HankiTheme.Canvas); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = HankiButton.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), HankiTheme.CardRadius * scale);
        using var fill = new SolidBrush(HankiTheme.Surface); e.Graphics.FillPath(fill, path);
        using var border = new Pen(HankiTheme.Hairline); e.Graphics.DrawPath(border, path);
    }
}

/// <summary>Colored status dots with counts. Colors mirror evidence states, not a health score.</summary>
internal static class StatusChips
{
    internal static IReadOnlyList<(Color Color, string Text)> Count(IEnumerable<DiagnosticResult> results)
    {
        var list = results.ToArray();
        int Of(Func<DiagnosticResult, bool> match) => list.Count(match);
        bool Collected(DiagnosticResult r) => r.Outcome is CollectionOutcome.Completed or CollectionOutcome.Partial;
        var chips = new List<(Color, string)>();
        void Add(int count, FindingSeverity severity, string label) { if (count > 0) chips.Add((HankiTheme.SeverityColor(severity, CollectionOutcome.Completed), $"{count} {label}")); }
        Add(Of(r => Collected(r) && r.Severity == FindingSeverity.Critical), FindingSeverity.Critical, "critical");
        Add(Of(r => Collected(r) && r.Severity == FindingSeverity.Warning), FindingSeverity.Warning, "to review");
        Add(Of(r => Collected(r) && r.Severity == FindingSeverity.Healthy), FindingSeverity.Healthy, "OK");
        Add(Of(r => Collected(r) && r.Severity == FindingSeverity.Informational), FindingSeverity.Informational, "info");
        int unknown = Of(r => !Collected(r) || r.Severity == FindingSeverity.Unknown);
        if (unknown > 0) chips.Add((HankiTheme.Muted, $"{unknown} not checked"));
        return chips;
    }
    internal static void Draw(Graphics g, Rectangle bounds, IEnumerable<(Color Color, string Text)> chips, Font font, float scale)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int x = bounds.X;
        foreach (var (color, text) in chips) {
            var size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
            float dot = 8 * scale;
            if (x + dot + 6 * scale + size.Width > bounds.Right) break;
            using (var brush = new SolidBrush(color)) g.FillEllipse(brush, x, bounds.Y + (bounds.Height - dot) / 2, dot, dot);
            x += (int)(dot + 6 * scale);
            TextRenderer.DrawText(g, text, font, new Rectangle(x, bounds.Y, size.Width + 2, bounds.Height), HankiTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            x += size.Width + (int)(16 * scale);
        }
    }
}

public sealed class HankiTabs : TabControl
{
    private readonly Dictionary<TabPage, FlowLayoutPanel> strips = new();
    public HankiTabs()
    {
        // The native header is fully covered by pages; each page owns an ordinary themed navigation row.
        Multiline = true; SizeMode = TabSizeMode.Fixed; ItemSize = new Size(1, 1);
        Appearance = TabAppearance.FlatButtons; TabStop = false;
        ControlAdded += (_, _) => RebuildNavigation();
        ControlRemoved += (_, _) => { if (!Disposing && !IsDisposed) RebuildNavigation(); };
        SelectedIndexChanged += (_, _) => UpdateSelection();
        // A selection made before the handle exists is applied after HandleCreated is raised.
        HandleCreated += (_, _) => BeginInvoke(UpdateSelection);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x1328 && !DesignMode) { m.Result = (IntPtr)1; return; }
        base.WndProc(ref m);
    }
    private readonly List<HankiButton> overflows = new();
    private void RebuildNavigation()
    {
        foreach (var strip in strips.Values) strip.Dispose();
        strips.Clear(); overflows.Clear();
        foreach (TabPage page in TabPages) {
            // One row of pill-shaped views; the ones that don't fit go into "More", and the current one always shows.
            var strip = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false,
                Padding = new Padding(0, 0, 0, 16), Margin = System.Windows.Forms.Padding.Empty, AccessibleName = Localizer.T("Workspace views") };
            foreach (TabPage destination in TabPages) {
                var target = destination;
                var button = new HankiButton { Text = Localizer.T(ViewLabel(target.Text)), AutoSize = true,
                    Appearance = HankiButtonStyle.Tab, Margin = new Padding(0, 0, 4, 0), Font = new Font("Segoe UI Semibold", 10.5f),
                    AccessibleName = Localizer.Format("Open {0}", Localizer.T(ViewLabel(target.Text))), Tag = target };
                button.Click += (_, _) => {
                    SelectedTab = target;
                    if (strips.TryGetValue(target, out var active))
                        active.Controls.OfType<HankiButton>().FirstOrDefault(b => ReferenceEquals(b.Tag, target))?.Focus();
                };
                strip.Controls.Add(button);
            }
            overflows.Add(Overflow.Attach(strip, b => b.Tag is TabPage, b => b.Selected, "More views", font: new Font("Segoe UI Semibold", 10.5f)));
            page.Controls.Add(strip); strips.Add(page, strip); HankiTheme.Apply(strip);
        }
        UpdateSelection();
    }
    private static string ViewLabel(string title) => title switch {
        "Snapshot / pagefile" => "Overview",
        "30-second sample" => "Quick sample",
        "Long monitoring / saved runs" => "Monitoring",
        "Files & storage" => "Files",
        "Apps & storage" => "Apps",
        "Usage review" => "App usage",
        "Startup / undo" => "Startup entries",
        "Recent Event Logs" => "Event logs",
        "Advanced / DNS repair" => "Network tools",
        "Defender controls / alerts" => "Scans & alerts",
        "Prepare / redact" => "Prepare report",
        "In-app AI (optional)" => "AI chat",
        _ => title
    };
    private void UpdateSelection()
    {
        // Before the handle exists SelectedTab can be null although the first page is shown.
        var current = SelectedTab ?? (TabCount > 0 ? TabPages[0] : null);
        foreach (var strip in strips.Values)
            foreach (var button in strip.Controls.OfType<HankiButton>()) button.Selected = ReferenceEquals(button.Tag, current);
        foreach (var more in overflows) Overflow.Refit(more);
    }
}

internal sealed class WorkspacePages : TabControl
{
    public WorkspacePages() { Multiline = true; SizeMode = TabSizeMode.Fixed; ItemSize = new Size(1, 1); Appearance = TabAppearance.FlatButtons; TabStop = false; }
    protected override void WndProc(ref Message m) {
        if (m.Msg == 0x1328 && !DesignMode) { m.Result = (IntPtr)1; return; } // TCM_ADJUSTRECT: sidebar replaces the tab strip.
        base.WndProc(ref m);
    }
}

internal sealed class BrandHeader : Control
{
    private readonly Image brandImage = LoadBrandImage();
    private static Image LoadBrandImage()
    {
        using var stream = typeof(BrandHeader).Assembly.GetManifestResourceStream("IgezziGuard.Brand.hanki-smile.png")
            ?? throw new InvalidOperationException("The Hanki brand image is missing.");
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }
    private readonly ToolTip meaning = new();
    protected override void Dispose(bool disposing)
    {
        if (disposing) { brandImage.Dispose(); meaning.Dispose(); }
        base.Dispose(disposing);
    }
    public BrandHeader()
    {
        Height = 82; Width = 204; AccessibleName = "Hanki Tools"; AccessibleDescription = AppInfo.Tagline;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        meaning.SetToolTip(this, AppInfo.TaglineMeaning);
    }
    // Painting is DPI-scaled; keep the tagline row inside the control at higher scaling.
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Height = (int)Math.Ceiling(82 * DeviceDpi / 96f); }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        float s = DeviceDpi / 96f;
        // App-icon tile: the mark's dark backdrop is clipped to a rounded square.
        var state = g.Save(); g.ScaleTransform(s, s);
        using (var tile = HankiButton.Rounded(new RectangleF(10, 14, 36, 36), 9)) {
            var clip = g.Save(); g.SetClip(tile); g.DrawImage(brandImage, new Rectangle(10, 14, 36, 36)); g.Restore(clip);
            if (!SystemInformation.HighContrast) { using var edge = new Pen(HankiTheme.Border); g.DrawPath(edge, tile); }
        }
        g.Restore(state);
        // TextRenderer ignores the scale transform, so text geometry is scaled explicitly.
        Rectangle At(float x, float y, float w, float h) => Rectangle.Round(new RectangleF(x * s, y * s, w * s, h * s));
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        using var title = new Font("Segoe UI Semibold", 17 * s, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, "Hanki Tools", title, At(56, 14, 148, 36), ForeColor, flags);
        // Brand line, as on hanki.tools: "+ A LITTLE SISU FOR YOUR PC".
        using var tagline = new Font("Segoe UI", 10.5f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        var accent = SystemInformation.HighContrast ? ForeColor : HankiTheme.Accent;
        TextRenderer.DrawText(g, "+", tagline, At(11, 58, 12, 16), accent, flags);
        TextRenderer.DrawText(g, AppInfo.Tagline.ToUpperInvariant(), tagline, At(24, 58, 180, 16), accent, flags | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Keyboard-accessible tool card: the whole surface opens its module.</summary>
internal sealed class HankiCard : Control
{
    private readonly string kind, title, description;
    private readonly Color accent;
    private readonly Font titleFont = new("Segoe UI Semibold", 13f), bodyFont = new("Segoe UI", 10.5f);
    private bool hover;
    public HankiCard(string kind, string title, string description, Action open, Color? accent = null)
    {
        title = Localizer.T(title); description = Localizer.T(description);
        this.kind = kind; this.title = title; this.description = description; this.accent = accent ?? HankiTheme.Accent;
        Text = title; AccessibleName = title; AccessibleDescription = description; AccessibleRole = AccessibleRole.PushButton;
        TabStop = true; Cursor = Cursors.Hand; Height = 148;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Click += (_, _) => open();
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) { open(); e.Handled = true; } };
    }
    protected override void Dispose(bool disposing) { if (disposing) { titleFont.Dispose(); bodyFont.Dispose(); } base.Dispose(disposing); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; float s = DeviceDpi / 96f;
        bool hc = SystemInformation.HighContrast;
        g.Clear(Parent?.BackColor ?? HankiTheme.Canvas); g.SmoothingMode = SmoothingMode.AntiAlias;
        var text = hc ? SystemColors.ControlText : HankiTheme.Text; var muted = hc ? SystemColors.ControlText : HankiTheme.Muted;
        using (var path = HankiButton.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), HankiTheme.CardRadius * s)) {
            using var fill = new SolidBrush(hc ? SystemColors.Control : hover ? HankiTheme.Raised : HankiTheme.Surface); g.FillPath(fill, path);
            bool ring = Focused && ShowFocusCues;
            using var pen = new Pen(hc ? SystemColors.ControlText : ring ? accent : hover ? Color.FromArgb(62, 70, 84) : HankiTheme.Hairline, ring ? 2 * s : 1);
            g.DrawPath(pen, path);
        }
        int pad = (int)(18 * s);
        var tileRect = new RectangleF(pad, pad, 40 * s, 40 * s);
        using (var tile = HankiButton.Rounded(tileRect, 8 * s)) { using var tileFill = new SolidBrush(hc ? SystemColors.Control : Color.FromArgb(28, accent)); g.FillPath(tileFill, tile); }
        ToolIcon.Draw(g, RectangleF.Inflate(tileRect, -10 * s, -10 * s), kind, hc ? SystemColors.ControlText : accent);
        int textLeft = pad + (int)(54 * s);
        TextRenderer.DrawText(g, title, titleFont, new Rectangle(textLeft, pad, Width - textLeft - pad - (int)(20 * s), (int)(40 * s)), text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "→", titleFont, new Rectangle(Width - pad - (int)(20 * s), pad, (int)(20 * s), (int)(40 * s)), hover || Focused ? accent : muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, description, bodyFont, new Rectangle(pad, pad + (int)(52 * s), Width - pad * 2, Height - pad * 2 - (int)(52 * s)), muted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Latest local Full System Scan, summarised from saved history. Absence of results is not a health verdict.</summary>
internal sealed class LastScanView : Control
{
    private DiagnosticScan? scan;
    private string? problem;
    private readonly Font eyebrow = new("Segoe UI", 8.25f, FontStyle.Bold), large = new("Segoe UI Semibold", 13f), body = new("Segoe UI", 9.75f);
    public LastScanView() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); AccessibleRole = AccessibleRole.StaticText; }
    protected override void Dispose(bool disposing) { if (disposing) { eyebrow.Dispose(); large.Dispose(); body.Dispose(); } base.Dispose(disposing); }
    public void Show(DiagnosticScan? latest, string? error = null)
    {
        scan = latest; problem = error;
        AccessibleName = latest is not null ? $"Last full scan {latest.Ended.ToLocalTime():g}: " + string.Join(", ", StatusChips.Count(latest.Results).Select(c => c.Text))
            : error is null ? "No saved full scan yet" : "Scan history unavailable: " + error;
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; float s = DeviceDpi / 96f;
        g.Clear(Parent?.BackColor ?? HankiTheme.Surface);
        bool hc = SystemInformation.HighContrast;
        var text = hc ? SystemColors.ControlText : HankiTheme.Text; var muted = hc ? SystemColors.ControlText : HankiTheme.Muted;
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, Localizer.T("LAST SCAN"), eyebrow, new Rectangle(0, 0, Width, (int)(18 * s)), muted, flags);
        int y = (int)(24 * s);
        if (scan is null) {
            TextRenderer.DrawText(g, Localizer.T(problem is null ? "No scans yet" : "History unavailable"), large, new Rectangle(0, y, Width, (int)(28 * s)), text, flags);
            TextRenderer.DrawText(g, problem ?? Localizer.T("Your results will appear here after the first scan."), body, new Rectangle(0, y + (int)(32 * s), Width, (int)(44 * s)), muted, flags | TextFormatFlags.WordBreak);
            return;
        }
        var ended = scan.Ended.ToLocalTime();
        string when = ended.Date == DateTime.Today ? "Today, " + ended.ToString("t") : ended.Date == DateTime.Today.AddDays(-1) ? "Yesterday, " + ended.ToString("t") : ended.ToString("g");
        TextRenderer.DrawText(g, when, large, new Rectangle(0, y, Width, (int)(28 * s)), text, flags);
        string coverage = scan.Cancelled ? "Cancelled · " : "";
        TextRenderer.DrawText(g, coverage + $"{scan.CompletedModules} of {scan.PlannedModules} checks finished", body, new Rectangle(0, y + (int)(30 * s), Width, (int)(20 * s)), muted, flags);
        StatusChips.Draw(g, new Rectangle(0, y + (int)(56 * s), Width, (int)(22 * s)), StatusChips.Count(scan.Results), body, s);
    }
}

internal sealed class Dashboard : UserControl
{
    private readonly LastScanView lastScan = new() { Dock = DockStyle.Fill };
    private readonly DiagnosticHistory history = new(Path.Combine(SecurityPaths.Root, "diagnostic-history.json"));
    public Dashboard(Action<string> navigate, Action startScan)
    {
        Dock = DockStyle.Fill; AutoScroll = true; Padding = new Padding(0, 4, 8, 16);

        var hero = new RoundedPanel { Dock = DockStyle.Top, Height = 200, Padding = new Padding(26, 22, 26, 20) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Tag = "card" };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var pitch = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Tag = "card", Margin = Padding.Empty };
        var eyebrow = new Label { Text = Localizer.T("Fix my PC").ToUpper(System.Globalization.CultureInfo.CurrentUICulture), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Tag = "accent", Margin = new Padding(0, 0, 0, 6) };
        var headline = new Label { Text = Localizer.T("Check your PC in one pass"), AutoSize = true, Font = new Font("Segoe UI Semibold", 20f), Margin = new Padding(0, 0, 0, 6) };
        var pitchText = new Label { Text = Localizer.T("Read-only. Nothing changes until you approve a repair, and nothing is uploaded."), AutoSize = true, Tag = "intro", Font = new Font("Segoe UI", 11f), Margin = new Padding(0, 0, 0, 14) };
        var actions = new FlowLayoutPanel { AutoSize = true, Tag = "card", Margin = Padding.Empty, WrapContents = false };
        var start = new HankiButton { Text = Localizer.T("Scan my PC"), Primary = true, AutoSize = true, Margin = new Padding(0, 0, 8, 0), Font = new Font("Segoe UI Semibold", 11f) };
        var results = new HankiButton { Text = Localizer.T("View results"), AutoSize = true, Margin = Padding.Empty };
        start.Click += (_, _) => startScan(); results.Click += (_, _) => navigate("Fix My PC");
        actions.Controls.AddRange([start, results]);
        pitch.Controls.AddRange([eyebrow, headline, pitchText, actions]);
        pitch.SizeChanged += (_, _) => pitchText.MaximumSize = new Size(Math.Max(200, pitch.ClientSize.Width - 12), 0);
        var summary = new Panel { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(20, 4, 0, 0), Margin = Padding.Empty };
        summary.Paint += (_, e) => {
            if (SystemInformation.HighContrast) return;
            using var line = new Pen(HankiTheme.Border); e.Graphics.DrawLine(line, 0, 4, 0, summary.Height - 4);
        };
        summary.Controls.Add(lastScan);
        layout.Controls.Add(pitch, 0, 0); layout.Controls.Add(summary, 1, 0);
        hero.Controls.Add(layout);

        // Docked top in reverse: the tiles are added first, the hero last so it sits highest.
        ToolTiles.Add(this, "More tools", ToolTiles.For(ProductArea.System, navigate), HankiTheme.Accent);
        Controls.Add(hero);
        ToolTiles.TopDown(this);
        VisibleChanged += (_, _) => { if (Visible) RefreshLastScan(); };
        RefreshLastScan();
    }
    internal void RefreshLastScan()
    {
        try { lastScan.Show(history.Read().OrderByDescending(s => s.Ended).FirstOrDefault()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { lastScan.Show(null, "Saved scan history could not be read. Original files were preserved."); }
    }
}

/// <summary>A large selectable option (title and one line); tiles sharing a parent behave as one radio group.</summary>
internal sealed class ChoiceTile : Control
{
    private readonly string title, description;
    private readonly Color accent;
    private readonly Font titleFont, bodyFont;
    private bool hover, selected;
    public event Action? Chosen;
    public object? Value { get; init; }
    public bool Selected
    {
        get => selected;
        set { selected = value; AccessibleDescription = (value ? "Selected. " : "") + description; Invalidate(); }
    }
    public ChoiceTile(string title, string description, Color? accent = null, bool compact = false)
    {
        title = Localizer.T(title); description = Localizer.T(description);
        this.title = title; this.description = description; this.accent = accent ?? HankiTheme.Accent;
        titleFont = new Font("Segoe UI Semibold", compact ? 11f : 13f); bodyFont = new Font("Segoe UI", 10f);
        Text = title; AccessibleName = title; AccessibleDescription = description; AccessibleRole = AccessibleRole.RadioButton;
        TabStop = true; Cursor = Cursors.Hand; Height = compact ? 48 : 112; Width = compact ? 150 : 236;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Click += (_, _) => Choose();
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) { Choose(); e.Handled = true; } };
    }
    /// <summary>The height a large tile of this width needs to show its whole description.</summary>
    internal int HeightFor(int width)
    {
        float s = DeviceDpi / 96f; int pad = (int)(14 * s);
        var body = TextRenderer.MeasureText(description, bodyFont, new Size(Math.Max(1, width - pad * 2), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return Math.Max((int)(100 * s), body.Height + pad * 2 + (int)(34 * s));
    }
    private void Choose()
    {
        foreach (var tile in Parent?.Controls.OfType<ChoiceTile>() ?? []) tile.Selected = tile == this;
        Chosen?.Invoke();
    }
    protected override void Dispose(bool disposing) { if (disposing) { titleFont.Dispose(); bodyFont.Dispose(); } base.Dispose(disposing); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; float s = DeviceDpi / 96f;
        bool hc = SystemInformation.HighContrast;
        g.Clear(Parent?.BackColor ?? HankiTheme.Canvas); g.SmoothingMode = SmoothingMode.AntiAlias;
        var text = hc ? SystemColors.ControlText : HankiTheme.Text; var muted = hc ? SystemColors.ControlText : HankiTheme.Muted;
        using (var path = HankiButton.Rounded(new RectangleF(1, 1, Width - 2.5f, Height - 2.5f), HankiTheme.CardRadius * s)) {
            var fill = hc ? (selected ? SystemColors.Highlight : SystemColors.Control) : selected ? Blend(HankiTheme.Surface, accent, 0.16f) : hover ? HankiTheme.Raised : HankiTheme.Surface;
            using var brush = new SolidBrush(fill); g.FillPath(brush, path);
            bool ring = selected || (Focused && ShowFocusCues);
            using var pen = new Pen(hc ? SystemColors.ControlText : ring ? accent : hover ? Color.FromArgb(62, 70, 84) : HankiTheme.Hairline, ring ? 2 * s : 1);
            g.DrawPath(pen, path);
        }
        if (hc && selected) text = muted = SystemColors.HighlightText;
        int pad = (int)(14 * s), dot = (int)(16 * s);
        // Radio mark, so the selection doesn't rely on color alone.
        var mark = new RectangleF(pad, (Height - dot) / 2f, dot, dot);
        if (Height > 60 * s) mark.Y = pad + 2 * s;
        using (var ringPen = new Pen(selected ? (hc ? text : accent) : muted, 1.6f * s)) g.DrawEllipse(ringPen, mark);
        if (selected) { using var fillDot = new SolidBrush(hc ? text : accent); g.FillEllipse(fillDot, RectangleF.Inflate(mark, -4 * s, -4 * s)); }
        int left = pad + dot + (int)(10 * s);
        if (Height <= 60 * s) {
            TextRenderer.DrawText(g, title, titleFont, new Rectangle(left, 0, Width - left - pad, Height), text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            return;
        }
        TextRenderer.DrawText(g, title, titleFont, new Rectangle(left, pad - (int)(2 * s), Width - left - pad, (int)(26 * s)), text, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, description, bodyFont, new Rectangle(pad, pad + (int)(30 * s), Width - pad * 2, Height - pad * 2 - (int)(28 * s)), muted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
    private static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
}
