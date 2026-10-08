using System.Windows;
using Application = System.Windows.Application;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Orientation = System.Windows.Controls.Orientation;

namespace IgezziGuard.Shell;

/// <summary>
/// A compact trend chart for one measurement over time: a filled line, a title with the latest value, and the peak and average.
/// Values outside the range are clamped; missing values (null) break the line instead of drawing zero.
/// </summary>
internal sealed class TrendChart : Border
{
    private readonly TextBlock title = new(), latest = new(), footer = new();
    private readonly Canvas plot = new() { Height = 74, ClipToBounds = true, Margin = new Thickness(0, 8, 0, 6) };
    private readonly Brush color;
    private IReadOnlyList<double?> values = [];
    private double max = 100;
    private string unit = "%";

    internal int PointCount => values.Count(v => v is not null);

    internal TrendChart(string name, string accent)
    {
        color = UiKit.Res(accent);
        Style = (Style)Application.Current.FindResource("Card"); Padding = new Thickness(16, 12, 16, 12);
        title.Text = name; title.FontSize = 14; title.FontWeight = FontWeights.SemiBold; title.Foreground = UiKit.Res("TextPrimary");
        latest.FontSize = 14; latest.Foreground = color; latest.HorizontalAlignment = HorizontalAlignment.Right;
        footer.FontSize = 12.5; footer.Foreground = UiKit.Res("TextMuted"); footer.TextWrapping = TextWrapping.Wrap;
        var head = new DockPanel(); DockPanel.SetDock(latest, Dock.Right); head.Children.Add(latest); head.Children.Add(title);
        var stack = new StackPanel(); stack.Children.Add(head); stack.Children.Add(plot); stack.Children.Add(footer);
        Child = stack; plot.SizeChanged += (_, _) => Draw();
        System.Windows.Automation.AutomationProperties.SetName(this, name);
    }

    /// <summary>Sets the series. <paramref name="top"/> fixes the top of the scale (100 for percentages); null scales to the peak.</summary>
    internal void Set(IEnumerable<double?> series, string unit = "%", double? top = 100)
    {
        values = series.ToArray(); this.unit = unit;
        var real = values.Where(v => v is not null).Select(v => v!.Value).ToArray();
        max = top ?? Math.Max(1, real.Length == 0 ? 1 : real.Max() * 1.15);
        if (real.Length == 0) { latest.Text = "not reported"; footer.Text = "This counter isn't available on this PC."; }
        else {
            latest.Text = $"{real[^1]:0.#} {unit}";
            footer.Text = $"Peak {real.Max():0.#} {unit} · average {real.Average():0.#} {unit} · {real.Length} seconds";
        }
        System.Windows.Automation.AutomationProperties.SetHelpText(this, latest.Text + ". " + footer.Text);
        Draw();
    }

    private void Draw()
    {
        plot.Children.Clear();
        double w = plot.ActualWidth, h = plot.Height;
        if (w <= 0 || values.Count < 2) return;
        foreach (var fraction in new[] { 0.25, 0.5, 0.75 })
            plot.Children.Add(new Line { X1 = 0, X2 = w, Y1 = h * fraction, Y2 = h * fraction, Stroke = UiKit.Res("Hairline"), StrokeThickness = 1 });
        double Y(double v) => h - Math.Clamp(v / max, 0, 1) * (h - 2) - 1;
        double X(int i) => i * w / (values.Count - 1);
        var segment = new List<System.Windows.Point>();
        void Flush() {
            if (segment.Count >= 2) {
                var fill = new Polygon { Fill = UiKit.Tint((SolidColorBrush)color, 38) };
                fill.Points.Add(new System.Windows.Point(segment[0].X, h)); foreach (var p in segment) fill.Points.Add(p); fill.Points.Add(new System.Windows.Point(segment[^1].X, h));
                plot.Children.Add(fill);
                var line = new Polyline { Stroke = color, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
                foreach (var p in segment) line.Points.Add(p); plot.Children.Add(line);
            }
            segment.Clear();
        }
        for (int i = 0; i < values.Count; i++) { if (values[i] is { } v) segment.Add(new System.Windows.Point(X(i), Y(v))); else Flush(); }
        Flush();
    }
}
