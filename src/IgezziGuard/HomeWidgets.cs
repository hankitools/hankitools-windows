using System.Drawing.Drawing2D;
namespace IgezziGuard;

/// <summary>
/// Home search: "What do you need help with?" Results appear as you type: guided fixes for symptoms and tool pages.
/// Enter opens the top result, ↓ moves into the list, Esc clears.
/// </summary>
internal sealed class HomeSearchBox : UserControl
{
    private readonly SearchField field = new("What do you need help with?  For example: slow, blue screen, Wi-Fi, FPS", 400) { Dock = DockStyle.Top, Height = 52, Fill = HankiTheme.Surface };
    private readonly Panel results = new() { Dock = DockStyle.Top, Visible = false, Padding = new Padding(0, 8, 0, 0) };
    // Sized by FitTry: an auto-sized flow inside an auto-sized parent is measured as one line, so a wrapped second line was cut off.
    private readonly FlowLayoutPanel tryRow = new() { Dock = DockStyle.Top, WrapContents = true, Padding = new Padding(0, 10, 0, 0) };
    private readonly Label tryLabel = new() { Text = "Try", AutoSize = true, Tag = "intro", Margin = new Padding(2, 0, 6, 0) };
    private readonly Func<IReadOnlyList<SearchEntry>> entries;
    private readonly Action<SearchEntry> open;

