using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace IgezziGuard.Shell;

/// <summary>Recovery: every change Hanki made, newest first, each with a way back that is reviewed before it runs.</summary>
internal sealed class RecoveryPage : NativePage
{
    private readonly StackPanel list = new();
    private readonly TextBlock message = UiKit.Text("", 14, UiKit.Res("TextPrimary"), wrap: true);
    private readonly Border messageCard = new() { Visibility = Visibility.Collapsed };
    private readonly Border emptyCard = new();
    private readonly Border listCard = new() { Visibility = Visibility.Collapsed };
    private readonly Button refresh = new();
    private bool busy;
    private List<SettingChange> records = [];

    internal bool HasRows => list.Children.Count > 0;
    internal int RowCount => records.Count;

    internal RecoveryPage(IShellServices shell)
    {
        var root = new StackPanel { Margin = new Thickness(24, 4, 24, 28) };
        root.Children.Add(UiKit.Text("Recovery covers power plans, IPv4 DNS, startup-folder files and the game and graphics settings Hanki changed. Current state must match a saved state before undo. Registry startup changes are undone in Maintain → Startup entries. Recycled files are restored through the Windows Recycle Bin; there is no automatic file rollback.", 14, UiKit.Res("TextMuted"), wrap: true));
        refresh.Style = (Style)Application.Current.FindResource("SecondaryButton"); refresh.Content = "Refresh"; refresh.HorizontalAlignment = HorizontalAlignment.Left; refresh.Margin = new Thickness(0, 14, 0, 14);
        refresh.Click += (_, _) => Reload();
        root.Children.Add(refresh);

        messageCard.Style = (Style)Application.Current.FindResource("Card"); messageCard.Padding = new Thickness(18, 12, 18, 12); messageCard.Margin = new Thickness(0, 0, 0, 14); messageCard.Child = message;
        root.Children.Add(messageCard);

        emptyCard.Style = (Style)Application.Current.FindResource("Card"); emptyCard.Padding = new Thickness(24);
        var emptyStack = new StackPanel();
        emptyStack.Children.Add(UiKit.Text("No changes yet", 19, weight: FontWeights.SemiBold));
        var emptyText = UiKit.Text("When Hanki changes a setting for you, it is saved here with what it was before, so you can put it back. Nothing has been changed so far.", 14, UiKit.Res("TextMuted"), wrap: true); emptyText.Margin = new Thickness(0, 6, 0, 0);
        emptyStack.Children.Add(emptyText); emptyCard.Child = emptyStack; root.Children.Add(emptyCard);

        listCard.Style = (Style)Application.Current.FindResource("Card"); listCard.Child = list; root.Children.Add(listCard);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
    }

    internal override void OnShown() => Reload();

    private static string IconFor(SettingChange change) => change.Kind switch {
        "Power plan" => "CPU", "IPv4 DNS" => "Connect", "Startup file" => "Maintain",
        "Windows gaming setting" or "NVIDIA global setting" or "NVIDIA setting" or "AMD setting" or "Display mode" or "GPU preference" => "Gaming",
        _ => "Recovery"
    };

    private void Say(string text) { message.Text = text; messageCard.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }

    private void Reload()
    {
        try { records = WindowsSettings.Journal().Read().OrderByDescending(e => e.At).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) {
            records = []; Say("Recovery history couldn't be read: " + ex.Message + " Original files were not changed.");
        }
        Render();
    }

    private void Render()
    {
        list.Children.Clear();
        emptyCard.Visibility = records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        listCard.Visibility = records.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        for (int i = 0; i < records.Count; i++) {
            list.Children.Add(Row(records[i]));
            if (i < records.Count - 1) list.Children.Add(new Border { Height = 1, Background = UiKit.Res("Hairline") });
        }
    }

    private UIElement Row(SettingChange change)
    {
        bool open = RecoveryText.CanUndo(change);
        var accent = open ? UiKit.Res("Accent") : UiKit.Res("TextMuted");
        var grid = new Grid { Margin = new Thickness(18, 14, 18, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = UiKit.Tint(accent, 36), Child = UiKit.Icon(IconFor(change), 18, accent), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        var text = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        string subject = RecoveryText.Subject(change);
        text.Children.Add(UiKit.Text(RecoveryText.Title(change) + (subject.Length > 0 ? " · " + subject : ""), 15.5, weight: FontWeights.SemiBold, wrap: true));
        text.Children.Add(UiKit.Text(RecoveryText.Value(change.Kind, change.Before) + "  →  " + RecoveryText.Value(change.Kind, change.After), 13.5, UiKit.Res("TextPrimary"), wrap: true));
        text.Children.Add(UiKit.Text(UiKit.When(change.At), 12.5, UiKit.Res("TextMuted")));
        Grid.SetColumn(text, 1);
        var side = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(side, 2);
        var status = UiKit.Text(RecoveryText.StatusLabel(change), 12.5, open ? UiKit.Res("Good") : UiKit.Res("TextMuted"), FontWeights.SemiBold); status.HorizontalAlignment = HorizontalAlignment.Right; status.Margin = new Thickness(0, 0, 0, 6);
        side.Children.Add(status);
        if (open) {
            var undo = new Button { Style = (Style)Application.Current.FindResource("SecondaryButton"), Content = "Put back", Padding = new Thickness(14, 6, 14, 6), IsEnabled = !busy };
            System.Windows.Automation.AutomationProperties.SetName(undo, "Put back " + RecoveryText.Title(change) + (subject.Length > 0 ? " for " + subject : ""));
            undo.Click += async (_, _) => await UndoAsync(change);
            side.Children.Add(undo);
        }
        grid.Children.Add(tile); grid.Children.Add(text); grid.Children.Add(side);
        return grid;
    }

    private async Task UndoAsync(SettingChange change)
    {
        if (busy) return;
        if (!ReviewPresenter.Ask(RecoveryText.UndoReview(change)).Confirmed) return;
        busy = true; Say("Putting it back…"); Render();
        try {
            await Task.Run(() => WindowsSettings.Journal().Undo(change.Id, CancellationToken.None));
            Say("Put back and checked: " + RecoveryText.Title(change) + " is " + RecoveryText.Value(change.Kind, change.Before) + " again.");
        } catch (Exception ex) {
            Say("Couldn't put it back: " + ex.Message + "\nNothing was overwritten. Check the setting in Windows, then refresh.");
        } finally { busy = false; Reload(); }
    }
}
