namespace IgezziGuard;

internal sealed class ResultCardsView : UserControl
{
    private readonly FlowLayoutPanel cards = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly HankiButton details = new() { Text = "View technical details", AutoSize = true, Appearance = HankiButtonStyle.Quiet, Enabled = false };
    private readonly TextBox evidence;
    public ResultCardsView(TextBox evidence)
    {
        Dock = DockStyle.Fill; this.evidence = evidence;
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        footer.Controls.Add(details);
        evidence.Visible = false;
        Controls.Add(evidence); Controls.Add(cards); Controls.Add(footer);
        details.Click += (_, _) => {
            bool show = !evidence.Visible; evidence.Visible = show; cards.Visible = !show;
            details.Text = show ? "Back to summary" : "View technical details";
        };
        cards.SizeChanged += (_, _) => Fit();
    }
    public void ShowCards(IEnumerable<ResultCard> items, bool hasEvidence = true)
    {
        cards.SuspendLayout();
        foreach (Control old in cards.Controls.Cast<Control>().ToArray()) old.Dispose();
        foreach (var item in items) {
            var card = new Panel { Tag = "card", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(18), Margin = new Padding(0, 0, 0, 12) };
            var content = new TableLayoutPanel { Tag = "card", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 2, Dock = DockStyle.Top };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.Controls.Add(new Label { Text = item.Title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) }, 0, 0);
            content.Controls.Add(new Label { Text = item.Body, AutoSize = true, Margin = Padding.Empty }, 0, 1);
            card.Controls.Add(content); cards.Controls.Add(card); HankiTheme.Apply(card);
        }
        evidence.Visible = false; cards.Visible = true; details.Enabled = hasEvidence; details.Text = "View technical details";
        cards.ResumeLayout(); Fit();
    }
    private void Fit()
    {
        int width = Math.Max(180, cards.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (Control card in cards.Controls) {
            card.MinimumSize = new Size(width, 0); card.MaximumSize = new Size(width, 0);
            foreach (var label in card.Controls[0].Controls.OfType<Label>()) label.MaximumSize = new Size(Math.Max(100, width - card.Padding.Horizontal - 8), 0);
        }
    }
}
