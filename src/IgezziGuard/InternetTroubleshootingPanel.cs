namespace IgezziGuard;

/// <summary>A read-only journey, retained while the user visits other tools. Resolution is user-confirmed.</summary>
internal sealed class InternetTroubleshootingPanel : ToolPage
{
    private readonly HankiButton check, resolved, more;
    private ConnectionCheck? latest;
    private bool showHelp;
    public event Action<string>? OpenRequested;
    protected override bool ReadOnlyTool => true;
    internal InternetTroubleshootingPanel() : base("Check the connection, try one next step, then verify whether your problem is solved.")
    {
        check = Button("Check connection", Check);
        resolved = Button("It works now", Resolve);
        more = Button("More help", () => { showHelp = !showHelp; Present(); });
        resolved.Visible = more.Visible = false;
        ShowSummary(new("", CardStatus.Info, Localizer.T("Let's check your internet connection"), [
            new(Localizer.T("Start with a connection check"), Localizer.T("Hanki checks the connection and suggests one next step. No settings are changed.")),
            new(Localizer.T("Before you start"), Localizer.T("This contacts www.microsoft.com, cloudflare.com, example.com and Cloudflare at 1.1.1.1. Your DNS resolver and test servers can see your IP address. No report is uploaded."))
        ]));
    }
    private async void Check()
    {
        latest = null; showHelp = false; resolved.Visible = more.Visible = false;
        ConnectionCheck? collected = null;
        await Run(async token => { collected = await NetworkDiagnostics.Inspect(token); return collected.Report; });
        if (IsDisposed || collected is null) return;
        latest = collected;
        check.Text = Localizer.T("Check again");
        Present();
    }
    internal void Preview(ConnectionCheck sample) { latest = sample; Output.Text = sample.Report; check.Text = Localizer.T("Check again"); Present(); }
    private void Present()
    {
        if (latest is null) return;
        resolved.Visible = more.Visible = true;
        more.Text = Localizer.T(showHelp ? "Hide extra help" : "More help");
        var next = InternetJourney.Evaluate(latest.Facts);
        var cards = new List<ResultCard> {
            new(Localizer.T("Try this next"), Localizer.T(next.Instruction), next.Status),
            new(Localizer.T("Check whether it helped"), Localizer.T("After trying that step, choose Check again and retry the app or website. Hanki keeps this result when you open another tool."))
        };
        if (showHelp) {
            cards.Add(new(Localizer.T("Still happening?"), Localizer.T("Open the next check, then use Back to return here. Opening a tool does not apply a repair."), CardStatus.Info,
                Localizer.T("Open next check"), () => OpenRequested?.Invoke(next.FollowUpRoute)));
            cards.Add(new(Localizer.T("Read the knowledge base"), Localizer.T("Short checks and explanations on hanki.tools. Opens in your browser; no report is attached."), CardStatus.Info,
                Localizer.T("Open knowledge base"), () => SupportPanel.OpenLink(this, "https://hanki.tools/help/#network")));
        }
        ShowSummary(new(latest.Report, next.Status, Localizer.T(next.Headline), cards));
    }
    private void Resolve()
    {
        resolved.Visible = more.Visible = false;
        ShowSummary(new(latest?.Report ?? "", CardStatus.Good, Localizer.T("You confirmed it works now"), [
            new(Localizer.T("Troubleshooting finished"), Localizer.T("No settings were changed by this guided check. You can check again if the problem returns."), CardStatus.Info)
        ]));
    }
}
