using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBox = System.Windows.Controls.TextBox;

namespace IgezziGuard.Shell;

/// <summary>Small dark dialogs the settings pages share: a list of drop-downs and a one-line question.</summary>
internal static class Dialogs
{
    private static Window Create(string title, double width, double height)
    {
        var window = new Window { Title = title, Width = width, Height = height, MinWidth = 420, Owner = Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = UiKit.Res("Surface"), Foreground = UiKit.Res("TextPrimary"), FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 14, ShowInTaskbar = false, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize };
        DarkTitleBar.Apply(window); return window;
    }

    /// <summary>A list of settings, each with a drop-down. Returns the chosen index per row, or null when cancelled.</summary>
    internal static int[]? Choices(string title, string intro, IReadOnlyList<(string Name, string Now, string[] Choices, int Initial)> rows)
    {
        var window = Create(title, 720, 200); window.MaxHeight = 720; window.SizeToContent = SizeToContent.Manual; window.Height = Math.Min(680, 190 + rows.Count * 62);
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(UiKit.Text(intro, 13.5, UiKit.Res("TextMuted"), wrap: true));
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        var boxes = new List<ComboBox>();
        for (int i = 0; i < rows.Count; i++) {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var (name, now, choices, initial) = rows[i];
            var label = new StackPanel { Margin = new Thickness(0, 6, 12, 6) };
            label.Children.Add(UiKit.Text(name, 14, weight: FontWeights.SemiBold, wrap: true)); label.Children.Add(UiKit.Text("Now: " + now, 12.5, UiKit.Res("TextMuted"), wrap: true));
            var box = new ComboBox { Margin = new Thickness(0, 6, 0, 6), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(box, name);
            foreach (var c in choices) box.Items.Add(c);
            if (box.Items.Count > 0) box.SelectedIndex = Math.Clamp(initial, 0, box.Items.Count - 1);
            Grid.SetRow(label, i); Grid.SetRow(box, i); Grid.SetColumn(box, 1); grid.Children.Add(label); grid.Children.Add(box); boxes.Add(box);
        }
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var ok = Buttons.Primary("Review changes"); var cancel = Buttons.Secondary("Cancel"); cancel.IsCancel = true; ok.IsDefault = true;
        ok.Click += (_, _) => window.DialogResult = true; bar.Children.Add(ok); bar.Children.Add(cancel);
        Grid.SetRow(bar, 2); root.Children.Add(bar); window.Content = root;
        return window.ShowDialog() == true ? boxes.Select(b => Math.Max(0, b.SelectedIndex)).ToArray() : null;
    }

    /// <summary>A one-line text question with OK and Cancel.</summary>
    internal static string? Ask(string title, string prompt, string initial = "")
    {
        var window = Create(title, 480, 200);
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(UiKit.Text(prompt, 14, wrap: true));
        var text = new TextBox { Style = (Style)Application.Current.FindResource("FieldBox"), Text = initial, MaxLength = 200, Margin = new Thickness(0, 10, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(text, prompt); stack.Children.Add(text);
        var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var ok = Buttons.Primary("OK"); var cancel = Buttons.Secondary("Cancel"); ok.IsDefault = true; cancel.IsCancel = true; ok.Click += (_, _) => window.DialogResult = true;
        bar.Children.Add(ok); bar.Children.Add(cancel); stack.Children.Add(bar); window.Content = stack; window.Loaded += (_, _) => { text.Focus(); text.SelectAll(); };
        return window.ShowDialog() == true ? text.Text : null;
    }

    /// <summary>"Keep this display mode?" with a 15-second automatic revert, as Windows does.</summary>
    internal static bool KeepDisplayMode(string mode)
    {
        var window = Create("Keep this display mode?", 480, 200); window.Topmost = true;
        int seconds = 15;
        var stack = new StackPanel { Margin = new Thickness(20) };
        string Message() => $"The display now uses {mode}.\nIf the picture looks wrong, do nothing: it switches back in {seconds} seconds.";
        var text = UiKit.Text(Message(), 14, wrap: true); stack.Children.Add(text);
        var bar = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var keep = Buttons.Primary("Keep changes"); var revert = Buttons.Secondary("Revert"); keep.IsDefault = true; revert.IsCancel = true; keep.Click += (_, _) => window.DialogResult = true;
        bar.Children.Add(keep); bar.Children.Add(revert); stack.Children.Add(bar); window.Content = stack;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { if (--seconds <= 0) { timer.Stop(); window.DialogResult = false; return; } text.Text = Message(); };
        window.Loaded += (_, _) => timer.Start(); window.Closed += (_, _) => timer.Stop();
        return window.ShowDialog() == true;
    }

    /// <summary>Shows exactly what will be sent and a consent box; true only when it was ticked and Send was chosen.</summary>
    internal static bool ReviewSend(string title, string text, string consent, string sendLabel)
    {
        var window = Create(title, 900, 650); window.SizeToContent = SizeToContent.Manual; window.ResizeMode = ResizeMode.CanResize; window.MinHeight = 420;
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var box = new TextBox { Style = (Style)Application.Current.FindResource("FieldBox"), Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13 };
        System.Windows.Automation.AutomationProperties.SetName(box, "Exactly what will be sent");
        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(bottom, 1);
        var agree = new System.Windows.Controls.CheckBox { Content = consent, Margin = new Thickness(0, 0, 0, 10) };
        var bar = new WrapPanel();
        var send = Buttons.Primary(sendLabel); send.IsEnabled = false; var cancel = Buttons.Secondary("Cancel"); cancel.IsCancel = true;
        agree.Checked += (_, _) => send.IsEnabled = true; agree.Unchecked += (_, _) => send.IsEnabled = false; send.Click += (_, _) => window.DialogResult = true;
        bar.Children.Add(send); bar.Children.Add(cancel); bottom.Children.Add(agree); bottom.Children.Add(bar);
        root.Children.Add(box); root.Children.Add(bottom); window.Content = root;
        return window.ShowDialog() == true && agree.IsChecked == true;
    }

    /// <summary>Tactical Vision for one game: off, or a Digital Vibrance strength from 51 to 100. Null when cancelled.</summary>
    internal static int? TacticalVision(GameEntry game)
    {
        var window = Create("Tactical Vision · " + game.Name, 560, 200);
        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(UiKit.Text("Boost color saturation while this game is focused. NVIDIA Digital Vibrance affects the entire display containing the game. Your previous colors return when you switch away, exit the game, or close Hanki.\n\nKeep Hanki open. Requires an SDR display connected directly to NVIDIA; HDR is not supported. This changes color, not FPS.", 13.5, UiKit.Res("TextMuted"), wrap: true));
        var enabled = new System.Windows.Controls.CheckBox { Content = Localizer.T("Enable Tactical Vision for this game"), IsChecked = game.TacticalVision > 0, Margin = new Thickness(0, 16, 0, 12) };
        var label = UiKit.Text("Digital Vibrance % (50 = neutral, 100 = maximum)", 13.5);
        var box = new TextBox { Style = (Style)Application.Current.FindResource("FieldBox"), Width = 100, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), MaxLength = 3,
            Text = (game.TacticalVision is >= 51 and <= 100 ? game.TacticalVision : 70).ToString(), IsEnabled = game.TacticalVision > 0 };
        System.Windows.Automation.AutomationProperties.SetName(box, "Digital Vibrance percentage");
        enabled.Click += (_, _) => box.IsEnabled = enabled.IsChecked == true;
        var error = UiKit.Text("", 13, UiKit.Res("Problem"), wrap: true); error.Margin = new Thickness(0, 8, 0, 0);
        var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var save = Buttons.Primary("Save"); var cancel = Buttons.Secondary("Cancel"); save.IsDefault = true; cancel.IsCancel = true;
        save.Click += (_, _) => {
            if (enabled.IsChecked == true && !(int.TryParse(box.Text, out var v) && v is >= 51 and <= 100)) { error.Text = "Enter a whole number from 51 to 100."; return; }
            window.DialogResult = true;
        };
        bar.Children.Add(save); bar.Children.Add(cancel);
        foreach (var item in new UIElement[] { enabled, label, box, error, bar }) stack.Children.Add(item);
        window.Content = stack;
        return window.ShowDialog() == true ? (enabled.IsChecked == true ? int.Parse(box.Text) : 0) : null;
    }
}
