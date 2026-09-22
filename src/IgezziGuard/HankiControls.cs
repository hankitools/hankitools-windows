using System.Drawing.Drawing2D;

namespace IgezziGuard;

public enum HankiButtonStyle { Secondary, Quiet, Navigation }

public sealed class HankiButton : Button
{
    public string? IconKind { get; set; }
    private bool primary;
    private HankiButtonStyle appearance;
    public bool Primary { get => primary; set { primary = value; UpdateSize(); Invalidate(); } }
    public HankiButtonStyle Appearance { get => appearance; set { appearance = value; UpdateSize(); Invalidate(); } }
    private void UpdateSize() {
        int height = Primary ? 44 : Appearance == HankiButtonStyle.Quiet ? 30 : 38;
        MinimumSize = new Size(0, (int)(height * DeviceDpi / 96f));
        Padding = Primary ? new Padding(16, 8, 16, 8) : Appearance == HankiButtonStyle.Quiet ? new Padding(8, 3, 8, 3) : new Padding(12, 5, 12, 5);
    }
    private bool selected, hover, pressed;
    public bool Selected { get => selected; set { selected = value; AccessibleDescription = value ? "Current view" : ""; Invalidate(); } }
    public HankiButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Padding = new Padding(12, 5, 12, 5); MinimumSize = new Size(0, 38);
        Cursor = Cursors.Hand; UseVisualStyleBackColor = false;
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
    protected override void OnPaint(PaintEventArgs e)
    {
        if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
        float scale = DeviceDpi / 96f;
        var rect = new RectangleF(1, 1, Width - 3, Height - 3);
        e.Graphics.Clear(Parent?.BackColor ?? HankiTheme.Canvas); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        bool navigation = Appearance == HankiButtonStyle.Navigation;
        bool quiet = Appearance == HankiButtonStyle.Quiet;
        var surface = Parent?.BackColor ?? HankiTheme.Canvas;
        var bg = !Enabled ? surface : pressed ? HankiTheme.Raised : Primary ? HankiTheme.PrimaryFill : hover || Selected ? HankiTheme.Raised : quiet || navigation ? surface : HankiTheme.Surface;
        var fg = !Enabled ? HankiTheme.Muted : Primary && !pressed ? Color.White : Selected ? HankiTheme.Accent : quiet && !hover ? HankiTheme.Muted : HankiTheme.Text;
        using var path = Rounded(rect, 8 * scale); using var brush = new SolidBrush(bg); e.Graphics.FillPath(brush, path);
        if (Focused || (!quiet && !navigation && !Primary)) {
            using var pen = new Pen(Focused ? HankiTheme.Accent : HankiTheme.Border, Focused ? 2 : 1); e.Graphics.DrawPath(pen, path);
        }
        if (Selected && !Primary) {
            using var marker = new SolidBrush(HankiTheme.Accent);
            e.Graphics.FillRectangle(marker, 2 * scale, 10 * scale, 3 * scale, Math.Max(1, Height - 20 * scale));
        }
        var textBounds = Rectangle.Inflate(ClientRectangle, -(int)(12 * scale), -3);
        if (IconKind is not null) {
            ToolIcon.Draw(e.Graphics, new RectangleF(12 * scale, (Height - 18 * scale) / 2, 18 * scale, 18 * scale), IconKind, fg);
            textBounds.X += (int)(28 * scale); textBounds.Width -= (int)(28 * scale);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, fg,
            (navigation || quiet ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -6, -6), fg, bg);
    }
    internal static GraphicsPath Rounded(RectangleF r, float radius) {
        var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) return path;
        path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
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
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x1328 && !DesignMode) { m.Result = (IntPtr)1; return; }
        base.WndProc(ref m);
    }
    private void RebuildNavigation()
    {
        foreach (var strip in strips.Values) strip.Dispose();
        strips.Clear();
        foreach (TabPage page in TabPages) {
            var strip = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true,
                Padding = new Padding(0, 2, 0, 12), Margin = System.Windows.Forms.Padding.Empty, AccessibleName = "Workspace views" };
            foreach (TabPage destination in TabPages) {
                var target = destination;
                var button = new HankiButton { Text = ViewLabel(target.Text), AutoSize = true,
                    Appearance = HankiButtonStyle.Navigation, Margin = new Padding(0, 0, 8, 0),
                    AccessibleName = "Open " + target.Text, Tag = target };
                button.Click += (_, _) => {
                    SelectedTab = target;
                    if (strips.TryGetValue(target, out var active))
                        active.Controls.OfType<HankiButton>().FirstOrDefault(b => ReferenceEquals(b.Tag, target))?.Focus();
                };
                strip.Controls.Add(button);
            }
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
        foreach (var strip in strips.Values)
            foreach (var button in strip.Controls.OfType<HankiButton>()) button.Selected = ReferenceEquals(button.Tag, SelectedTab);
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
    public BrandHeader() { Height = 70; Width = 200; AccessibleName = "Hanki Tools"; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(DeviceDpi / 96f, DeviceDpi / 96f);
        using var tile = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Highlight : HankiTheme.PrimaryFill);
        using var shape = HankiButton.Rounded(new RectangleF(8, 16, 32, 34), 5); g.FillPath(tile, shape);
        using var white = new SolidBrush(SystemInformation.HighContrast ? SystemColors.HighlightText : Color.White);
        g.FillRectangle(white, 15, 23, 4, 20); g.FillRectangle(white, 29, 23, 4, 20);
        g.FillPolygon(white,new Point[] {new(19,32),new(29,28),new(29,33),new(19,37)});
        using var title = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(ForeColor); g.DrawString("Hanki Tools", title, text, new PointF(49, 22));
    }
}

