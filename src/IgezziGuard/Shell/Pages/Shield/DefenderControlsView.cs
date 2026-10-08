using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace IgezziGuard.Shell;

/// <summary>
/// Defender controls and alerts: refresh protection and recent findings, ask Defender for a quick or full scan or a definitions update,
/// cancel an active scan, and optionally watch protection every minute while Hanki is open. Every action is reviewed first; Defender
/// itself decides what to do and Windows asks for administrator approval.
/// </summary>
internal sealed class DefenderControlsView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly Button refresh = Buttons.Primary("Refresh protection / findings"), quick = Buttons.Secondary("Defender quick scan"), full = Buttons.Secondary("Defender full scan"),
        stop = Buttons.Secondary("Cancel active Defender scan"), update = Buttons.Secondary("Update Defender definitions"), toggle = Buttons.Secondary("Enable protection alerts"), cancel = Buttons.Secondary("Cancel");
    private readonly TextBlock alert = UiKit.Text("Protection monitoring off. Checks run only when requested or while explicitly enabled in this app.", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly TextBlock message = UiKit.Text("", 14, UiKit.Res("TextPrimary"), wrap: true);
    private readonly DiagnosisView view = new();
    private readonly DispatcherTimer watch = new() { Interval = TimeSpan.FromSeconds(60) };
    private CancellationTokenSource? running, monitoring;
    private bool checking;

    internal bool MonitoringOn => watch.IsEnabled;
    internal DiagnosisView Result => view;

    internal DefenderControlsView(IShellServices shell)
    {
        this.shell = shell;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
        root.Children.Add(UiKit.Text("Use Windows Defender's own quick/full scans and read recent detections. Defender may remediate/quarantine according to Windows policy; those actions are not undone by Hanki. Another antivirus, passive mode, policy or permissions may explain disabled or unavailable results.", 14, UiKit.Res("TextMuted"), wrap: true));
        var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 4) };
        foreach (var b in new[] { refresh, quick, full, stop, update, toggle, cancel }) bar.Children.Add(b);
        root.Children.Add(bar);
        var card = new Border { Style = (Style)System.Windows.Application.Current.FindResource("Card"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 4, 0, 6), Child = alert };
        root.Children.Add(card); root.Children.Add(message); view.Margin = new Thickness(0, 6, 0, 0); root.Children.Add(view);
        Content = root; cancel.IsEnabled = false;

        refresh.Click += async (_, _) => await Refresh();
        cancel.Click += (_, _) => running?.Cancel();
        quick.Click += (_, _) => Start(1);
        full.Click += (_, _) => Start(2);
        stop.Click += (_, _) => { if (ReviewPresenter.Ask("Ask Defender to cancel active quick/full scanning? This can also affect a scan started outside Hanki. Completed remediation is not reversed. Windows may request administrator approval (UAC).")) Launch(["-Scan", "-Cancel"], "Cancel command launched; verify scan status in Windows Security."); };
        update.Click += (_, _) => { if (ReviewPresenter.Ask("Ask Defender to update security intelligence using its configured update source? This uses the network and may request Windows administrator approval. Hanki does not undo Defender updates.")) Launch(["-SignatureUpdate"], "Definition update requested. Refresh protection status to verify signature age/date."); };
        toggle.Click += (_, _) => ToggleMonitoring();
        watch.Tick += async (_, _) => await Check();
    }

    /// <summary>Stops the minute-by-minute watch (when the window closes).</summary>
    internal void StopMonitoring() { watch.Stop(); monitoring?.Cancel(); }

    private void ToggleMonitoring()
    {
        if (watch.IsEnabled) { StopMonitoring(); toggle.Content = "Enable protection alerts"; alert.Text = "Monitoring off."; }
        else { watch.Start(); toggle.Content = "Disable protection alerts"; alert.Text = "Monitoring on while Hanki remains open. Next check within 60 seconds."; _ = Check(); }
    }

    private async Task Check()
    {
        if (running is not null || checking) return; checking = true;
        using var cts = new CancellationTokenSource(); monitoring = cts;
        using var ticket = shell.Tasks.Begin("Defender", cts.Cancel);
        try {
            using var doc = JsonDocument.Parse(await Task.Run(() => DefenderToolsPanel.Status(cts.Token), cts.Token));
            if (watch.IsEnabled) {
                var state = DefenderReview.Alerts(doc.RootElement); alert.Text = DateTime.Now.ToString("T") + "  " + state.Replace("\r\n", " • ");
                if (!state.StartsWith("Selected protection", StringComparison.Ordinal)) shell.Say("Defender: attention needed — see Shield / controls and alerts.");
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) { if (watch.IsEnabled) { alert.Text = "Protection status unknown: " + ex.Message; shell.Say("Defender status unavailable — see Shield / controls and alerts."); } }
        finally { monitoring = null; checking = false; }
    }

    private async Task Refresh()
    {
        if (running is not null) return;
        using var cts = new CancellationTokenSource(); running = cts; refresh.IsEnabled = false; cancel.IsEnabled = true;
        using var ticket = shell.Tasks.Begin("Defender", cts.Cancel);
        message.Text = "Collecting results. Your previous report remains available after this operation."; view.Clear();
        try { view.Show(await Task.Run(() => DefenderToolsPanel.Report(cts.Token), cts.Token)); message.Text = "Report ready · " + DateTime.Now.ToString("t"); }
        catch (OperationCanceledException) { message.Text = "Cancelled. Nothing on your PC was changed; incomplete results were discarded. Run it again when you're ready."; }
        catch (Exception ex) { message.Text = "Operation stopped: " + ex.Message; }
        finally { running = null; refresh.IsEnabled = true; cancel.IsEnabled = false; }
    }

    private void Start(int type)
    {
        if (!ReviewPresenter.Ask($"Start a Defender {(type == 1 ? "quick" : "full")} scan?\nWindows policy may automatically remediate or quarantine detections. Full scans can be lengthy and disk-intensive. Windows may request administrator approval (UAC). The scan continues if Hanki closes; Hanki's general Cancel tasks does not stop it.")) return;
        Launch(["-Scan", "-ScanType", type.ToString()], "Defender scan command launched, not a completion or acceptance confirmation. Refresh protection/findings and inspect QuickScan/FullScan start/end times. Windows Security is authoritative; policy can reject the request.");
    }

    private void Launch(string[] args, string done)
    {
        try {
            var start = new ProcessStartInfo(DefenderToolsPanel.Exe()) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Could not launch Defender.");
            message.Text = done;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { message.Text = "Windows administrator approval was declined. No retry made."; }
        catch (Exception ex) { message.Text = ex.Message; }
    }
}
