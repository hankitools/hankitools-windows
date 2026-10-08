using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace IgezziGuard.Shell;

/// <summary>
/// A plain-language result: a status headline, one card per observation (each may offer an action), and the technical report
/// one click away. It is what the old report pages showed, without the wall of text first.
/// </summary>
internal sealed class DiagnosisView : StackPanel
{
    private readonly StackPanel body = new();
    private readonly Button toggle = new();
    private readonly TextBox report = new();
    private string reportText = "";

    internal string ReportText => reportText;
    internal int CardCount { get; private set; }

    internal DiagnosisView()
    {
        toggle.Style = (Style)Application.Current.FindResource("QuietButton"); toggle.Content = "Technical details"; toggle.HorizontalAlignment = HorizontalAlignment.Left; toggle.Margin = new Thickness(0, 14, 0, 0); toggle.Visibility = Visibility.Collapsed;
        toggle.Click += (_, _) => { bool show = report.Visibility != Visibility.Visible; report.Visibility = show ? Visibility.Visible : Visibility.Collapsed; toggle.Content = show ? "Hide technical details" : "Technical details"; };
        report.Style = (Style)Application.Current.FindResource("FieldBox"); report.IsReadOnly = true; report.TextWrapping = TextWrapping.Wrap; report.FontFamily = new System.Windows.Media.FontFamily("Consolas"); report.FontSize = 12.5;
        report.MaxHeight = 380; report.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; report.Visibility = Visibility.Collapsed; report.Margin = new Thickness(0, 8, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(report, "Technical report");
        Children.Add(body); Children.Add(toggle); Children.Add(report);
        Visibility = Visibility.Collapsed;
    }

    internal static Brush StatusBrush(CardStatus status) => status switch {
        CardStatus.Good => UiKit.Res("Good"), CardStatus.Review => UiKit.Res("Review"), CardStatus.Problem => UiKit.Res("Problem"), CardStatus.Unknown => UiKit.Res("TextMuted"), _ => UiKit.Res("Accent")
    };

    internal void Clear() { body.Children.Clear(); report.Text = ""; reportText = ""; CardCount = 0; Visibility = Visibility.Collapsed; }

    /// <summary>Shows a diagnosis; the report text goes behind "Technical details".</summary>
    internal void Show(Diagnosis diagnosis)
    {
        body.Children.Clear(); CardCount = diagnosis.Cards.Count;
        var color = (SolidColorBrush)StatusBrush(diagnosis.Status);
        var banner = new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(18, 14, 18, 14), Background = UiKit.Tint(color, 28), BorderBrush = UiKit.Tint(color, 90) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 11, Height = 11, Fill = color, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(UiKit.Text(diagnosis.Headline, 17, weight: FontWeights.SemiBold, wrap: true));
        banner.Child = row; body.Children.Add(banner);
        System.Windows.Automation.AutomationProperties.SetName(banner, diagnosis.Headline);
        foreach (var card in diagnosis.Cards) body.Children.Add(Card(card));
        reportText = diagnosis.Report; report.Text = diagnosis.Report;
        toggle.Visibility = diagnosis.Report.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Visibility = Visibility.Visible;
    }

    private static UIElement Card(ResultCard card)
    {
        var color = (SolidColorBrush)StatusBrush(card.Status);
        var inner = new DockPanel();
        var bar = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = color, Margin = new Thickness(0, 2, 14, 2) }; DockPanel.SetDock(bar, Dock.Left); inner.Children.Add(bar);
        if (card.ActionLabel is { Length: > 0 } label && card.Action is { } action) {
            var button = new Button { Style = (Style)Application.Current.FindResource("SecondaryButton"), Content = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            button.Click += (_, _) => action(); DockPanel.SetDock(button, Dock.Right); inner.Children.Add(button);
        }
        var text = new StackPanel();
        text.Children.Add(UiKit.Text(card.Title, 15, weight: FontWeights.SemiBold, wrap: true));
        var detail = UiKit.Text(card.Body, 13.5, UiKit.Res("TextMuted"), wrap: true); detail.Margin = new Thickness(0, 3, 0, 0); text.Children.Add(detail);
        inner.Children.Add(text);
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 10, 0, 0), Child = inner };
    }
}
