using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace IgezziGuard.Shell;

/// <summary>One button of a <see cref="ReportView"/>: it runs a check (plain-language result or text) or just opens something. Checks run on the UI thread and move heavy work to a background thread themselves.</summary>
internal sealed record ReportAction(string Label, Func<ReportView, CancellationToken, Task<Diagnosis>>? Diagnose = null, Func<ReportView, CancellationToken, Task<string>>? Text = null, Action? Open = null, bool Primary = false, Func<ReportView, CancellationToken, Task<string>>? Output = null);

/// <summary>A page of read-only checks: an introduction, buttons, then the result as a headline with cards and the technical report one click away.</summary>
internal sealed class ReportView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly string taskName;
    private readonly WrapPanel bar = new() { Margin = new Thickness(0, 14, 0, 4) };
    private readonly List<Button> buttons = [];
    private readonly Button cancel = Buttons.Secondary("Cancel"), assistant = Buttons.Quiet("Prepare for Assistant");
    private readonly TextBlock status = UiKit.Text("", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly DiagnosisView view = new();
    private CancellationTokenSource? running;

    private readonly System.Windows.Controls.TextBox plain = Blocks.Output(620);
    private readonly StackPanel root = new() { Margin = new Thickness(24, 0, 24, 28) };
    /// <summary>Puts a note under the introduction, above the buttons.</summary>
    internal void InsertNote(UIElement element) => root.Children.Insert(1, element);
    internal DiagnosisView Result => view;
    internal string PlainText => plain.Text;

    /// <summary>Puts an extra control (a text field, a choice) on the button row; <paramref name="first"/> places it before the buttons.</summary>
    internal void AddToBar(UIElement element, bool first = false) { if (first) bar.Children.Insert(0, element); else bar.Children.Insert(bar.Children.Count - 2, element); }

    /// <summary>Shows plain text in the output box, for the tools whose result is a log rather than a diagnosis.</summary>
    internal void ShowPlain(string text) { plain.Text = text; plain.Visibility = Visibility.Visible; }
    internal bool IsBusy => running is not null;
    internal string StatusText => status.Text;

    internal ReportView(IShellServices shell, string taskName, string intro, IReadOnlyList<ReportAction> actions)
    {
        this.shell = shell; this.taskName = taskName;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        root.Children.Add(UiKit.Text(intro, 14, UiKit.Res("TextMuted"), wrap: true));
        foreach (var action in actions) {
            var button = action.Primary ? Buttons.Primary(action.Label) : Buttons.Secondary(action.Label);
            button.Click += async (_, _) => await Run(action);
            buttons.Add(button); bar.Children.Add(button);
        }
        cancel.IsEnabled = false; cancel.Click += (_, _) => running?.Cancel(); bar.Children.Add(cancel);
        assistant.IsEnabled = false; assistant.Click += (_, _) => { var report = view.ReportText.Length > 0 ? view.ReportText : plain.Text; if (report.Length > 0) shell.PrepareForAssistant(report); }; bar.Children.Add(assistant);
        root.Children.Add(bar); root.Children.Add(status); view.Margin = new Thickness(0, 6, 0, 0); root.Children.Add(view); plain.Visibility = Visibility.Collapsed; root.Children.Add(plain);
        Content = root;
    }

    /// <summary>Shows a message in place of a result (used by actions that finish without one).</summary>
    internal void Say(string text) => status.Text = text;

    /// <summary>Runs an action by its label (the UI check does this instead of clicking).</summary>
    internal Task RunAsync(string label, IReadOnlyList<ReportAction> actions) => Run(actions.First(a => a.Label == label));

    private async Task Run(ReportAction action)
    {
        if (action.Open is { } open) { open(); return; }
        if (running is not null) return;
        using var cts = new CancellationTokenSource(); running = cts;
        foreach (var b in buttons) b.IsEnabled = false; cancel.IsEnabled = true;
        using var ticket = shell.Tasks.Begin(taskName, cts.Cancel);
        status.Text = "Collecting results. Your previous report remains available after this operation."; view.Clear(); plain.Visibility = Visibility.Collapsed; assistant.IsEnabled = false;
        try {
            if (action.Diagnose is { } diagnose) { view.Show(await diagnose(this, cts.Token)); status.Text = "Report ready · " + DateTime.Now.ToString("t"); assistant.IsEnabled = view.ReportText.Length > 0; }
            else if (action.Output is { } output) { ShowPlain(await output(this, cts.Token)); status.Text = "Done · " + DateTime.Now.ToString("t"); assistant.IsEnabled = true; }
            else if (action.Text is { } text) status.Text = await text(this, cts.Token);
        }
        catch (OperationCanceledException) { status.Text = "Cancelled. Nothing on your PC was changed; incomplete results were discarded. Run it again when you're ready."; }
        catch (Exception ex) { status.Text = "Operation stopped: " + ex.Message; }
        finally { running = null; foreach (var b in buttons) b.IsEnabled = true; cancel.IsEnabled = false; }
    }
}
