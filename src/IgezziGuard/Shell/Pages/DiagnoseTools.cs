using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace IgezziGuard.Shell;

/// <summary>The pages of Connect and Diagnose that used to be WinForms tools: network tools, activation, dump analysis and crash timeline, as report pages.</summary>
internal static class DiagnoseTools
{
    private static TextBox Field(string name, string text, double width)
    {
        var box = new TextBox { Style = (Style)Application.Current.FindResource("FieldBox"), Text = text, Width = width, Margin = new Thickness(0, 0, 8, 8) };
        System.Windows.Automation.AutomationProperties.SetName(box, name); return box;
    }
    private static CheckBox Check(string text)
    {
        var box = new CheckBox { Content = text, Margin = new Thickness(6, 8, 14, 8), VerticalAlignment = VerticalAlignment.Center };
        return box;
    }

    /// <summary>Connect → Network tools: trace route, compare DNS, bounded speed test, and IPv4 DNS repair with undo.</summary>
    internal static ReportView NetworkTools(IShellServices shell)
    {
        var host = Field("Host to test", "example.com", 230);
        var adapters = Pick.Box("Network adapter", 320);
        var interfaces = new List<NetworkInterface>();
        ReportView view = null!;

        void Refresh()
        {
            try {
                interfaces = NetworkInterface.GetAllNetworkInterfaces().Where(a => Guid.TryParse(a.Id, out _) && NetworkAdapterFilter.IsUserAdapter(a.Description, a.NetworkInterfaceType)).ToList();
                adapters.Items.Clear(); foreach (var a in interfaces) adapters.Items.Add(a.Name + " / " + a.OperationalStatus);
                view.ShowPlain(interfaces.Count + " adapters found. Pick yours (usually Wi-Fi or Ethernet) before changing DNS.");
            } catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException) { view.ShowPlain(ex.Message); }
        }

        async Task<string> ChangeDns(string after)
        {
            if (adapters.SelectedIndex < 0 || adapters.SelectedIndex >= interfaces.Count) return "Choose a network adapter first: click Refresh adapters, pick yours (usually Wi-Fi or Ethernet), then try again. Nothing was changed.";
            var adapter = interfaces[adapters.SelectedIndex]; var target = Guid.Parse(adapter.Id).ToString();
            var before = await new WindowsSettings().Read("IPv4 DNS", target, CancellationToken.None);
            if (!Blocks.Confirm("Change IPv4 DNS", $"Change IPv4 DNS for {adapter.Name}?\nBefore: {(before.Length == 0 ? "Automatic" : before)}\nAfter: {(after.Length == 0 ? "Automatic" : after)}\n\nMay interrupt name resolution or break private/corporate names. Public DNS receives future queries. IPv6 DNS is not changed. Policy/VPN settings can take precedence. Recovery stores the original configuration. Administrator rights may be required.", "Change DNS")) return "Nothing was changed.";
            await WindowsSettings.Journal().Apply("IPv4 DNS", target, before, after, CancellationToken.None);
            return "IPv4 DNS setting applied and verified. Re-test connectivity. Undo in Recovery. This does not override VPN/NRPT or prove the network is fixed.";
        }

