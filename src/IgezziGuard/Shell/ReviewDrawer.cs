using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace IgezziGuard.Shell;

/// <summary>
/// The in-app confirmation: a panel on the right edge of the window that says what will happen, what changes, how to undo it and
/// what the choices are. Cancel has focus when it opens and Enter never confirms; Escape cancels.
/// </summary>
internal sealed class ReviewDrawer : Window
{
    private readonly ReviewRequest request;
    private readonly Button confirm = new(), cancel = new();
    private readonly TextBlock problem = UiKit.Text("", 13, UiKit.Res("Review"), wrap: true);
    private readonly List<(RadioButton Radio, ReviewChoice Choice)> choices = [];
    private readonly List<(CheckBox Box, ReviewItem Item)> items = [];
    private readonly List<(CheckBox Box, ReviewOption Option)> options = [];
    private readonly CheckBox acknowledge = new() { Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
    /// <summary>What was chosen; null until the drawer closes.</summary>
    internal ReviewResult? Result { get; private set; }
    internal Button ConfirmButton => confirm;
    internal Button CancelButton => cancel;
    internal string ConfirmText => confirm.Content?.ToString() ?? "";
    internal string ProblemText => problem.Text;

    internal ReviewDrawer(ReviewRequest request, FrameworkElement? anchor = null)
    {
        this.request = request;
        DarkTitleBar.Apply(this);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Width = 440; Height = 640;
        Background = UiKit.Res("Surface"); Foreground = UiKit.Res("TextPrimary"); FontFamily = new FontFamily("Segoe UI"); FontSize = 14; UseLayoutRounding = true;
        BorderBrush = UiKit.Res("Border"); BorderThickness = new Thickness(1, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(this, request.Title);
        Content = BuildContent();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Finish(false); } };
        Loaded += (_, _) => { Position(anchor); cancel.Focus(); };
        Closed += (_, _) => Result ??= new ReviewResult(false, Current());
        if (anchor is not null && Owner is null) Owner = Window.GetWindow(anchor);
    }

    // Hooks for the UI check: the same changes a person makes with the mouse.
    internal void SelectChoice(string id) { foreach (var (radio, choice) in choices) if (choice.Id == id) radio.IsChecked = true; }
    internal void Tick(string id, bool on) { foreach (var (box, item) in items) if (item.Id == id) box.IsChecked = on; foreach (var (box, option) in options) if (option.Id == id) box.IsChecked = on; }
    internal void Acknowledge(bool on) => acknowledge.IsChecked = on;
    internal bool ConfirmEnabled => confirm.IsEnabled;
    internal bool AcknowledgeVisible => acknowledge.Visibility == Visibility.Visible;

