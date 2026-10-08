using System.Drawing.Drawing2D;

namespace IgezziGuard;

/// <summary>Headline banner plus status cards. Plain language first; callers keep the technical report elsewhere.</summary>
internal sealed class SummaryView : UserControl
{
    private readonly FlowLayoutPanel cards = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly StatusBanner banner = new() { Dock = DockStyle.Top, Visible = false };
    private readonly Panel gap = new() { Dock = DockStyle.Top, Height = 12, Visible = false };
    public SummaryView()
    {
        Dock = DockStyle.Fill;
        Controls.Add(cards); Controls.Add(gap); Controls.Add(banner);
        // Cards always fit the width; only vertical scrolling is wanted.
        cards.AutoScroll = false; cards.HorizontalScroll.Enabled = false; cards.HorizontalScroll.Visible = false; cards.HorizontalScroll.Maximum = 0; cards.AutoScroll = true;
        cards.SizeChanged += (_, _) => Fit();
        VisibleChanged += (_, _) => { if (Visible) Fit(); };
    }
    public void Show(CardStatus? status, string? headline, IEnumerable<ResultCard> items)
    {
        // Visible reads false while this view is hidden, so decide from the inputs, not from the property.
        bool hasHeadline = status is not null && !string.IsNullOrWhiteSpace(headline);
        banner.Visible = gap.Visible = hasHeadline;
        if (hasHeadline) banner.Set(status!.Value, headline!);
        cards.SuspendLayout();
        foreach (Control old in cards.Controls.Cast<Control>().ToArray()) old.Dispose();
        foreach (var item in items) {
            var card = new StatusCardPanel(item.Status) { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20, 14, 18, 16), Margin = new Padding(0, 0, 0, 10) };
            var content = new TableLayoutPanel { Tag = "card", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 3, Dock = DockStyle.Top };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            content.Controls.Add(new Label { Text = item.Title, AutoSize = true, Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(0, 0, 0, 8) }, 0, 0);
            var pill = StatusText(item.Status);
            if (pill.Length > 0) content.Controls.Add(new Label { Text = pill, AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5f), Tag = "status-" + item.Status.ToString().ToLowerInvariant(), Margin = new Padding(12, 2, 0, 0) }, 1, 0);
            var body = new Label { Text = item.Body, AutoSize = true, Margin = Padding.Empty, Font = new Font("Segoe UI", 11f) };
            content.Controls.Add(body, 0, 1); content.SetColumnSpan(body, 2);
            if (item.Action is { } action) {
                var open = new HankiButton { Text = (item.ActionLabel ?? "Open") + "  →", AutoSize = true, Appearance = HankiButtonStyle.Quiet, Margin = new Padding(0, 8, 0, 0), AccessibleName = item.ActionLabel ?? "Open" };
                open.Click += (_, _) => action();
                content.Controls.Add(open, 0, 2);
            }
            card.Controls.Add(content); cards.Controls.Add(card);
        }
        HankiTheme.Apply(this);
        cards.ResumeLayout(); Fit();
        cards.AutoScrollPosition = Point.Empty;
    }
    internal static string StatusText(CardStatus status) => status switch {
        CardStatus.Good => "Looks OK", CardStatus.Review => "Worth reviewing", CardStatus.Problem => "Needs attention", CardStatus.Unknown => "Not available", _ => ""
    };
    private void Fit()
    {
        int width = Math.Max(180, cards.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (Control card in cards.Controls) {
            card.MinimumSize = new Size(width, 0); card.MaximumSize = new Size(width, 0);
            var content = card.Controls[0];
            foreach (var label in content.Controls.OfType<Label>().Where(l => l.Tag as string is not { } tag || !tag.StartsWith("status-", StringComparison.Ordinal)))
                label.MaximumSize = new Size(Math.Max(100, width - card.Padding.Horizontal - (label.Font.Bold || label.Font.Name.Contains("Semibold") ? 130 : 8)), 0);
        }
    }
}

