using System.Diagnostics;
using System.Text.Json;

namespace IgezziGuard;

public sealed class DefenderToolsPanel : ToolPage
{
    private readonly System.Windows.Forms.Timer watch = new() { Interval = 60000 };
    private readonly HankiButton toggle;
    private readonly Label alert = new() { Dock = DockStyle.Top, Height = 55, Text = "Protection monitoring off. Checks run only when requested or while explicitly enabled in this app." };
    private bool checking;
    private CancellationTokenSource? monitoring;
    public event Action<string>? ProtectionAlert;
    public bool MonitoringBusy => checking;
    public void StopMonitoring() { watch.Stop(); monitoring?.Cancel(); }
    public DefenderToolsPanel() : base("Use Windows Defender's own quick/full scans and read recent detections. Defender may remediate/quarantine according to Windows policy; those actions are not undone by Hanki. Another antivirus, passive mode, policy or permissions may explain disabled/unavailable fields.") {
        Button("Refresh protection / findings", async () => await Run(Report));
        Button("Defender quick scan", () => Start(1)); Button("Defender full scan", () => Start(2));
        Button("Cancel active Defender scan", () => { if (Review("Ask Defender to cancel active quick/full scanning? This can also affect a scan started outside Hanki. Completed remediation is not reversed. Windows may request administrator approval (UAC).")) Launch(["-Scan", "-Cancel"], "Cancel command launched; verify scan status in Windows Security."); });
        Button("Update Defender definitions", () => { if (Review("Ask Defender to update security intelligence using its configured update source? This uses the network and may request Windows administrator approval. Hanki does not undo Defender updates.")) Launch(["-SignatureUpdate"], "Definition update requested. Refresh protection status to verify signature age/date."); });
        toggle = Button("Enable protection alerts", () => { watch.Enabled = !watch.Enabled; toggle!.Text = watch.Enabled ? "Disable protection alerts" : "Enable protection alerts"; alert.Text = watch.Enabled ? "Monitoring on while Hanki remains open. Next check within 60 seconds." : "Monitoring off."; });
        Controls.Add(alert); Controls.SetChildIndex(alert, 1);
        watch.Tick += async (_, _) => {
            if (IsBusy || checking) return; checking = true;
            using var cts = new CancellationTokenSource(); monitoring = cts;
            try { using var doc = JsonDocument.Parse(await Status(cts.Token)); if (!IsDisposed && watch.Enabled) { var state = DefenderReview.Alerts(doc.RootElement); alert.Text = DateTime.Now.ToString("T") + "  " + state.Replace("\r\n", " • "); if (!state.StartsWith("Selected protection", StringComparison.Ordinal)) ProtectionAlert?.Invoke("Defender: attention needed — see Shield / controls and alerts."); } }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
            catch (Exception ex) { if (!IsDisposed && watch.Enabled) { alert.Text = "Protection status unknown: " + ex.Message; ProtectionAlert?.Invoke("Defender status unavailable — see Shield / controls and alerts."); } }
            finally { monitoring = null; checking = false; }
        };
        Disposed += (_, _) => watch.Dispose();
    }
    private static string Exe() { var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe"); return File.Exists(exe) ? exe : throw new IOException("Microsoft Defender command tool is not present at its standard location. Use Windows Security."); }
    private void Start(int type) {
        if (!Review($"Start a Defender {(type == 1 ? "quick" : "full")} scan?\nWindows policy may automatically remediate or quarantine detections. Full scans can be lengthy and disk-intensive. Windows may request administrator approval (UAC). The scan continues if Hanki closes; Hanki's general Cancel button does not stop it. Use Cancel active Defender scan or Windows Security.")) return;
        Launch(["-Scan", "-ScanType", type.ToString()], "Defender scan command launched, not a completion or acceptance confirmation. Refresh protection/findings and inspect QuickScan/FullScan start/end times. Windows Security is authoritative; policy can reject the request.");
    }
    private void Launch(string[] args, string message) {
        try { var start = new ProcessStartInfo(Exe()) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }; foreach (var arg in args) start.ArgumentList.Add(arg); using var process = Process.Start(start) ?? throw new IOException("Could not launch Defender."); Output.Text = message; }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { Output.Text = "Windows administrator approval was declined. No retry made."; }
        catch (Exception ex) { Output.Text = ex.Message; }
    }
    private static Task<string> Status(CancellationToken token) => WindowsCommand.PowerShell("Get-MpComputerStatus | Select-Object AMRunningMode,AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,BehaviorMonitorEnabled,IsTamperProtected,AntivirusSignatureAge,AntivirusSignatureLastUpdated,QuickScanStartTime,QuickScanEndTime,FullScanStartTime,FullScanEndTime | ConvertTo-Json", token);
    private static async Task<string> Report(CancellationToken token) {
        var json = await Status(token); using var doc = JsonDocument.Parse(json);
        var findings = await WindowsCommand.PowerShell("$detections=@(Get-MpThreatDetection | Sort-Object InitialDetectionTime -Descending | Select-Object -First 30 ThreatID,InitialDetectionTime,LastThreatStatusChangeTime,ActionSuccess,ThreatStatusID,Resources); $threats=@(Get-MpThreat | Select-Object ThreatID,ThreatName,SeverityID,IsActive,DidThreatExecute); [pscustomobject]@{Detections=$detections;ThreatNames=$threats} | ConvertTo-Json -Depth 5", token);
        return $"DEFENDER REVIEW {DateTimeOffset.Now:O}\r\n{DefenderReview.Alerts(doc.RootElement)}\r\n\r\nSTATUS\r\n{json}\r\n\r\nRECENT FINDINGS (up to 30 detections, plus threat-name mapping)\r\n{findings}\r\nEmpty findings are not proof of a clean computer. Resource paths may contain private data. ActionSuccess describes the reported action; inspect Windows Protection History before drawing conclusions. Another antivirus or policy may explain disabled flags.";
    }
}
