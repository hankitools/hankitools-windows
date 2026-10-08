using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;

namespace IgezziGuard.Shell;

/// <summary>Building blocks shared by the pages that are mostly text: a read-only output box, a titled card with buttons, a notice and a small editor window.</summary>
internal static class Blocks
{
    /// <summary>A read-only, wrapping text box on a card, for the plain-text reports some pages show.</summary>
    internal static TextBox Output(double maxHeight = 520)
    {
        var box = new TextBox { Style = (Style)Application.Current.FindResource("FieldBox"), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Padding = new Thickness(14, 12, 14, 12),
            MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, AcceptsReturn = true, Margin = new Thickness(0, 8, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(box, "Report");
        return box;
    }

    /// <summary>A card with a heading, a description and a row of quiet buttons.</summary>
    internal static Border Section(string title, string description, params (string Label, Action Action)[] actions)
    {
        var stack = new StackPanel();
        var heading = UiKit.Text(title, 17, weight: FontWeights.SemiBold, wrap: true); System.Windows.Automation.AutomationProperties.SetHeadingLevel(heading, System.Windows.Automation.AutomationHeadingLevel.Level2);
        stack.Children.Add(heading);
        var body = UiKit.Text(description, 14, UiKit.Res("TextMuted"), wrap: true); body.Margin = new Thickness(0, 8, 0, 12); stack.Children.Add(body);
        var bar = new WrapPanel();
        foreach (var (label, action) in actions) { var b = Buttons.Secondary(label); b.Click += (_, _) => action(); bar.Children.Add(b); }
        stack.Children.Add(bar);
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(22, 18, 22, 10), Margin = new Thickness(0, 0, 0, 14), Child = stack };
    }

    /// <summary>A page body: a scrolling column with the page's standard margins.</summary>
    internal static (ScrollViewer Scroller, StackPanel Stack) Page()
    {
        var stack = new StackPanel { Margin = new Thickness(24, 4, 24, 28) };
        return (new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack }, stack);
    }

    /// <summary>Tells the person something in the review drawer, with one OK button. No choice is offered.</summary>
    internal static void Notice(string title, string text) =>
        ReviewPresenter.Ask(new ReviewRequest(title, text) { ConfirmLabel = "OK" });

    /// <summary>Asks a yes/no question in the drawer; the named action is never the default.</summary>
    internal static bool Confirm(string title, string text, string confirm, bool danger = false) =>
        ReviewPresenter.Ask(new ReviewRequest(title, text) { ConfirmLabel = confirm, Danger = danger }).Confirmed;

    /// <summary>Opens a link or settings URI in the default handler; tells the person if that fails.</summary>
    internal static void OpenLink(string url)
    {
        try { using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Notice("Could not open it", ex.Message); }
    }

    /// <summary>Copies text and says so; a failure is shown instead of ignored.</summary>
    internal static void Copy(string text)
    {
        try { System.Windows.Clipboard.SetText(text); Notice("Copied", "Copied. Review before sharing."); }
        catch (System.Runtime.InteropServices.ExternalException ex) { Notice("Could not copy text", ex.Message); }
    }

    /// <summary>A small dark window with an editable text box and buttons, for text the person reviews before using it.</summary>
    internal static void EditText(Window owner, string title, string text, params (string Label, Action<string> Action, bool Primary)[] actions)
    {
        var window = new Window { Title = title, Width = 820, Height = 640, MinWidth = 540, MinHeight = 400, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = UiKit.Res("Surface"), Foreground = UiKit.Res("TextPrimary"), FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 14, ShowInTaskbar = false };
        DarkTitleBar.Apply(window);
        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var box = new TextBox { Style = (Style)Application.Current.FindResource("FieldBox"), Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13.5, Padding = new Thickness(12) };
        System.Windows.Automation.AutomationProperties.SetName(box, title);
        var bar = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (label, action, primary) in actions) { var b = primary ? Buttons.Primary(label) : Buttons.Secondary(label); b.Click += (_, _) => action(box.Text); bar.Children.Add(b); }
        var close = Buttons.Quiet("Close"); close.IsCancel = true; close.Click += (_, _) => window.Close(); bar.Children.Add(close);
        Grid.SetRow(bar, 1); grid.Children.Add(box); grid.Children.Add(bar); window.Content = grid;
        window.ShowDialog();
    }
}