internal sealed class Dashboard : UserControl
{
    public Dashboard(Action<string> navigate)
    {
        Dock = DockStyle.Fill; AutoScroll = true; Padding = new Padding(14);
        var heading = new Label { Text = "YOUR TOOLBOX", Font = new Font("Segoe UI", 9, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 12, 0, 8) };
        var intro = new Label { Text = "Inspect, measure and review changes with the right tool.", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 14) };
        var cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 8, 0, 0) };
        var items = new[] {
            ("Diagnose", "Follow the crash timeline", "Review nearby events, restart markers and practical explanations."),
            ("Performance", "Understand your workload", "Track CPU, memory, disk and GPU. Save comparisons and review power tuning."),
            ("Maintain", "Make room for what matters", "Find duplicates, review cleanup candidates and manage startup entries."),
            ("Connect", "Find the connection problem", "Compare DNS, trace routes, measure transfers and review reversible DNS changes."),
            ("Shield · experimental", "Review your protection", "Run Defender scans, inspect findings and enable in-app protection alerts.")
        };
        foreach (var (target, title, description) in items) {
            var card = new Panel { Width = 310, Height = 218, Padding = new Padding(18), Margin = new Padding(0, 0, 16, 16), Tag = "card" };
            card.Paint += (_, e) => {
                using var border = new Pen(SystemInformation.HighContrast ? SystemColors.ControlText : HankiTheme.Border);
                e.Graphics.DrawRectangle(border, 0, 0, Math.Max(1, card.Width - 1), Math.Max(1, card.Height - 1));
            };
            var label = new Label { Text = target.Split(' ')[0], Font = new Font("Segoe UI", 12, FontStyle.Bold), Dock = DockStyle.Top, Height = 32 };
            var body = new Label { Text = description, Dock = DockStyle.Fill };
            var open = new HankiButton { Text = "Open " + target.Split(' ')[0] + "  →", Dock = DockStyle.Bottom, Height = 34, Appearance = HankiButtonStyle.Quiet };
            open.Click += (_, _) => navigate(target);
            card.Controls.Add(body); card.Controls.Add(label); card.Controls.Add(new ToolIcon { Kind = target }); card.Controls.Add(open); cards.Controls.Add(card);
        }
        void FitCards() {
            int width = Math.Max(260, cards.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            int columns = Math.Clamp(width / 250, 1, 3);
            foreach (Control card in cards.Controls) card.Width = Math.Max(220, width / columns - 16);
        }
        cards.SizeChanged += (_, _) => FitCards();
        var utilities = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        utilities.Controls.Add(new Label { Text = "ALSO AVAILABLE", AutoSize = true, Margin = new Padding(0, 10, 12, 0), Font = new Font("Segoe UI", 9) });
        foreach (var name in new[] { "Assistant", "Recovery", "Scan history" }) {
            var link = new HankiButton { Text = name + "  →", AutoSize = true, Appearance = HankiButtonStyle.Quiet };
            link.Click += (_, _) => navigate(name); utilities.Controls.Add(link);
        }
        Controls.Add(cards); Controls.Add(utilities); Controls.Add(intro); Controls.Add(heading);
        FitCards();
    }
}
