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
    private readonly Button start = new(), cancel = new(), repairs = new(), copy = new(), assistant = new(), customer = new();
    private readonly CheckBox probes = new() { Content = "Include network probes", Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border progressCard = new() { Visibility = Visibility.Collapsed };
    private readonly ProgressBar bar = new() { Height = 6, Minimum = 0, Maximum = 1 };
    private readonly TextBlock progressText = UiKit.Text("", 14), activityText = UiKit.Text("", 13, UiKit.Res("TextMuted"), wrap: true);
    private readonly StackPanel summary = new();
    private readonly StackPanel list = new();
    private readonly Border listCard = new() { Visibility = Visibility.Collapsed };
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

        listCard.Style = (Style)Application.Current.FindResource("Card"); listCard.Padding = new Thickness(0, 0, 0, 4);
        var resultsStack = new StackPanel(); summary.Margin = new Thickness(20, 16, 20, 12); resultsStack.Children.Add(summary);
        resultsStack.Children.Add(new Border { Height = 1, Background = UiKit.Res("Hairline") }); resultsStack.Children.Add(list);
        listCard.Child = resultsStack; root.Children.Add(listCard);

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
        Set(repairs, "Review automatic repairs", false, () => _ = scan.ReviewRepairsAsync(shell.DialogOwner));
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
        await scan.StartAsync(probes.IsChecked == true, () => MessageBox.Show(System.Windows.Application.Current.MainWindow,
            "Include network probes? Gateway ICMP contacts your local network. " + WindowsDiagnosticCatalog.ProbeDisclosure + " Installed KMS clients may also query your organization DNS and contact the Windows-configured KMS host. These endpoints and your DNS resolver can see your source IP. No report is uploaded. You can run without these checks by clearing Include network probes.",
            "Review action", MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel) == MessageBoxResult.OK);
    }

    internal bool CanStart => start.IsEnabled;
    internal override void OnShown() { customer.Visibility = scan.CanReportForCustomers ? Visibility.Visible : Visibility.Collapsed; Refresh(); }

    private void Refresh()
    {
        bool busy = scan.IsBusy;
        start.IsEnabled = !busy; repairs.IsEnabled = !busy; copy.IsEnabled = !busy && scan.Latest is not null; assistant.IsEnabled = !busy && scan.Latest is not null;
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
        if (!ReferenceEquals(latest, shown)) { shown = latest; ShowFindings(latest); }
        emptyCard.Visibility = latest is null && !busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowFindings(DiagnosticScan? latest)
    {
        list.Children.Clear(); summary.Children.Clear();
        listCard.Visibility = latest is null || latest.Results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (latest is null) return;
        var headline = UiKit.Text($"{latest.Results.Count} results", 14, UiKit.Res("TextMuted"), FontWeights.SemiBold); headline.Margin = new Thickness(0, 0, 0, 8); summary.Children.Add(headline);
        summary.Children.Add(ResultChips.Build(latest.Results));
        System.Windows.Automation.AutomationProperties.SetName(summary, $"{latest.Results.Count} results: " + string.Join(", ", StatusChips.Count(latest.Results).Select(c => c.Text)));
        foreach (var r in FindingAnalysis.Rank(FindingAnalysis.Normalize(latest.Results)).SelectMany(g => g.Group.Sources)) list.Children.Add(Row(latest, r));
    }

    // Several findings share a module title (for example two services); the finding id tells them apart.
    private static string DisplayTitle(DiagnosticScan latest, DiagnosticResult r) =>
        latest.Results.Count(x => x.Title == r.Title) > 1 && r.FindingId != "collection" ? r.Title + " · " + r.FindingId.Replace("service-", "", StringComparison.Ordinal) : r.Title;

    private UIElement Row(DiagnosticScan latest, DiagnosticResult r)
    {
        var color = UiKit.FromDrawing(HankiTheme.SeverityColor(r.Severity, r.Outcome));
        var head = new Grid { Margin = new Thickness(8, 8, 8, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pill = new Border { CornerRadius = new CornerRadius(11), Background = UiKit.Tint(color, 46), Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        pill.Child = new TextBlock { Text = HankiTheme.SeverityLabel(r.Severity, r.Outcome), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center };
        var title = UiKit.Text(DisplayTitle(latest, r), 15); title.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(title, 1);
        var category = UiKit.Text(r.Category.ToString(), 13, UiKit.Res("TextMuted")); category.VerticalAlignment = VerticalAlignment.Center; category.Margin = new Thickness(12, 0, 6, 0); Grid.SetColumn(category, 2);
        head.Children.Add(pill); head.Children.Add(title); head.Children.Add(category);
        var button = new Button { Style = (Style)Application.Current.FindResource("RowButton"), Content = head };
        System.Windows.Automation.AutomationProperties.SetName(button, $"{HankiTheme.SeverityLabel(r.Severity, r.Outcome)}: {DisplayTitle(latest, r)}. {r.Category}");
        var details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = UiKit.Res("Canvas"), BorderThickness = new Thickness(0), Foreground = UiKit.Res("TextPrimary"), FontSize = 13.5, Padding = new Thickness(18, 12, 18, 14), Text = FindingAnalysis.Describe(r) };
        System.Windows.Automation.AutomationProperties.SetName(details, "Explanation of " + DisplayTitle(latest, r));
        var detailsHost = new Border { Visibility = Visibility.Collapsed, Child = details, BorderBrush = UiKit.Res("Hairline"), BorderThickness = new Thickness(0, 1, 0, 1) };
        button.Click += (_, _) => detailsHost.Visibility = detailsHost.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        var stack = new StackPanel(); stack.Children.Add(button); stack.Children.Add(detailsHost);
        var line = new Border { BorderBrush = UiKit.Res("Hairline"), BorderThickness = new Thickness(0, 0, 0, 1), Child = stack };
        return line;
    }
}
