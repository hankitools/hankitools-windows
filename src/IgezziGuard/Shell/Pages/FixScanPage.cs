using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace IgezziGuard.Shell;

/// <summary>Full scan: start or cancel, live progress, the ranked findings with their explanations, repairs and reports.</summary>
internal sealed class FixScanPage : NativePage
{
    private readonly IShellServices shell;
    private readonly FullScanController scan;
    private readonly Button start = new(), cancel = new(), repairs = new(), recovery = new(), copy = new(), assistant = new(), customer = new();
    private readonly CheckBox probes = new() { Content = "Include network probes", Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border progressCard = new() { Visibility = Visibility.Collapsed };
    private readonly ProgressBar bar = new() { Height = 6, Minimum = 0, Maximum = 1 };
    private readonly TextBlock progressText = UiKit.Text("", 14), activityText = UiKit.Text("", 13, UiKit.Res("TextMuted"), wrap: true);
    private readonly DiagnosisView guided = new();
    private readonly Button backToResults = new();
    private bool showOther, showGaps;
    private readonly Border emptyCard = new();
    private readonly TextBox output = new();
    private readonly Border outputCard = new() { Visibility = Visibility.Collapsed };
    private DiagnosticScan? shown;

    internal FixScanPage(IShellServices shell)
    {
        this.shell = shell; scan = shell.Scan;
        var root = new StackPanel { Margin = new Thickness(24, 4, 24, 28) };
        root.Children.Add(UiKit.Text("A local, read-only review of Windows, storage, devices, security and performance. Some checks require administrator access and may take several minutes. Unavailable checks stay unknown. No repairs, uploads or automatic elevation. Existing tools remain available individually.", 14, UiKit.Res("TextMuted"), wrap: true));
        root.Children.Add(BuildActions());
        if (!scan.IsAdministrator) {
            var hint = UiKit.Text("DISM and SFC checks need administrator rights. To include them, close Hanki and run it as administrator.", 13.5, UiKit.Res("TextMuted"), wrap: true); hint.Margin = new Thickness(2, 0, 0, 12); root.Children.Add(hint);
        }
        progressCard.Style = (Style)Application.Current.FindResource("Card"); progressCard.Padding = new Thickness(20, 16, 20, 16); progressCard.Margin = new Thickness(0, 0, 0, 14);
        var progressStack = new StackPanel(); progressStack.Children.Add(progressText); bar.Margin = new Thickness(0, 10, 0, 10); progressStack.Children.Add(bar); progressStack.Children.Add(activityText);
        progressCard.Child = progressStack; root.Children.Add(progressCard);

        emptyCard.Style = (Style)Application.Current.FindResource("Card"); emptyCard.Padding = new Thickness(24);
        var emptyStack = new StackPanel();
        emptyStack.Children.Add(UiKit.Text("No scan yet", 19, weight: FontWeights.SemiBold));
        var emptyText = UiKit.Text("Start a full scan to see what Windows, storage, devices and security report. Results appear here, ranked by what to look at first.", 14, UiKit.Res("TextMuted"), wrap: true); emptyText.Margin = new Thickness(0, 6, 0, 0);
        emptyStack.Children.Add(emptyText); emptyCard.Child = emptyStack; root.Children.Add(emptyCard);

        backToResults.Style = (Style)Application.Current.FindResource("QuietButton"); backToResults.Content = Localizer.T("Back to scan results"); backToResults.HorizontalAlignment = HorizontalAlignment.Left; backToResults.Visibility = Visibility.Collapsed;
        backToResults.Click += (_, _) => { RenderResults(); };
        root.Children.Add(backToResults); root.Children.Add(guided);

        outputCard.Style = (Style)Application.Current.FindResource("Card"); outputCard.Padding = new Thickness(20, 14, 20, 14); outputCard.Margin = new Thickness(0, 14, 0, 0);
        output.IsReadOnly = true; output.TextWrapping = TextWrapping.Wrap; output.Background = System.Windows.Media.Brushes.Transparent; output.BorderThickness = new Thickness(0); output.Foreground = UiKit.Res("TextPrimary"); output.FontSize = 13.5;
        output.MaxHeight = 360; output.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; System.Windows.Automation.AutomationProperties.SetName(output, "Scan report");
        outputCard.Child = output; root.Children.Add(outputCard);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        scan.Changed += () => Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    private StackPanel BuildActions()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 16, 0, 16) };
        void Set(Button b, string text, bool primary, Action click) {
            b.Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "SecondaryButton"); b.Content = text; b.Margin = new Thickness(0, 0, 8, 8); b.Click += (_, _) => click(); row.Children.Add(b);
        }
        Set(start, "Start full scan", true, () => _ = StartAsync());
        Set(cancel, "Cancel scan", false, scan.Cancel); cancel.Visibility = Visibility.Collapsed;
        row.Children.Add(probes);
        Set(repairs, Localizer.T("Review repairs"), false, () => _ = scan.ReviewRepairsAsync(shell.DialogOwner));
        Set(recovery, Localizer.T("Recovery"), false, () => OpenRoute("Recovery"));
        Set(copy, "Copy summary", false, () => { if (scan.Latest is { } s) { try { Clipboard.SetText(FullScanPanel.Summary(s)); } catch (System.Runtime.InteropServices.ExternalException) { } } });
        Set(assistant, "Prepare for Assistant", false, () => { if (scan.Latest is { } s) shell.PrepareForAssistant(FullScanPanel.Summary(s)); });
        // Technician only; the licence can change while Hanki is open, so visibility follows it.
        Set(customer, "Customer report", false, () => {
            if (scan.Latest is not { } s) { Say("Run a full scan first. To report on an earlier scan, open System actions → Saved scans."); return; }
            if (CustomerReportFlow.Create(shell.DialogOwner, s, scan.Repairs) is { } status) Say(status);
        });
        var host = new StackPanel(); host.Children.Add(row); return host;
    }

    private void Say(string text) { output.Text = text; outputCard.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }

    private async Task StartAsync()
    {
        await scan.StartAsync(probes.IsChecked == true, () => ReviewPresenter.Ask(
            "Include network probes? Gateway ICMP contacts your local network. " + WindowsDiagnosticCatalog.ProbeDisclosure + " Installed KMS clients may also query your organization DNS and contact the Windows-configured KMS host. These endpoints and your DNS resolver can see your source IP. No report is uploaded. You can run without these checks by clearing Include network probes."));
    }

    internal bool CanStart => start.IsEnabled;
    internal int ResultCardCount => guided.CardCount;
    internal bool RepairsOffered => repairs.Visibility == Visibility.Visible;
    internal bool InDetail => backToResults.Visibility == Visibility.Visible;
    internal string Report => guided.ReportText;
    internal void ToggleOther() { showOther = !showOther; RenderResults(); }
    internal void ToggleGaps() { showGaps = !showGaps; RenderResults(); }
    internal void ShowDetail(DiagnosticResult finding) => RenderFinding(finding);
    internal void ShowResults() => RenderResults();
    internal override void OnShown() { customer.Visibility = scan.CanReportForCustomers ? Visibility.Visible : Visibility.Collapsed; Refresh(); }

    private void Refresh()
    {
        bool busy = scan.IsBusy;
        start.IsEnabled = !busy; repairs.IsEnabled = !busy; repairs.Visibility = scan.CanOfferRepairs ? Visibility.Visible : Visibility.Collapsed; copy.IsEnabled = !busy && scan.Latest is not null; assistant.IsEnabled = !busy && scan.Latest is not null;
        customer.Visibility = scan.CanReportForCustomers ? Visibility.Visible : Visibility.Collapsed; customer.IsEnabled = !busy;
        probes.IsEnabled = !busy; cancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        progressCard.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) {
            if (scan.Progress is { } p && p.Total > 0) { bar.IsIndeterminate = false; bar.Maximum = p.Total; bar.Value = p.Done; progressText.Text = $"{p.Done}/{p.Total} checks finished"; }
            else { bar.IsIndeterminate = true; progressText.Text = "Starting the scan…"; }
            activityText.Text = (scan.Activity.Length > 0 ? scan.Activity + "\n" : "") + "Unavailable checks remain unknown. Cancel preserves results from completed checks.";
        }
        // The report text belongs to the last operation; while a scan runs, the progress card replaces it.
        outputCard.Visibility = !busy && scan.Message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!busy) output.Text = scan.Message;
        var latest = scan.Latest;
        if (!ReferenceEquals(latest, shown)) { shown = latest; showOther = showGaps = false; if (latest is null) { guided.Clear(); backToResults.Visibility = Visibility.Collapsed; } else RenderResults(); }
        emptyCard.Visibility = latest is null && !busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenRoute(string name) => shell.Routes.FirstOrDefault(r => r.Name == name)?.Open();

    /// <summary>The scan as a short list: what needs attention first, then incomplete checks and the ones that need no action, each expandable.</summary>
    private void RenderResults()
    {
        backToResults.Visibility = Visibility.Collapsed;
        if (shown is not { } latest) return;
        var cards = new List<ResultCard>();
        if (!scan.HistorySaved) cards.Add(new(Localizer.T("History could not be saved"), Localizer.T("Results remain available in this window. Your existing history was preserved."), CardStatus.Unknown));
        var ordered = FindingAnalysis.Rank(FindingAnalysis.Normalize(latest.Results)).SelectMany(g => g.Group.Sources).ToArray();
        ResultCard Card(DiagnosticResult r) => new(DisplayTitle(latest, r), r.Explanation, ScanPresentation.Status(r), Localizer.T("See next step"), () => RenderFinding(r));
        cards.AddRange(ordered.Where(ScanPresentation.NeedsAttention).Select(Card));
        if (latest.Cancelled || latest.CompletedModules < latest.PlannedModules) cards.Add(new(Localizer.T("Some checks are incomplete"),
            Localizer.Format("Checks processed: {0}/{1}. Run a new scan to check the remaining items.", latest.CompletedModules, latest.PlannedModules), CardStatus.Unknown));
        var gaps = ordered.Where(ScanPresentation.HasGap).ToArray();
        if (gaps.Length > 0) {
            cards.Add(new(Localizer.Format("Checks with missing evidence: {0}", gaps.Length), Localizer.T("These checks cannot establish that everything is OK."), CardStatus.Unknown,
                Localizer.T(showGaps ? "Hide incomplete checks" : "Review incomplete checks"), () => { showGaps = !showGaps; RenderResults(); }));
            if (showGaps) cards.AddRange(gaps.Select(Card));
        }
        var other = ordered.Where(r => !ScanPresentation.NeedsAttention(r) && !ScanPresentation.HasGap(r)).ToArray();
        if (other.Length > 0) {
            cards.Add(new(Localizer.Format("Other completed checks: {0}", other.Length), Localizer.T("Healthy checks and information are kept here. No action is required just because a result is listed."), CardStatus.Info,
                Localizer.T(showOther ? "Hide other checks" : "Show other checks"), () => { showOther = !showOther; RenderResults(); }));
            if (showOther) cards.AddRange(other.Select(Card));
        }
        if (cards.Count == 0) cards.Add(new(Localizer.T("No results yet"), Localizer.T("Run the scan again when you are ready."), CardStatus.Unknown));
        guided.Show(new Diagnosis(FullScanPanel.Summary(latest), ordered.Any(r => r.Severity == FindingSeverity.Critical) ? CardStatus.Problem : ordered.Any(ScanPresentation.NeedsAttention) ? CardStatus.Review : latest.Complete && ordered.Length > 0 ? CardStatus.Good : CardStatus.Unknown,
            ScanPresentation.Headline(latest), cards));
    }

    /// <summary>One finding: what was found, a manual next step (with the tool that helps), and what the check covers. Technical details hold the raw evidence.</summary>
    private void RenderFinding(DiagnosticResult finding)
    {
        backToResults.Visibility = Visibility.Visible;
        var recommendation = FindingAnalysis.Recommend(finding);
        var route = ScanPresentation.Route(finding);
        guided.Show(new Diagnosis(FindingAnalysis.Describe(finding), ScanPresentation.Status(finding), finding.Title, [
            new(Localizer.T("What we found"), finding.Explanation, ScanPresentation.Status(finding)),
            new(Localizer.T("Try this next"), recommendation?.ManualAction ?? Localizer.T("Review the coverage below. A missing result is not a diagnosis."), CardStatus.Info,
                route is null ? null : Localizer.T("Open recommended tool"), route is null ? null : () => OpenRoute(route)),
            new(Localizer.T("What this check covers"), finding.Coverage + (ScanPresentation.HasGap(finding) ? "\n" + Localizer.T("This check is incomplete.") : ""), ScanPresentation.HasGap(finding) ? CardStatus.Unknown : CardStatus.Info)
        ]));
    }

    // Several findings share a module title (for example two services); the finding id tells them apart.
    private static string DisplayTitle(DiagnosticScan latest, DiagnosticResult r) =>
        latest.Results.Count(x => x.Title == r.Title) > 1 && r.FindingId != "collection" ? r.Title + " · " + r.FindingId.Replace("service-", "", StringComparison.Ordinal) : r.Title;
}