        view = new ReportView(shell, "Network tools",
            "Trace routes, compare DNS resolvers, test bounded transfer throughput, and repair IPv4 DNS with undo. Targets/resolvers see your source IP and requested hostname. Speed tests use Cloudflare and transfer up to 25 MiB down plus 10 MiB up. VPNs, filtering, server load and route asymmetry affect results. No network reset or firewall changes.", [
            new("Trace route", Output: async (_, token) => {
                var name = NetworkToolsPanel.ValidateHost(host.Text);
                if (!Blocks.Confirm("Trace route", $"Trace {name} using up to 20 ICMP TTL probes? The destination/network can observe probes; local DNS resolves the hostname.", "Trace")) return "Nothing was sent.";
                return await NetworkToolsPanel.Trace(name, token);
            }, Primary: true),
            new("Compare DNS", Output: async (view2, token) => {
                var name = NetworkToolsPanel.ValidateHost(host.Text);
                if (IPAddress.TryParse(name, out _)) throw new ArgumentException("Enter a DNS hostname for DNS comparisons.");
                if (!Blocks.Confirm("Compare DNS", $"Query A records for {name} three times each using configured DNS, Cloudflare 1.1.1.1 and Google 8.8.8.8? Public resolvers receive this hostname. Cached results can affect timings.", "Compare")) return "Nothing was sent.";
                return await NetworkToolsPanel.CompareDns(name, token);
            }),
            new("Speed test (35 MiB max)", Output: async (_, token) => {
                if (!Blocks.Confirm("Speed test", "Send a bounded speed test to speed.cloudflare.com?\nUp to 25 MiB download and 10 MiB upload plus protocol overhead. This uses bandwidth and may incur metered-data charges. Cloudflare sees your source IP. Each direction has a 25-second timeout; no automatic retry. Results are approximate single-request throughput, not line capacity.", "Start test")) return "Nothing was sent.";
                return await NetworkToolsPanel.Speed(token);
            }),
            new("Refresh adapters", Open: Refresh),
            new("Restore automatic IPv4 DNS", Output: (_, _) => ChangeDns("")),
            new("Use Cloudflare IPv4 DNS", Output: (_, _) => ChangeDns("1.1.1.1,1.0.0.1")),
        ]);
        view.AddToBar(host, first: true); view.AddToBar(adapters);
        return view;
    }

    /// <summary>Diagnose → Windows Activation: read-only licensing data, with an optional organization KMS reachability check.</summary>
    internal static ReportView Activation(IShellServices shell)
    {
        var network = Check("Allow organization KMS network checks");
        var view = new ReportView(shell, "Windows Activation",
            "Understand legitimate Windows activation problems using read-only licensing data. Hanki does not collect full product keys, change activation settings or activate Windows. Organization KMS probes are optional and run only when Windows reports a KMS client.", [
            new("Review Windows activation", Primary: true, Diagnose: async (_, token) => {
                bool approved = network.IsChecked == true;
                if (approved && !Blocks.Confirm("Organization KMS check", "Allow bounded DNS SRV discovery and TCP reachability checks to the KMS host configured in Windows or published by your organization's DNS? Only installed KMS clients are probed. No public KMS server is suggested and no activation request is sent.", "Allow")) throw new OperationCanceledException();
                var context = FullScanPanel.Context(approved);
                var results = await DiagnosticExecution.RunAsync(new ActivationDiagnostic(), context, null, token);
                var cards = results.Select(ResultPresentation.FromFinding).ToList();
                var worst = Diagnosis.Worst(cards.Select(c => c.Status));
                cards.Add(new("What to do next", worst is CardStatus.Problem or CardStatus.Review
                    ? "Open Windows Activation settings (button above) and use Troubleshoot, or enter a genuine product key. Organization PCs may need your IT department."
                    : "Nothing to do. If Windows still shows an activation message, open Windows Activation settings for Microsoft's own troubleshooter."));
                string headline = worst switch { CardStatus.Problem => "Windows activation needs attention", CardStatus.Review => "Windows activation has something worth checking",
                    CardStatus.Unknown => "Activation status could not be fully read", _ => "Windows reports no activation problem" };
                return Diagnosis.From(string.Join("\r\n\r\n", results.Select(FindingAnalysis.Describe)), cards, headline);
            }),
            new("Open Windows Activation settings", Open: () => HealthSettings.Open(shell.DialogOwner, "ms-settings:activation", "Settings → System → Activation")),
        ]);
        view.AddToBar(network);
        return view;
    }

    /// <summary>Diagnose → Dump analysis: inspect a local crash dump or run Microsoft's installed debugger on it.</summary>
    internal static ReportView DumpAnalysis(IShellServices shell)
    {
        string? dump = null, debugger = null;
        var symbols = Check("Allow Microsoft symbol downloads");
        var view = new ReportView(shell, "Dump analysis",
            "Inspect a local crash dump, or run Microsoft's installed CDB/KD debugger for !analyze -v and stack output. Hanki never uploads the dump. Symbol downloads are off by default. Debugger output is evidence, not a guaranteed root cause. Install Debugging Tools for Windows separately if the debugger is absent.", [
            new("Choose / inspect dump…", Primary: true, Output: (_, token) => {
                var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Crash dumps|*.dmp;*.mdmp|All files|*.*" };
                if (picker.ShowDialog() != true) return Task.FromResult("No dump chosen.");
                dump = Path.GetFullPath(picker.FileName); var path = dump;
                return Task.Run(() => { token.ThrowIfCancellationRequested(); using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return path + "\r\n\r\n" + DumpInspector.Inspect(file); }, token);
            }),
            new("Select Microsoft debugger…", Open: () => {
                var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Microsoft debuggers|cdb.exe;kd.exe", InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "Debuggers", "x64") };
                if (picker.ShowDialog() == true) { debugger = Path.GetFullPath(picker.FileName); Blocks.Notice("Debugger selected", debugger + "\nOnly a valid Microsoft-signed cdb.exe or kd.exe will be launched. Use your trusted Windows SDK installation."); }
            }),
            new("Analyze dump", Output: async (_, token) => {
                if (dump is null || debugger is null) return "Select a dump and installed cdb.exe (user dump) or kd.exe (kernel dump) first.";
                var path = dump; var exe = debugger; bool online = symbols.IsChecked == true;
                if (!Blocks.Confirm("Run the debugger", $"Run the selected Microsoft debugger against this dump?\n{path}\nDebugger: {exe}\n\nLocal commands: !analyze -v; k; q\n{(online ? "Microsoft symbol requests disclose module/symbol identifiers and your IP; the dump is not uploaded." : "No symbol server configured; analysis may lack symbols.")}\nTimeout 180 seconds. Dump content is sensitive; review output before sharing.", "Run debugger")) return "Nothing was run.";
                var name = Path.GetFileName(exe).ToLowerInvariant();
                if (name is not ("cdb.exe" or "kd.exe")) throw new IOException("Expected Microsoft cdb.exe or kd.exe.");
                using var debuggerLock = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
                using (var header = new BinaryReader(File.OpenRead(path))) { if (header.ReadUInt32() == 0x45474150 && name != "kd.exe") throw new IOException("Kernel PAGE dump: select kd.exe from Debugging Tools for Windows. CDB is for user-mode dumps."); }
                var check = await WindowsCommand.PowerShell("$s=Get-AuthenticodeSignature -LiteralPath " + WindowsCommand.Quote(exe) + "; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch '(?:^|,\\s*)O=Microsoft Corporation(?:,|$)'){throw 'Debugger must have a valid Microsoft signature'}; 'Verified'", token);
                var cache = Path.Combine(SecurityPaths.Root, "symbols"); Directory.CreateDirectory(cache);
                string symbolPath = online ? "srv*" + cache + "*https://msdl.microsoft.com/download/symbols" : cache;
                string output = await WindowsCommand.Run(exe, ["-sins", "-y", symbolPath, "-z", path, "-c", "!analyze -v; k; q"], token, 180, workingDirectory: Path.GetDirectoryName(exe), isolateDebugger: true);
                return "LOCAL DEBUGGER ANALYSIS\r\n" + check + "\r\n" + output + "\r\n\r\nDo not treat 'probably caused by', a faulting module or a failure bucket as a confirmed diagnosis. Compare repeated dumps, event timeline and recent driver/software changes. Missing symbols can weaken conclusions.";
            }),
        ]);
        view.AddToBar(symbols);
        return view;
    }

    /// <summary>Diagnose → Crash timeline: find restart markers, then list what Windows logged in the minutes around one.</summary>
    internal static ReportView CrashTimeline(IShellServices shell)
    {
        var markers = Pick.Box("Restart marker", 440);
        var center = Field("Local time (yyyy-MM-dd HH:mm:ss)", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), 190);
        var minutes = Field("Minutes either side", "15", 52);
        var found = new List<CrashEvent>();
        ReportView view = null!;
        markers.SelectionChanged += (_, _) => { if (markers.SelectedIndex >= 0 && markers.SelectedIndex < found.Count) center.Text = found[markers.SelectedIndex].Time.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"); };

        view = new ReportView(shell, "Crash timeline",
            "See what happened before a crash. 1. Find restart markers lists unexpected restarts and blue screens from the last 7 days. 2. Pick one, or type the time you remember the problem happening (local time; a marker's logged time may be after restart). 3. Build timeline shows everything Windows logged in the minutes around it, with a plain explanation of each event. The restart record itself isn't the cause; look for disk, hardware or driver events just before it.", [
            new("Find restart markers", Primary: true, Output: async (_, token) => {
                var end = DateTimeOffset.Now;
                var result = await Task.Run(() => CrashEventReader.Read(end.AddDays(-7), end, true, token), token);
                found = result.Events.OrderByDescending(e => e.Time).ToList(); markers.Items.Clear();
                foreach (var e in found) markers.Items.Add($"{e.Time.ToLocalTime():ddd d MMM  HH:mm:ss}  ·  {EventKnowledge.Describe(e.Provider, e.Id).Name}");
                if (found.Count > 0) markers.SelectedIndex = 0;
                return found.Count == 0
                    ? $"No unexpected restarts or blue screens were recorded in the last 7 days.\r\n\r\nIf you know roughly when something went wrong, set that time and click Build timeline.\r\n\r\n{result.Coverage}"
                    : $"Found {found.Count} restart record(s) in the last 7 days; the newest is selected. One restart often leaves two records.\r\n\r\nNext: click Build timeline to see what Windows logged in the minutes around it.\r\n\r\n{result.Coverage}";
            }),
            new("Build timeline", Primary: true, Output: async (_, token) => {
                if (!DateTime.TryParse(center.Text, out var parsed)) throw new ArgumentException("Type the local time as yyyy-MM-dd HH:mm:ss.");
                if (!int.TryParse(minutes.Text, out var range) || range is < 1 or > 60) throw new ArgumentException("Minutes must be between 1 and 60.");
                var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
                if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local)) throw new ArgumentException("This local time is invalid or ambiguous at a daylight-saving transition. Choose an unambiguous nearby time and widen the window.");
                var time = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
                var result = await Task.Run(() => CrashEventReader.Read(time.AddMinutes(-range), time.AddMinutes(range), false, token), token);
                return IgezziGuard.CrashTimeline.Report(result.Events, time, range, result.Coverage);
            }),
            new("Review / export", Open: () => {
                if (view.PlainText.Length == 0) { Blocks.Notice("Nothing to export", "Build a timeline first."); return; }
                Blocks.EditText(Application.Current.MainWindow!, "Review / redact timeline", view.PlainText, ("Save reviewed report", text => {
                    var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Text report|*.txt", FileName = "Hanki-crash-timeline.txt", OverwritePrompt = true };
                    if (dialog.ShowDialog() == true) try { File.WriteAllText(dialog.FileName, text); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Blocks.Notice("Export failed", ex.Message); }
                }, true));
            }),
        ]);
        view.AddToBar(markers, first: true); view.AddToBar(center); view.AddToBar(minutes);
        return view;
    }
}

