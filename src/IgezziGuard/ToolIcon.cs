using System.Drawing.Drawing2D;
namespace IgezziGuard;

internal sealed class ToolIcon : Control
{
    internal string Kind { get; set; } = "Home";
    public ToolIcon() { Height = 38; Dock = DockStyle.Top; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Draw(e.Graphics, new RectangleF(0, 0, 28 * DeviceDpi / 96f, 28 * DeviceDpi / 96f), Kind,
            SystemInformation.HighContrast ? SystemColors.ControlText : HankiTheme.Accent);
    }
    internal static void Draw(Graphics graphics, RectangleF bounds, string kind, Color color)
    {
        var state = graphics.Save(); graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(bounds.Width / 32, bounds.Height / 32);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = new Pen(color, 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (kind.Split(' ')[0]) {
            case "Performance": graphics.DrawLines(p, new Point[] {new(2,17),new(8,17),new(12,6),new(18,27),new(23,13),new(27,17),new(30,17)}); break;
            case "Maintain": graphics.DrawRectangle(p,8,9,16,19); graphics.DrawLine(p,6,7,26,7); graphics.DrawLine(p,12,3,20,3); graphics.DrawLine(p,13,13,13,24); graphics.DrawLine(p,19,13,19,24); break;
            case "Shield": graphics.DrawPolygon(p,new Point[] {new(16,3),new(27,7),new(25,21),new(16,29),new(7,21),new(5,7)}); graphics.DrawLines(p,new Point[] {new(10,16),new(14,20),new(22,12)}); break;
            case "Connect": graphics.DrawEllipse(p,3,3,26,26); graphics.DrawEllipse(p,10,3,12,26); graphics.DrawLine(p,3,16,29,16); break;
            case "Assistant": graphics.DrawRectangle(p,4,5,24,18); graphics.DrawLines(p,new Point[] {new(9,23),new(9,29),new(15,23)}); break;
            case "Recovery": graphics.DrawArc(p,6,5,22,22,210,280); graphics.DrawLines(p,new Point[] {new(3,5),new(3,13),new(11,13)}); break;
            case "Home": graphics.DrawLines(p,new Point[] {new(3,15),new(16,4),new(29,15)}); graphics.DrawLines(p,new Point[] {new(7,13),new(7,28),new(25,28),new(25,13)}); break;
            case "Full": // scan frame with check
                graphics.DrawLines(p,new Point[] {new(3,10),new(3,3),new(10,3)}); graphics.DrawLines(p,new Point[] {new(22,3),new(29,3),new(29,10)});
                graphics.DrawLines(p,new Point[] {new(29,22),new(29,29),new(22,29)}); graphics.DrawLines(p,new Point[] {new(10,29),new(3,29),new(3,22)});
                graphics.DrawLines(p,new Point[] {new(10,16),new(14,20),new(22,12)}); break;
            case "Diagnose": case "Search": graphics.DrawEllipse(p,4,4,18,18); graphics.DrawLine(p,19,19,28,28); break;
            case "Diagnostic": graphics.DrawEllipse(p,4,4,24,24); graphics.DrawLines(p,new Point[] {new(16,9),new(16,16),new(21,19)}); break;
            case "Scan": graphics.DrawLine(p,5,8,27,8); graphics.DrawLine(p,5,16,27,16); graphics.DrawLine(p,5,24,19,24); break;
            case "Help": graphics.DrawEllipse(p,3,3,26,26); graphics.DrawArc(p,11,8,10,9,180,240); graphics.DrawLine(p,16,17,16,19); graphics.DrawEllipse(p,15.2f,22.5f,1.6f,1.6f); break;
            case "Hanki": graphics.DrawPolygon(p,new Point[] {new(16,3),new(20,12),new(29,13),new(22,19),new(24,28),new(16,23),new(8,28),new(10,19),new(3,13),new(12,12)}); break;
            default: graphics.DrawRectangle(p,3,4,26,18); graphics.DrawLine(p,16,22,16,28); graphics.DrawLine(p,10,28,22,28); break;
        }
        graphics.Restore(state);
    }
}