/// <summary>Rounded card with a status-colored edge.</summary>
internal sealed class StatusCardPanel : Panel
{
    private readonly CardStatus status;
    public StatusCardPanel(CardStatus status) { this.status = status; Tag = "card"; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (SystemInformation.HighContrast) { base.OnPaintBackground(e); ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.ControlText, ButtonBorderStyle.Solid); return; }
        float s = DeviceDpi / 96f; var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? HankiTheme.Canvas); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = HankiButton.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), HankiTheme.CardRadius * s);
        using (var fill = new SolidBrush(HankiTheme.Surface)) g.FillPath(fill, path);
        using (var border = new Pen(HankiTheme.Hairline)) g.DrawPath(border, path);
        if (status == CardStatus.Info) return;
        var state = g.Save(); g.SetClip(path);
        using (var edge = new SolidBrush(HankiTheme.StatusColor(status))) g.FillRectangle(edge, 0, 0, 4 * s, Height);
        g.Restore(state);
    }
}

/// <summary>One-sentence outcome with a colored status mark.</summary>
internal sealed class StatusBanner : Control
{
    private CardStatus status;
    private readonly Font font = new("Segoe UI Semibold", 12f);
    public StatusBanner() { Height = 58; AccessibleRole = AccessibleRole.StaticText; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override void Dispose(bool disposing) { if (disposing) font.Dispose(); base.Dispose(disposing); }
    public void Set(CardStatus value, string headline)
    {
        status = value; Text = headline; AccessibleName = (SummaryView.StatusText(value) is { Length: > 0 } label ? label + ": " : "") + headline;
        Height = (int)(58 * DeviceDpi / 96f); Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f; var g = e.Graphics; bool hc = SystemInformation.HighContrast;
        g.Clear(Parent?.BackColor ?? HankiTheme.Canvas); g.SmoothingMode = SmoothingMode.AntiAlias;
        var color = hc ? SystemColors.ControlText : HankiTheme.StatusColor(status);
        using (var path = HankiButton.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), HankiTheme.CardRadius * s)) {
            using var fill = new SolidBrush(hc ? SystemColors.Control : Color.FromArgb(26, color)); g.FillPath(fill, path);
            using var border = new Pen(hc ? SystemColors.ControlText : Color.FromArgb(70, color)); g.DrawPath(border, path);
        }
        float d = 12 * s;
        using (var dot = new SolidBrush(color)) g.FillEllipse(dot, 18 * s, (Height - d) / 2, d, d);
        TextRenderer.DrawText(g, Text, font, new Rectangle((int)(42 * s), 0, Width - (int)(56 * s), Height), hc ? SystemColors.ControlText : HankiTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class ResultCardsView : UserControl
{
    private readonly SummaryView summary = new();
    private readonly HankiButton details = new() { Text = Localizer.T("Technical details"), AutoSize = true, Appearance = HankiButtonStyle.Quiet, Enabled = false, AccessibleName = Localizer.T("View technical details") };
    private readonly FlowLayoutPanel footer = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly TextBox evidence;
    public ResultCardsView(TextBox evidence)
    {
        Dock = DockStyle.Fill; this.evidence = evidence;
        footer.Controls.Add(details);
        evidence.Visible = false;
        Controls.Add(evidence); Controls.Add(summary); Controls.Add(footer);
        details.Click += (_, _) => {
            bool show = !evidence.Visible; evidence.Visible = show; summary.Visible = !show;
            details.Text = show ? "Back to summary" : "Technical details";
        };
    }
    public void ShowCards(IEnumerable<ResultCard> items, bool hasEvidence = true) => Present(null, null, items, hasEvidence);
    public void Show(Diagnosis diagnosis) => Present(diagnosis.Status, diagnosis.Headline, diagnosis.Cards, true);
    private void Present(CardStatus? status, string? headline, IEnumerable<ResultCard> items, bool hasEvidence)
    {
        summary.Show(status, headline, items);
        evidence.Visible = false; summary.Visible = true; details.Enabled = hasEvidence; details.Text = Localizer.T("Technical details");
    }
    /// <summary>Moves the summary/details switch into a page's own action row, instead of a line below the cards.</summary>
    internal void PlaceDetailsIn(Control host) { footer.Controls.Remove(details); footer.Visible = false; details.Margin = new Padding(0, 1, 4, 0); host.Controls.Add(details); }
}
