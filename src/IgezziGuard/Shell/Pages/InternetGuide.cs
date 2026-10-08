using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace IgezziGuard.Shell;

/// <summary>
/// Connect → Guided troubleshooting: check the connection, try one suggested step, then confirm whether the problem is solved.
/// Read-only; the result stays while you open another tool and come back. Only you can mark it resolved.
/// </summary>
internal sealed class InternetGuideView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly Button check = Buttons.Primary(Localizer.T("Check connection")), resolved = Buttons.Secondary(Localizer.T("It works now")), more = Buttons.Quiet(Localizer.T("More help")), cancel = Buttons.Secondary(Localizer.T("Cancel"));
    private readonly TextBlock status = UiKit.Text("", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly DiagnosisView view = new();
    private ConnectionCheck? latest;
    private bool showHelp;
    private CancellationTokenSource? running;
    internal DiagnosisView Result => view;
    internal bool Resolved { get; private set; }

    internal InternetGuideView(IShellServices shell)
    {
        this.shell = shell; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        root.Children.Add(UiKit.Text(Localizer.T("Check the connection, try one next step, then verify whether your problem is solved."), 14, UiKit.Res("TextMuted"), wrap: true));
        var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 4) };
        cancel.IsEnabled = false; resolved.Visibility = more.Visibility = Visibility.Collapsed;
        bar.Children.Add(check); bar.Children.Add(cancel); bar.Children.Add(resolved); bar.Children.Add(more);
        root.Children.Add(bar); root.Children.Add(status); view.Margin = new Thickness(0, 6, 0, 0); root.Children.Add(view); Content = root;
        check.Click += async (_, _) => await Check(); cancel.Click += (_, _) => running?.Cancel();
        resolved.Click += (_, _) => Resolve(); more.Click += (_, _) => { showHelp = !showHelp; Present(); };
        view.Show(new Diagnosis("", CardStatus.Info, Localizer.T("Let's check your internet connection"), [
            new(Localizer.T("Start with a connection check"), Localizer.T("Hanki checks the connection and suggests one next step. No settings are changed.")),
            new(Localizer.T("Before you start"), Localizer.T("This contacts www.microsoft.com, cloudflare.com, example.com and Cloudflare at 1.1.1.1. Your DNS resolver and test servers can see your IP address. No report is uploaded."))
        ]));
    }

    private async Task Check()
    {
        if (running is not null) return;
        latest = null; showHelp = false; Resolved = false; resolved.Visibility = more.Visibility = Visibility.Collapsed;
        using var cts = new CancellationTokenSource(); running = cts; check.IsEnabled = false; cancel.IsEnabled = true;
        using var ticket = shell.Tasks.Begin("Connect", cts.Cancel);
        status.Text = "Collecting results. Your previous report remains available after this operation.";
        try {
            var collected = await Task.Run(() => NetworkDiagnostics.Inspect(cts.Token), cts.Token);
            latest = collected; check.Content = Localizer.T("Check again"); status.Text = "Report ready · " + DateTime.Now.ToString("t"); Present();
        }
        catch (OperationCanceledException) { status.Text = "Cancelled. Nothing on your PC was changed; incomplete results were discarded. Run it again when you're ready."; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { status.Text = "Operation stopped: " + ex.Message; }
        finally { running = null; check.IsEnabled = true; cancel.IsEnabled = false; }
    }

    internal void Preview(ConnectionCheck sample) { latest = sample; check.Content = Localizer.T("Check again"); Present(); }

    private void Present()
    {
        if (latest is null) return;
        resolved.Visibility = more.Visibility = Visibility.Visible;
        more.Content = Localizer.T(showHelp ? "Hide extra help" : "More help");
        var next = InternetJourney.Evaluate(latest.Facts);
        var cards = new List<ResultCard> {
            new(Localizer.T("Try this next"), Localizer.T(next.Instruction), next.Status),
            new(Localizer.T("Check whether it helped"), Localizer.T("After trying that step, choose Check again and retry the app or website. Hanki keeps this result when you open another tool."))
        };
        if (showHelp) {
            cards.Add(new(Localizer.T("Still happening?"), Localizer.T("Open the next check, then use Back to return here. Opening a tool does not apply a repair."), CardStatus.Info,
                Localizer.T("Open next check"), () => shell.Routes.FirstOrDefault(r => r.Name == next.FollowUpRoute)?.Open()));
            cards.Add(new(Localizer.T("Read the knowledge base"), Localizer.T("Short checks and explanations on hanki.tools. Opens in your browser; no report is attached."), CardStatus.Info,
                Localizer.T("Open knowledge base"), () => Blocks.OpenLink("https://hanki.tools/help/#network")));
        }
        view.Show(new Diagnosis(latest.Report, next.Status, Localizer.T(next.Headline), cards));
    }

    private void Resolve()
    {
        Resolved = true; resolved.Visibility = more.Visibility = Visibility.Collapsed;
        view.Show(new Diagnosis(latest?.Report ?? "", CardStatus.Good, Localizer.T("You confirmed it works now"), [
            new(Localizer.T("Troubleshooting finished"), Localizer.T("No settings were changed by this guided check. You can check again if the problem returns."), CardStatus.Info)
        ]));
    }
}
