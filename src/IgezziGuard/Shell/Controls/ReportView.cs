using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace IgezziGuard.Shell;

/// <summary>One button of a <see cref="ReportView"/>: it runs a check (plain-language result or text) or just opens something. Checks run on the UI thread and move heavy work to a background thread themselves.</summary>
internal sealed record ReportAction(string Label, Func<ReportView, CancellationToken, Task<Diagnosis>>? Diagnose = null, Func<ReportView, CancellationToken, Task<string>>? Text = null, Action? Open = null, bool Primary = false);

/// <summary>A page of read-only checks: an introduction, buttons, then the result as a headline with cards and the technical report one click away.</summary>
internal sealed class ReportView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly string taskName;
    private readonly WrapPanel bar = new() { Margin = new Thickness(0, 14, 0, 4) };
    private readonly List<Button> buttons = [];
    private readonly Button cancel = Buttons.Secondary("Cancel");
    private readonly TextBlock status = UiKit.Text("", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly DiagnosisView view = new();
    private CancellationTokenSource? running;

    internal DiagnosisView Result => view;
    internal bool IsBusy => running is not null;
    internal string StatusText => status.Text;

    internal ReportView(IShellServices shell, string taskName, string intro, IReadOnlyList<ReportAction> actions)
    {
        this.shell = shell; this.taskName = taskName;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        root.Children.Add(UiKit.Text(intro, 14, UiKit.Res("TextMuted"), wrap: true));
        foreach (var action in actions) {
            var button = action.Primary ? Buttons.Primary(action.Label) : Buttons.Secondary(action.Label);
            button.Click += async (_, _) => await Run(action);
            buttons.Add(button); bar.Children.Add(button);
        }
        cancel.IsEnabled = false; cancel.Click += (_, _) => running?.Cancel(); bar.Children.Add(cancel);
        root.Children.Add(bar); root.Children.Add(status); view.Margin = new Thickness(0, 6, 0, 0); root.Children.Add(view);
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
        status.Text = "Collecting results. Your previous report remains available after this operation."; view.Clear();
        try {
            if (action.Diagnose is { } diagnose) { view.Show(await diagnose(this, cts.Token)); status.Text = "Report ready · " + DateTime.Now.ToString("t"); }
            else if (action.Text is { } text) status.Text = await text(this, cts.Token);
        }
        catch (OperationCanceledException) { status.Text = "Cancelled. Nothing on your PC was changed; incomplete results were discarded. Run it again when you're ready."; }
        catch (Exception ex) { status.Text = "Operation stopped: " + ex.Message; }
        finally { running = null; foreach (var b in buttons) b.IsEnabled = true; cancel.IsEnabled = false; }
    }
}
