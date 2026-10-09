using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace IgezziGuard.Shell;

/// <summary>Defender audit: Microsoft Defender's protection status and configured exclusions, read with built-in PowerShell. Nothing changes and nothing elevates.</summary>
internal sealed class DefenderAuditView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly Button audit = Buttons.Primary("Audit Defender settings"), cancel = Buttons.Secondary("Cancel"), assistant = Buttons.Secondary("Prepare for Assistant");
    private readonly TextBlock status = UiKit.Text("Ready when you are", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly DiagnosisView view = new();
    private CancellationTokenSource? running;

    internal DiagnosisView Result => view;

    internal DefenderAuditView(IShellServices shell)
    {
        this.shell = shell;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
        root.Children.Add(UiKit.Text("Reads Defender status and configured exclusions using built-in PowerShell. No changes or auto-elevation. Exclusion paths may contain private data.", 14, UiKit.Res("TextMuted"), wrap: true));
        var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 4) }; bar.Children.Add(audit); bar.Children.Add(cancel); bar.Children.Add(assistant);
        root.Children.Add(bar); root.Children.Add(status); view.Margin = new Thickness(0, 6, 0, 0); root.Children.Add(view);
        Content = root; cancel.IsEnabled = false; assistant.IsEnabled = false;
        audit.Click += async (_, _) => await Run();
        cancel.Click += (_, _) => running?.Cancel();
        assistant.Click += (_, _) => { if (view.ReportText.Length > 0) shell.PrepareForAssistant(view.ReportText); };
    }

    internal Task RunAsync() => Run();

    private async Task Run()
    {
        if (running is not null) return;
        using var cts = new CancellationTokenSource(); running = cts; audit.IsEnabled = false; cancel.IsEnabled = true; assistant.IsEnabled = false;
        using var ticket = shell.Tasks.Begin("Defender", cts.Cancel);
        status.Text = "Collecting results. Your previous report remains available after this operation."; view.Clear();
        try {
            var diagnosis = await Task.Run(async () => ResultPresentation.DefenderDiagnosis(await ReadOnlyDiagnostics.Defender(cts.Token)), cts.Token);
            view.Show(diagnosis); status.Text = "Report ready · " + DateTime.Now.ToString("t"); assistant.IsEnabled = true;
        }
        catch (OperationCanceledException) { status.Text = "Cancelled. Nothing on your PC was changed; incomplete results were discarded. Run it again when you're ready."; }
        catch (Exception ex) { status.Text = "Operation stopped: " + ex.Message + "\r\nFor a setting change, inspect Recovery before retrying. Access denied may require running Hanki as administrator; Hanki does not auto-elevate."; }
        finally { running = null; audit.IsEnabled = true; cancel.IsEnabled = false; }
    }
}
