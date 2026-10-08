using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using ComboBox = System.Windows.Controls.ComboBox;

namespace IgezziGuard.Shell;

/// <summary>Choose the app language (or follow Windows). It applies the next time Hanki opens, so a running scan or repair is never interrupted.</summary>
internal static class LanguagePicker
{
    /// <summary>The choices in list order: "Use Windows language" first, then every language by its own name.</summary>
    internal static IReadOnlyList<Localizer.Language> Choices() => [new Localizer.Language("", Localizer.T("Use Windows language")), .. Localizer.Languages];

    internal static void Show()
    {
        var window = new Window { Title = Localizer.T("Language"), Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, Owner = Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = UiKit.Res("Surface"), Foreground = UiKit.Res("TextPrimary"), FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 14, ShowInTaskbar = false };
        DarkTitleBar.Apply(window);
        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(UiKit.Text("App language", 14));
        var box = new ComboBox { Margin = new Thickness(0, 10, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(box, Localizer.T("App language"));
        var choices = Choices(); string? saved = Localizer.ReadSavedLanguage(Localizer.PreferencePath);
        foreach (var choice in choices) box.Items.Add(choice.NativeName);
        box.SelectedIndex = Math.Max(0, choices.ToList().FindIndex(c => c.Code.Length > 0 && c.Code == saved));
        stack.Children.Add(box);
        var hint = UiKit.Text("The language will change the next time you open Hanki Tools.", 13.5, UiKit.Res("TextMuted"), wrap: true); hint.Margin = new Thickness(0, 16, 0, 16); stack.Children.Add(hint);
        var bar = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var save = Buttons.Primary("Save"); var cancel = Buttons.Secondary("Cancel"); save.IsDefault = true; cancel.IsCancel = true;
        save.Click += (_, _) => {
            try {
                string code = choices[Math.Max(0, box.SelectedIndex)].Code;
                Localizer.SavePreference(Localizer.PreferencePath, code.Length == 0 ? null : code);
                window.DialogResult = true;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Blocks.Notice(Localizer.T("The language preference could not be saved."), ex.Message); }
        };
        bar.Children.Add(save); bar.Children.Add(cancel); stack.Children.Add(bar); window.Content = stack;
        window.ShowDialog();
    }
}