    public HomeSearchBox(Func<IReadOnlyList<SearchEntry>> entries, Action<SearchEntry> open)
    {
        this.entries = entries; this.open = open;
        Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(0, 0, 0, 18);
        field.Box.Font = new Font("Segoe UI", 12.5f); field.Box.AccessibleName = "Search for a problem or a tool";
        tryRow.Controls.Add(tryLabel);
        foreach (var suggestion in HomeSearch.Suggestions) {
            var chip = new HankiButton { Text = suggestion, AutoSize = true, Appearance = HankiButtonStyle.Tab, Margin = new Padding(0, 0, 4, 4), AccessibleName = "Search for " + suggestion };
            chip.Click += (_, _) => { field.Box.Text = suggestion; field.Box.Focus(); field.Box.SelectionStart = field.Box.TextLength; };
            tryRow.Controls.Add(chip);
        }
        Controls.Add(results); Controls.Add(tryRow); Controls.Add(field);
        field.Box.TextChanged += (_, _) => Show(HomeSearch.Find(field.Box.Text, this.entries()));
        field.Box.KeyDown += (_, e) => {
            var rows = results.Controls.OfType<SearchResultRow>().OrderBy(r => r.Top).ToArray();
            if (e.KeyCode == Keys.Enter && rows.Length > 0) { this.open(rows[0].Entry); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down && rows.Length > 0) { rows[0].Focus(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { field.Box.Clear(); e.SuppressKeyPress = true; }
        };
        results.SizeChanged += (_, _) => FitRows();
        tryRow.SizeChanged += (_, _) => FitTry();
    }

    /// <summary>Wraps the suggestions to the row's width, with "Try" centered on the first line.</summary>
    private void FitTry()
    {
        int available = tryRow.ClientSize.Width - tryRow.Padding.Horizontal, x = 0, line = 0, height = 0, first = 0;
        foreach (Control c in tryRow.Controls) {
            var size = c.GetPreferredSize(Size.Empty);
            int w = size.Width + c.Margin.Horizontal, h = size.Height + c.Margin.Vertical;
            if (x > 0 && x + w > available) { height += line; x = line = 0; }
            x += w; line = Math.Max(line, h);
            if (height == 0 && c is HankiButton) first = Math.Max(first, size.Height);
        }
        int top = Math.Max(0, (first - tryLabel.GetPreferredSize(Size.Empty).Height) / 2);
        if (tryLabel.Margin.Top != top) tryLabel.Margin = new Padding(tryLabel.Margin.Left, top, tryLabel.Margin.Right, 0);
        int wanted = height + line + tryRow.Padding.Vertical;
        if (tryRow.Height != wanted) tryRow.Height = wanted;
    }

    private void Show(IReadOnlyList<SearchEntry> found)
    {
        results.SuspendLayout();
        foreach (Control old in results.Controls.Cast<Control>().ToArray()) old.Dispose();
        bool query = field.Box.Text.Trim().Length > 0;
        tryRow.Visible = !query;
        if (query && found.Count == 0)
            results.Controls.Add(new Label { Text = "Nothing matched. Try a broader word, such as network, storage or games, or press Ctrl+K to list every tool.",
                Dock = DockStyle.Top, Height = 40, Tag = "intro", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) });
        // Docked to the top in reverse, so the best match is first.
        foreach (var entry in found.Reverse()) {
            var row = new SearchResultRow(entry) { Dock = DockStyle.Top };
            row.Opened += () => open(entry);
            row.KeyDown += (_, e) => {
                if (e.KeyCode is Keys.Up or Keys.Down) {
                    var rows = results.Controls.OfType<SearchResultRow>().OrderBy(r => r.Top).ToList();
                    int i = rows.IndexOf(row) + (e.KeyCode == Keys.Down ? 1 : -1);
                    if (i < 0) field.Box.Focus(); else if (i < rows.Count) rows[i].Focus();
                    e.Handled = true;
                }
            };
            results.Controls.Add(row);
        }
        results.Visible = query;
        HankiTheme.Apply(results);
        results.ResumeLayout();
        FitRows();
    }
    private void FitRows() => results.Height = results.Padding.Vertical + results.Controls.Cast<Control>().Sum(c => c.Height);
    internal void FocusSearch() => field.Box.Focus();
    /// <summary>For the UI check's screenshots: types a query as someone would.</summary>
    internal void Query(string text) => field.Box.Text = text;
}

/// <summary>One search result: title, where it leads, and whether it's a guided fix or a tool. The whole row opens it.</summary>
internal sealed class SearchResultRow : Control
{
    internal SearchEntry Entry { get; }
    internal event Action? Opened;
    private readonly Font titleFont = Dpi.PaintFont("Segoe UI Semibold", 11f), detailFont = Dpi.PaintFont("Segoe UI", 9.5f);
    private bool hover;
    public SearchResultRow(SearchEntry entry)
    {
        Entry = entry; Text = entry.Title; AccessibleName = entry.Title; AccessibleDescription = (entry.GuidedFix ? "Guided fix. " : "Tool. ") + entry.Detail;
        AccessibleRole = AccessibleRole.PushButton; TabStop = true; Cursor = Cursors.Hand; Height = 58;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Click += (_, _) => Opened?.Invoke();
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) { Opened?.Invoke(); e.Handled = true; } };
    }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void Dispose(bool disposing) { if (disposing) { titleFont.Dispose(); detailFont.Dispose(); } base.Dispose(disposing); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; float s = Dpi.Factor; bool hc = SystemInformation.HighContrast;
        using (var back = new SolidBrush(Parent?.BackColor ?? HankiTheme.Canvas)) g.FillRectangle(back, ClientRectangle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool active = hover || Focused;
        var text = hc ? SystemColors.ControlText : HankiTheme.Text; var muted = hc ? SystemColors.ControlText : HankiTheme.Muted;
        var accent = hc ? SystemColors.ControlText : Entry.GuidedFix ? HankiTheme.Accent : HankiTheme.PerformanceAccent;
        using (var path = HankiButton.Rounded(new RectangleF(0.5f, 2, Width - 1.5f, Height - 4.5f), HankiTheme.ControlRadius * s)) {
            if (active) { using var fill = new SolidBrush(hc ? SystemColors.Highlight : HankiTheme.Raised); g.FillPath(fill, path); }
            if (Focused && ShowFocusCues) { using var ring = new Pen(accent, 2 * s); g.DrawPath(ring, path); }
        }
        if (hc && active) text = muted = accent = SystemColors.HighlightText;
        var tile = new RectangleF(12 * s, (Height - 34 * s) / 2, 34 * s, 34 * s);
        using (var tilePath = HankiButton.Rounded(tile, 8 * s)) { using var tint = new SolidBrush(Color.FromArgb(hc ? 0 : 30, accent)); g.FillPath(tint, tilePath); }
        ToolIcon.Draw(g, RectangleF.Inflate(tile, -9 * s, -9 * s), Entry.GuidedFix ? "Help" : "Overview", accent);
        int left = (int)(58 * s), right = (int)(120 * s);
        TextRenderer.DrawText(g, Entry.Title, titleFont, new Rectangle(left, (int)(8 * s), Width - left - right, (int)(24 * s)), text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g, Entry.Detail, detailFont, new Rectangle(left, (int)(30 * s), Width - left - right, (int)(20 * s)), muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g, (Entry.GuidedFix ? "Guided fix" : "Open") + "  →", detailFont, new Rectangle(Width - right, 0, right - (int)(14 * s), Height), active ? accent : muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

/// <summary>A "Your PC at a glance" tile: icon, what it is, the value, a line of context and, for usage, a thin bar.</summary>
internal sealed class GlanceTile : Control
{
    private GlanceTileModel model;
    private readonly Font labelFont = Dpi.PaintFont("Segoe UI", 9.5f), valueFont = Dpi.PaintFont("Segoe UI Semibold", 13f), detailFont = Dpi.PaintFont("Segoe UI", 9.25f);
    private bool hover;
    internal event Action<string>? Opened;
    public GlanceTile(GlanceTileModel model)
    {
        this.model = model; TabStop = true; Cursor = Cursors.Hand; Height = 124; AccessibleRole = AccessibleRole.PushButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Click += (_, _) => Opened?.Invoke(this.model.Target);
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) { Opened?.Invoke(this.model.Target); e.Handled = true; } };
        Set(model);
    }
    internal void Set(GlanceTileModel value)
    {
        model = value; Text = value.Label; AccessibleName = value.Label;
        AccessibleDescription = value.Value + (value.Detail.Length > 0 ? ". " + value.Detail : "") + (SummaryView.StatusText(value.Status) is { Length: > 0 } status ? ". " + status : "");
        Invalidate();
    }
    protected override void Dispose(bool disposing) { if (disposing) { labelFont.Dispose(); valueFont.Dispose(); detailFont.Dispose(); } base.Dispose(disposing); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; float s = Dpi.Factor; bool hc = SystemInformation.HighContrast;
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
        TextRenderer.DrawText(g, model.Label, labelFont, new Rectangle(textLeft, pad, Width - textLeft - pad, (int)(30 * s)), muted, line);
        TextRenderer.DrawText(g, model.Value, valueFont, new Rectangle(pad, pad + (int)(38 * s), Width - pad * 2, (int)(28 * s)), text, line);
        TextRenderer.DrawText(g, model.Detail, detailFont, new Rectangle(pad, pad + (int)(66 * s), Width - pad * 2, (int)(20 * s)), muted, line);
        if (model.Percent is { } percent && !hc) {
            float y = Height - pad * 0.75f, width = Width - pad * 2;
            using var track = new SolidBrush(HankiTheme.Raised); g.FillRectangle(track, pad, y, width, 3 * s);
            using var bar = new SolidBrush(model.Status is CardStatus.Good or CardStatus.Info ? HankiTheme.Accent : color);
            g.FillRectangle(bar, pad, y, width * Math.Clamp(percent, 0, 100) / 100f, 3 * s);
        }
    }
}

/// <summary>Recent activity: the latest scans and Hanki's latest changes, each with a way to review it.</summary>
internal sealed class RecentActivityView : RoundedPanel
{
    private readonly TableLayoutPanel rows = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Tag = "card", Margin = Padding.Empty };
    private readonly Action<string> navigate;
    public RecentActivityView(Action<string> navigate)
    {
        this.navigate = navigate;
        Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(22, 14, 18, 14);
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(rows);
    }
    internal void Show(IReadOnlyList<ActivityItem> items)
    {
        rows.SuspendLayout();
        foreach (Control old in rows.Controls.Cast<Control>().ToArray()) old.Dispose();
        rows.RowStyles.Clear(); rows.RowCount = Math.Max(1, items.Count);
        if (items.Count == 0)
            rows.Controls.Add(new Label { Text = "Nothing yet. Scans, checks and the changes Hanki makes will show up here, newest first.", AutoSize = true, Tag = "intro", Margin = new Padding(0, 6, 0, 6) }, 0, 0);
        for (int i = 0; i < items.Count; i++) {
            var item = items[i];
            var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Tag = "card", Margin = new Padding(0, 6, 12, 6) };
            text.Controls.Add(new Label { Text = item.Title, AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Margin = Padding.Empty, UseMnemonic = false });
            text.Controls.Add(new Label { Text = item.Detail, AutoSize = true, Tag = "intro", Margin = new Padding(0, 2, 0, 0), UseMnemonic = false });
            var when = new Label { Text = When(item.At), AutoSize = true, Tag = "intro", Anchor = AnchorStyles.Right, Margin = new Padding(12, 0, 12, 0) };
            var action = new HankiButton { Text = item.ActionLabel, AutoSize = true, Appearance = HankiButtonStyle.Quiet, Anchor = AnchorStyles.Right, AccessibleName = item.ActionLabel + ": " + item.Title };
            action.Click += (_, _) => navigate(item.Target);
            rows.Controls.Add(text, 0, i); rows.Controls.Add(when, 1, i); rows.Controls.Add(action, 2, i);
        }
        HankiTheme.Apply(this);
        rows.ResumeLayout();
    }
    private static string When(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        return local.Date == DateTime.Today ? "Today " + local.ToString("t") : local.Date == DateTime.Today.AddDays(-1) ? "Yesterday " + local.ToString("t") : local.ToString("g");
    }
}
