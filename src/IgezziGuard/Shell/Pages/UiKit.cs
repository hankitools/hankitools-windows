using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Brush = System.Windows.Media.Brush;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using FontFamily = System.Windows.Media.FontFamily;

namespace IgezziGuard.Shell;

/// <summary>Small building blocks shared by the native pages, so tiles and rows look the same everywhere.</summary>
internal static class UiKit
{
    internal static SolidColorBrush Res(string key) => (SolidColorBrush)Application.Current.FindResource(key);
    internal static SolidColorBrush FromDrawing(System.Drawing.Color color) { var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B)); brush.Freeze(); return brush; }
    internal static SolidColorBrush Tint(SolidColorBrush brush, byte alpha) { var c = brush.Color; var tinted = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, c.R, c.G, c.B)); tinted.Freeze(); return tinted; }
    internal static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>A Segoe Fluent glyph for each icon name the navigation and tiles use.</summary>
    internal static string Glyph(string icon) => icon switch {
        "Home" => "", "Overview" => "", "Diagnostic" => "", "Diagnose" => "", "Fix" => "", "Full" => "",
        "Storage" => "", "Memory" => "", "GPU" => "", "Shield" => "", "Maintain" => "", "Connect" => "",
        "Recovery" => "", "Performance" => "", "Gaming" => "", "CPU" => "", "Lab" => "", "Sessions" => "",
        "Help" => "", "Assistant" => "", "Hanki" => "", "Search" => "", _ => ""
    };

    internal static TextBlock Icon(string icon, double size, Brush brush) =>
        new() { Text = Glyph(icon), FontFamily = IconFont, FontSize = size, Foreground = brush, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    /// <summary>The accent brush of a tile: attention colors for problems, the area accent otherwise.</summary>
    internal static SolidColorBrush StatusBrush(CardStatus status, string accent = "Accent") => status switch {
        CardStatus.Good => Res("Good"), CardStatus.Review => Res("Review"), CardStatus.Problem => Res("Problem"), _ => Res(accent)
    };

    internal static string When(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        return local.Date == DateTime.Today ? "Today " + local.ToString("t") : local.Date == DateTime.Today.AddDays(-1) ? "Yesterday " + local.ToString("t") : local.ToString("g");
    }

    internal static TextBlock Text(string text, double size = 14, Brush? brush = null, FontWeight? weight = null, bool wrap = false) =>
        new() { Text = text, FontSize = size, Foreground = brush ?? Res("TextPrimary"), FontWeight = weight ?? FontWeights.Normal, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };

    /// <summary>A status tile (Your PC at a glance): icon, label, value, detail and an optional usage bar. The whole tile opens a page.</summary>
    internal static Button StatusTile(GlanceTileModel model, Action open)
    {
        var color = StatusBrush(model.Status);
        var grid = new Grid { Margin = new Thickness(2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tile = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = Tint(color, 40), Child = Icon(model.Icon, 16, color), Margin = new Thickness(0, 0, 10, 0) };
        grid.Children.Add(tile);
        var label = Text(model.Label, 13.5, Res("TextMuted")); label.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(label, 1); grid.Children.Add(label);
        var value = Text(model.Value, 18, weight: FontWeights.SemiBold); value.Margin = new Thickness(0, 10, 0, 0); Grid.SetRow(value, 1); Grid.SetColumnSpan(value, 2); grid.Children.Add(value);
        var detail = Text(model.Detail, 12.5, Res("TextMuted")); detail.Margin = new Thickness(0, 2, 0, 0); Grid.SetRow(detail, 2); Grid.SetColumnSpan(detail, 2); grid.Children.Add(detail);
        if (model.Percent is { } percent) {
            var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Res("Track"), Margin = new Thickness(0, 10, 0, 0) };
            var bar = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = model.Status is CardStatus.Good or CardStatus.Info ? Res("Accent") : color };
            var host = new Grid(); host.Children.Add(track); host.Children.Add(bar); Grid.SetRow(host, 3); Grid.SetColumnSpan(host, 2);
            host.SizeChanged += (_, e) => bar.Width = Math.Max(0, e.NewSize.Width * Math.Clamp(percent, 0, 100) / 100.0);
            grid.Children.Add(host);
        }
        var button = new Button { Style = (Style)Application.Current.FindResource("TileButton"), Padding = new Thickness(14), Content = grid, MinHeight = 118 };
        System.Windows.Automation.AutomationProperties.SetName(button, model.Label + ": " + model.Value + (model.Detail.Length > 0 ? ". " + model.Detail : ""));
        button.Click += (_, _) => open();
        return button;
    }

    /// <summary>A tool card (More tools): icon tile, title, one line of description. The whole card opens its page.</summary>
    internal static Button ToolCard(string icon, string title, string description, Action open, string accent = "Accent")
    {
        var brush = Res(accent);
        var stack = new StackPanel { Margin = new Thickness(2) };
        var head = new DockPanel { LastChildFill = true };
        var tile = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = Tint(brush, 40), Child = Icon(icon, 19, brush), Margin = new Thickness(0, 0, 14, 0) };
        var arrow = new TextBlock { Text = "→", FontSize = 16, Foreground = Res("TextMuted"), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(arrow, Dock.Right);
        DockPanel.SetDock(tile, Dock.Left);
        head.Children.Add(tile); head.Children.Add(arrow); head.Children.Add(Text(title, 16, weight: FontWeights.SemiBold));
        ((TextBlock)head.Children[2]).VerticalAlignment = VerticalAlignment.Center;
        stack.Children.Add(head);
        var body = Text(description, 13, Res("TextMuted"), wrap: true); body.Margin = new Thickness(0, 12, 0, 0); stack.Children.Add(body);
        var button = new Button { Style = (Style)Application.Current.FindResource("TileButton"), Padding = new Thickness(16), Content = stack, MinHeight = 120, VerticalContentAlignment = VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        System.Windows.Automation.AutomationProperties.SetHelpText(button, description);
        button.Click += (_, _) => open();
        return button;
    }

    /// <summary>A wrapping grid whose column count follows the available width (three, two or one across).</summary>
    internal static System.Windows.Controls.Primitives.UniformGrid AdaptiveGrid(double gap = 14)
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        grid.SizeChanged += (_, e) => grid.Columns = e.NewSize.Width >= 900 ? 3 : e.NewSize.Width >= 560 ? 2 : 1;
        return grid;
    }
    internal static void Gap(IEnumerable<FrameworkElement> items, double gap = 14) { foreach (var item in items) item.Margin = new Thickness(0, 0, gap, gap); }
}

internal static class ResultChips
{
    /// <summary>The scan's count chips ("2 to review", "10 OK", …) as dots with text.</summary>
    internal static System.Windows.Controls.WrapPanel Build(IEnumerable<DiagnosticResult> results)
    {
        var panel = new System.Windows.Controls.WrapPanel();
        foreach (var (color, text) in StatusChips.Count(results)) {
            var chip = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 4) };
            chip.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = UiKit.FromDrawing(color), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            chip.Children.Add(UiKit.Text(text, 14));
            panel.Children.Add(chip);
        }
        return panel;
    }
}