    /// <summary>Docks the drawer to the right edge of the anchor (the shell's content area), full height.</summary>
    private void Position(FrameworkElement? anchor)
    {
        if (anchor is null || PresentationSource.FromVisual(anchor) is not { } source) return;
        var toDips = source.CompositionTarget.TransformFromDevice;
        var topLeft = toDips.Transform(anchor.PointToScreen(new System.Windows.Point(0, 0)));
        Height = anchor.ActualHeight; Top = topLeft.Y; Left = topLeft.X + anchor.ActualWidth - Width;
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel();
        var footer = new Border { Padding = new Thickness(24, 14, 24, 18), BorderBrush = UiKit.Res("Hairline"), BorderThickness = new Thickness(0, 1, 0, 0), Background = UiKit.Res("Surface") };
        DockPanel.SetDock(footer, Dock.Bottom);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Style = (Style)Application.Current.FindResource("SecondaryButton"); cancel.Content = "Cancel"; cancel.Margin = new Thickness(0, 0, 10, 0); cancel.IsCancel = true;
        cancel.Click += (_, _) => Finish(false);
        confirm.Style = (Style)Application.Current.FindResource(request.Danger ? "DangerButton" : "PrimaryButton"); confirm.Content = request.ConfirmLabel; confirm.FontWeight = FontWeights.SemiBold;
        confirm.Click += (_, _) => Finish(true);
        buttons.Children.Add(cancel); buttons.Children.Add(confirm);
        var footerStack = new StackPanel(); problem.Margin = new Thickness(0, 0, 0, 10); problem.Visibility = Visibility.Collapsed;
        footerStack.Children.Add(problem); footerStack.Children.Add(buttons); footer.Child = footerStack;
        root.Children.Add(footer);

        var body = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        var title = UiKit.Text(request.Title, 21, weight: FontWeights.SemiBold, wrap: true); body.Children.Add(title);
        if (request.Intro is { Length: > 0 }) { var intro = UiKit.Text(request.Intro, 14.5, UiKit.Res("TextMuted"), wrap: true); intro.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(intro); }
        if (request.Rows.Count > 0) body.Children.Add(Rows());
        if (request.ListLines.Count > 0) body.Children.Add(ListBox());
        if (request.Choices.Count > 0) body.Children.Add(Choices());
        if (request.Items.Count > 0) body.Children.Add(Items());
        if (request.Options.Count > 0) body.Children.Add(Options());
        if (request.Notes.Count > 0) body.Children.Add(Notes());
        acknowledge.Style = null; body.Children.Add(acknowledge);
        acknowledge.Checked += (_, _) => Refresh(); acknowledge.Unchecked += (_, _) => Refresh();
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body });
        Refresh();
        return root;
    }

    private UIElement Rows()
    {
        var card = new Border { Style = (Style)Application.Current.FindResource("Card"), Background = UiKit.Res("Canvas"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 18, 0, 0) };
        var stack = new StackPanel();
        for (int i = 0; i < request.Rows.Count; i++) {
            var row = request.Rows[i];
            var label = new TextBlock { Text = row.Label.ToUpperInvariant(), Style = (Style)Application.Current.FindResource("Eyebrow"), Foreground = UiKit.Res("TextMuted"), Margin = new Thickness(0, i == 0 ? 0 : 12, 0, 2) };
            var accent = i == request.Rows.Count - 1 && request.Rows.Count > 1;
            stack.Children.Add(label); stack.Children.Add(UiKit.Text(row.Value, 15, accent ? UiKit.Res("Accent") : UiKit.Res("TextPrimary"), wrap: true));
        }
        card.Child = stack; return card;
    }

    private UIElement ListBox()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        if (request.ListCaption is { Length: > 0 }) { var caption = UiKit.Text(request.ListCaption, 13, UiKit.Res("TextMuted"), wrap: true); caption.Margin = new Thickness(0, 0, 0, 6); stack.Children.Add(caption); }
        var box = new TextBox { Text = string.Join("\n", request.ListLines), IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, MaxHeight = 170, MinHeight = 60,
            Background = UiKit.Res("Canvas"), BorderBrush = UiKit.Res("Hairline"), BorderThickness = new Thickness(1), Foreground = UiKit.Res("TextPrimary"), FontFamily = new FontFamily("Consolas"), FontSize = 12.5,
            Padding = new Thickness(10, 8, 10, 8), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(box, request.ListCaption ?? "Details");
        stack.Children.Add(box); return stack;
    }

    private UIElement Choices()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        string group = "choice" + GetHashCode();
        for (int i = 0; i < request.Choices.Count; i++) {
            var choice = request.Choices[i];
            var content = new StackPanel();
            content.Children.Add(UiKit.Text(choice.Title, 15, weight: FontWeights.SemiBold));
            var description = UiKit.Text(choice.Description, 13, UiKit.Res("TextMuted"), wrap: true); description.Margin = new Thickness(0, 3, 0, 0); content.Children.Add(description);
            var radio = new RadioButton { Style = (Style)Application.Current.FindResource("ChoiceCard"), GroupName = group, Content = content, IsChecked = i == 0, Margin = new Thickness(0, 0, 0, 10), Tag = choice.Danger ? UiKit.Res("Problem") : UiKit.Res("Accent") };
            System.Windows.Automation.AutomationProperties.SetName(radio, choice.Title); System.Windows.Automation.AutomationProperties.SetHelpText(radio, choice.Description);
            radio.Checked += (_, _) => Refresh();
            choices.Add((radio, choice)); stack.Children.Add(radio);
        }
        return stack;
    }

    private UIElement Items()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        foreach (var item in request.Items) {
            var content = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            double indent = item.Informational ? 0 : 26;
            if (item.Informational) content.Children.Add(UiKit.Text(item.Title, 15, weight: FontWeights.SemiBold, wrap: true));
            else {
                var box = new CheckBox { Content = UiKit.Text(item.Title, 15, weight: FontWeights.SemiBold), IsEnabled = item.Blocked is null, IsChecked = item.Ticked };
                content.Children.Add(box);
                box.Checked += (_, _) => Refresh(); box.Unchecked += (_, _) => Refresh();
                System.Windows.Automation.AutomationProperties.SetName(box, item.Title);
                items.Add((box, item));
            }
            foreach (var line in item.Lines) { var text = UiKit.Text(line, 13, UiKit.Res("TextMuted"), wrap: true); text.Margin = new Thickness(indent, 4, 0, 0); content.Children.Add(text); }
            if (item.Blocked is { } blocked) { var why = UiKit.Text(blocked, 13, UiKit.Res("Review"), wrap: true); why.Margin = new Thickness(indent, 6, 0, 0); content.Children.Add(why); }
            if (item.LinkLabel is { } label && item.Link is { } link) {
                var open = new Button { Style = (Style)Application.Current.FindResource("QuietButton"), Content = label, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(indent - 8, 6, 0, 0), Foreground = UiKit.Res("Accent") };
                open.Click += (_, _) => link(); content.Children.Add(open);
            }
            stack.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Card"), Background = UiKit.Res("Canvas"), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10), Child = content });
        }
        return stack;
    }

    private UIElement Options()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var option in request.Options) {
            var box = new CheckBox { Content = UiKit.Text(option.Title, 14.5, weight: FontWeights.SemiBold), Margin = new Thickness(0, 10, 0, 0) };
            box.Checked += (_, _) => Refresh(); box.Unchecked += (_, _) => Refresh();
            System.Windows.Automation.AutomationProperties.SetName(box, option.Title);
            var description = UiKit.Text(option.Description, 13, UiKit.Res("TextMuted"), wrap: true); description.Margin = new Thickness(26, 3, 0, 0);
            options.Add((box, option)); stack.Children.Add(box); stack.Children.Add(description);
        }
        return stack;
    }

    private UIElement Notes()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        foreach (var note in request.Notes) {
            var (glyph, brush) = note.Kind switch {
                ReviewNoteKind.Undo => ("", UiKit.Res("Good")), ReviewNoteKind.Warning => ("", UiKit.Res("Review")), ReviewNoteKind.Admin => ("", UiKit.Res("Accent")), _ => ("", UiKit.Res("TextMuted"))
            };
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var icon = new TextBlock { Text = glyph, FontFamily = UiKit.IconFont, FontSize = 16, Foreground = brush, Width = 26, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            System.Windows.Automation.AutomationProperties.SetName(icon, note.Kind + " note");
            row.Children.Add(icon); row.Children.Add(UiKit.Text(note.Text, 13.5, UiKit.Res("TextMuted"), wrap: true));
            stack.Children.Add(row);
        }
        return stack;
    }

    private ReviewState Current() => new(choices.FirstOrDefault(c => c.Radio.IsChecked == true).Choice?.Id,
        items.Where(i => i.Box.IsChecked == true && i.Item.Blocked is null).Select(i => i.Item.Id).ToHashSet(),
        options.Where(o => o.Box.IsChecked == true).Select(o => o.Option.Id).ToHashSet(),
        acknowledge.Visibility == Visibility.Visible && acknowledge.IsChecked == true);

    private void Refresh()
    {
        if (confirm is null) return;
        var state = Current();
        var chosen = request.Choices.FirstOrDefault(c => c.Id == state.ChoiceId);
        if (chosen is not null) {
            confirm.Content = chosen.ConfirmLabel;
            confirm.Style = (Style)Application.Current.FindResource(chosen.Danger ? "DangerButton" : "PrimaryButton");
            bool needsAck = chosen.Acknowledge is not null;
            acknowledge.Visibility = needsAck ? Visibility.Visible : Visibility.Collapsed;
            if (needsAck) acknowledge.Content = UiKit.Text(chosen.Acknowledge!, 14);
            else acknowledge.IsChecked = false;
        }
        string? why = request.Validate?.Invoke(state);
        if (why is null && chosen?.Acknowledge is not null && !state.Acknowledged) why = null; // The checkbox itself says what is needed.
        problem.Text = why ?? ""; problem.Visibility = why is null ? Visibility.Collapsed : Visibility.Visible;
        bool ready = why is null && (chosen?.Acknowledge is null || state.Acknowledged) && (request.Choices.Count == 0 || chosen is not null);
        confirm.IsEnabled = ready;
    }

    private void Finish(bool confirmed)
    {
        Result = new ReviewResult(confirmed && confirm.IsEnabled, Current());
        try { DialogResult = Result.Confirmed; } catch (InvalidOperationException) { /* shown without ShowDialog (the UI check) */ }
        Close();
    }
}
