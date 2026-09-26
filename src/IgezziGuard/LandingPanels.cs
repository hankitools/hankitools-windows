namespace IgezziGuard;

/// <summary>An area's other pages as large tiles under a section heading (HANKI-UX-300), laid out in 1–3 columns.</summary>
internal static class ToolTiles
{
    /// <summary>Adds the heading and tiles to a Dock=Top page (added in reverse, so call before anything that sits above them).</summary>
    internal static void Add(Control page, string heading, IEnumerable<(string Icon, string Title, string Text, Action Open)> tiles, Color accent)
    {
        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        foreach (var (icon, title, text, open) in tiles) cards.Controls.Add(new HankiCard(icon, title, text, open, accent) { Margin = new Padding(0, 0, 14, 14) });
        void Fit() {
            int width = Math.Max(260, page.ClientSize.Width - page.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            int columns = width >= 900 ? 3 : width >= 560 ? 2 : 1;
            foreach (Control card in cards.Controls) card.Width = Math.Max(220, (width - 14 * columns) / columns);
        }
        page.SizeChanged += (_, _) => Fit();
        page.Controls.Add(cards);
        page.Controls.Add(Heading(heading));
        Fit();
    }
    internal static IEnumerable<(string, string, string, Action)> For(ProductArea area, Action<string> navigate) =>
        Navigation.Tools(area).Select(i => (i.Icon, Navigation.Title(i), i.Introduction, (Action)(() => navigate(i.Page))));
    internal static Label Heading(string text) => new() { Text = Localizer.T(text), Dock = DockStyle.Top, AutoSize = false, Height = 56,
        Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(2, 0, 0, 12) };

    /// <summary>
    /// For a scrolling page of Dock=Top controls (added bottom first): tab order follows what you see, top to bottom,
    /// and the page opens at the top. Otherwise the first focus lands on the last control and scrolls the page down.
    /// </summary>
    internal static void TopDown(ScrollableControl page)
    {
        int index = 0;
        foreach (var control in page.Controls.Cast<Control>().Reverse()) control.TabIndex = index++;
        page.VisibleChanged += (_, _) => { if (page.Visible) page.AutoScrollPosition = Point.Empty; };
    }
}

/// <summary>History: system actions and performance sessions, kept apart, and Recovery to undo a change.</summary>
internal sealed class HistoryLanding : UserControl
{
    public HistoryLanding(Action<string> navigate)
    {
        Dock = DockStyle.Fill; AutoScroll = true; Padding = new Padding(0, 4, 8, 16);
        var recovery = Navigation.Find("Recovery")!;
        ToolTiles.Add(this, "Undo a change", [(recovery.Icon, "Recovery", recovery.Introduction, () => navigate("Recovery"))], HankiTheme.Accent);
        ToolTiles.Add(this, "What Hanki has done", ToolTiles.For(ProductArea.History, navigate), HankiTheme.Accent);
        ToolTiles.TopDown(this);
    }
}

/// <summary>Help: guides and community, the Assistant, and Hanki Pro.</summary>
internal sealed class HelpLanding : UserControl
{
    public HelpLanding(Action<string> navigate, Action remoteHelp)
    {
        Dock = DockStyle.Fill; AutoScroll = true; Padding = new Padding(0, 4, 8, 16);
        ToolTiles.Add(this, "More help", [("Help", "Remote help", "Opens Windows' Quick Assist so someone you trust can see your screen. Hanki shows a scam warning first.", remoteHelp)], HankiTheme.Accent);
        ToolTiles.Add(this, "Help and support", ToolTiles.For(ProductArea.Support, navigate), HankiTheme.Accent);
        ToolTiles.TopDown(this);
    }
}