/// <summary>Diagnose → Guided checks: pick what is happening and get a short, ordered plan whose steps open the right tool.</summary>
internal sealed class GuidedChecksView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly ComboBox symptom = Pick.Box("What's happening?", 420);
    private readonly DiagnosisView plan = new();
    internal int SymptomCount => symptom.Items.Count;
    internal int CardCount => plan.CardCount;

    internal GuidedChecksView(IShellServices shell)
    {
        this.shell = shell; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        stack.Children.Add(UiKit.Text("Pick what's happening to get a short, ordered plan. Each step explains why it helps and opens the right tool. Steps only read information unless you choose a change yourself, and every supported change can be undone in Recovery.", 14, UiKit.Res("TextMuted"), wrap: true));
        var row = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var label = UiKit.Text("What's happening?", 14); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 10, 8);
        row.Children.Add(label); row.Children.Add(symptom);
        foreach (var guide in TroubleshootingPanel.Guides) symptom.Items.Add(guide.Symptom);
        symptom.SelectionChanged += (_, _) => Show();
        stack.Children.Add(row); plan.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(plan); Content = stack;
        symptom.SelectedIndex = 0;
    }

    internal void ShowSymptom(int index) { if (index >= 0 && index < symptom.Items.Count) symptom.SelectedIndex = index; }

    private void Show()
    {
        if (symptom.SelectedIndex < 0) return;
        var (name, steps) = TroubleshootingPanel.Guides[symptom.SelectedIndex];
        var text = name + "\r\n\r\n" + string.Join("\r\n", steps.Select((s, i) => $"{i + 1}. {s.Title} — {s.Why}")) +
            "\r\n\r\nNearby events and individual measurements are clues, not proof. Do not run untrusted commands copied from logs or AI output.";
        var cards = steps.Select((s, i) => new ResultCard($"Step {i + 1}: {s.Title}", s.Why, CardStatus.Info,
            s.Route is null ? null : s.ActionLabel, s.Route is { } route ? () => shell.Routes.FirstOrDefault(r => r.Name == route)?.Open() : null)).ToList();
        cards.Add(new("Good to know", "Work through the steps in order and change one thing at a time. Results are clues, not proof. Hanki records every supported change in Recovery so you can undo it."));
        plan.Show(new Diagnosis(text, CardStatus.Info, name, cards));
    }
}
