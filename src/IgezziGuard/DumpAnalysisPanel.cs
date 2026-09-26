namespace IgezziGuard;

public sealed class DumpAnalysisPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    private string? dump, debugger;
    private readonly CheckBox symbols = new() { Text = "Allow Microsoft symbol downloads", AutoSize = true };
    public DumpAnalysisPanel() : base("Inspect a local crash dump, or run Microsoft's installed CDB/KD debugger for !analyze -v and stack output. Hanki never uploads the dump. Symbol downloads are off by default. Debugger output is evidence, not a guaranteed root cause. Install Debugging Tools for Windows separately if the debugger is absent.") {
        Button("Choose / inspect dump…", async () => {
            using var picker = new OpenFileDialog { Filter = "Crash dumps|*.dmp;*.mdmp|All files|*.*" }; if (picker.ShowDialog(this) != DialogResult.OK) return;
            dump = Path.GetFullPath(picker.FileName); var path = dump;
            await Run(token => { token.ThrowIfCancellationRequested(); using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Task.FromResult(path + "\r\n\r\n" + DumpInspector.Inspect(file)); });
        });
        Button("Select Microsoft debugger…", () => {
            using var picker = new OpenFileDialog { Filter = "Microsoft debuggers|cdb.exe;kd.exe", InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "Debuggers", "x64") };
            if (picker.ShowDialog(this) == DialogResult.OK) { debugger = Path.GetFullPath(picker.FileName); Output.Text = "Debugger selected: " + debugger + "\r\nOnly a valid Microsoft-signed cdb.exe or kd.exe will be launched. Use your trusted Windows SDK installation."; }
        });
        Bar.Controls.Add(symbols);
        Button("Analyze dump", async () => {
            if (dump is null || debugger is null) { Output.Text = "Select a dump and installed cdb.exe (user dump) or kd.exe (kernel dump) first."; return; }
            var path = dump; var exe = debugger; bool online = symbols.Checked;
            if (!Review($"Run the selected Microsoft debugger against this dump?\n{path}\nDebugger: {exe}\n\nLocal commands: !analyze -v; k; q\n{(online ? "Microsoft symbol requests disclose module/symbol identifiers and your IP; the dump is not uploaded." : "No symbol server configured; analysis may lack symbols.")}\nTimeout 180 seconds. Dump content is sensitive; review output before sharing.")) return;
            await Run(async token => {
                var name = Path.GetFileName(exe).ToLowerInvariant();
                if (name is not ("cdb.exe" or "kd.exe")) throw new IOException("Expected Microsoft cdb.exe or kd.exe.");
                using var debuggerLock = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
                using (var header = new BinaryReader(File.OpenRead(path))) { if (header.ReadUInt32() == 0x45474150 && name != "kd.exe") throw new IOException("Kernel PAGE dump: select kd.exe from Debugging Tools for Windows. CDB is for user-mode dumps."); }
                var check = await WindowsCommand.PowerShell("$s=Get-AuthenticodeSignature -LiteralPath " + WindowsCommand.Quote(exe) + "; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch '(?:^|,\\s*)O=Microsoft Corporation(?:,|$)'){throw 'Debugger must have a valid Microsoft signature'}; 'Verified'", token);
                var cache = Path.Combine(SecurityPaths.Root, "symbols"); Directory.CreateDirectory(cache);
                string symbolPath = online ? "srv*" + cache + "*https://msdl.microsoft.com/download/symbols" : cache;
                string output = await WindowsCommand.Run(exe, ["-sins", "-y", symbolPath, "-z", path, "-c", "!analyze -v; k; q"], token, 180, workingDirectory: Path.GetDirectoryName(exe), isolateDebugger: true);
                return "LOCAL DEBUGGER ANALYSIS\r\n" + check + "\r\n" + output + "\r\n\r\nDo not treat 'probably caused by', a faulting module or a failure bucket as a confirmed diagnosis. Compare repeated dumps, event timeline and recent driver/software changes. Missing symbols can weaken conclusions.";
            });
        });
    }
}

/// <summary>One troubleshooting step; <see cref="Route"/> names the Hanki tool that performs it, if any.</summary>
public sealed record GuideStep(string Title, string Why, string? Route = null, string? ActionLabel = null);

public sealed class TroubleshootingPanel : ToolPage
{
    private readonly ComboBox symptom = new() { Width = 380, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = Localizer.T("What's happening?"), Margin = new Padding(0, 6, 8, 0) };
    /// <summary>Raised with a tool route name, for example "Diagnose  /  Crash timeline".</summary>
    public event Action<string>? OpenRequested;
    public TroubleshootingPanel() : base("Pick what's happening to get a short, ordered plan. Each step explains why it helps and opens the right tool. Steps only read information unless you choose a change yourself, and every supported change can be undone in Recovery.") {
        Bar.Controls.Add(new Label { Text = Localizer.T("What's happening?"), AutoSize = true, Margin = new Padding(0, 10, 8, 0) });
        symptom.Items.AddRange(Guides.Select(g => Localizer.T(g.Symptom)).ToArray()); Bar.Controls.Add(symptom);
        symptom.SelectedIndexChanged += (_, _) => ShowGuide();
        symptom.SelectedIndex = 0;
    }
    internal static readonly (string Symptom, GuideStep[] Steps)[] Guides = [
        ("My PC restarted by itself or showed a blue screen", [
            new("Check what Windows recorded", "Looks for unexpected restarts, blue screens and disk or hardware errors in the last 7 days.", "Diagnose  /  Recent Event Logs", "Open Event logs"),
            new("See what happened just before", "Builds a timeline of events around the restart so you can spot a failing driver, disk or device.", "Diagnose  /  Crash timeline", "Open Crash timeline"),
            new("Run a full scan", "Checks storage, devices, Windows files and security in one pass.", "Fix My PC", "Open Full scan"),
            new("Look inside the crash dump (advanced)", "If Windows saved a dump, Microsoft's debugger can name the module involved.", "Diagnose  /  Dump analysis", "Open Dump analysis"),
            new("Think about recent changes", "New drivers, hardware, updates or overclocking are the most common causes. Undo one change at a time and note what you did."),
        ]),
        ("My PC freezes or the screen goes black", [
            new("Check for graphics and disk problems", "Graphics driver recoveries and storage resets are common causes of short freezes and black screens.", "Diagnose  /  Recent Event Logs", "Open Event logs"),
            new("Measure while it happens", "Monitor CPU, memory, disk and GPU during the activity that freezes to see what is maxed out.", "Performance Lab  /  Monitor", "Open Monitor"),
            new("Check memory right now", "Running out of memory makes Windows stall.", "Memory  /  Memory & pagefile", "Open Memory"),
            new("Update graphics and storage drivers", "Get drivers from your PC or graphics card maker. Also check temperatures if freezes happen under load."),
        ]),
        ("My PC is slow", [
            new("See what's using memory now", "Shows how much memory is free and the biggest users.", "Memory  /  Memory & pagefile", "Open Memory"),
            new("Measure CPU for 30 seconds", "Shows whether the processor is busy even when you aren't doing much.", "Performance Lab  /  Comparisons", "Open Comparisons"),
            new("Trim startup apps", "Fewer apps launching at sign-in means a faster start. Every change can be undone.", "Maintain  /  Startup / undo", "Open Startup entries"),
            new("Free up disk space", "A nearly full system drive slows Windows and blocks updates.", "Maintain  /  Files & storage", "Open Files"),
            new("Check the power plan", "Battery-saving plans limit speed; you can switch and undo.", "CPU  /  Power plans", "Open power plans"),
            new("Check when Windows last restarted", "With Fast Startup, Shut down doesn't restart Windows. A real restart finishes updates and clears memory.", "Diagnose  /  Battery & startup", "Open Battery & startup"),
        ]),
        ("My laptop battery runs out quickly", [
            new("Check battery health", "Shows how much of its original capacity the battery still holds.", "Diagnose  /  Battery & startup", "Open Battery & startup"),
            new("See what keeps the processor busy", "A processor that stays busy in the background drains the battery.", "Performance Lab  /  Comparisons", "Open Comparisons"),
            new("Trim startup apps", "Apps that start with Windows keep running in the background. Every change can be undone.", "Maintain  /  Startup / undo", "Open Startup entries"),
            new("Check the power plan", "A high-performance plan uses more power; you can switch and undo.", "CPU  /  Power plans", "Open power plans"),
            new("See Windows' battery usage", "Settings → System → Power & battery → Battery usage shows which apps use the most battery."),
        ]),
        ("The internet is slow or keeps dropping", [
            new("Follow one step at a time", "Check your connection, try the suggested step and verify whether it helped.", "Connect  /  Guided troubleshooting", "Open guided troubleshooting"),
            new("Check the basic connection", "Tests your adapter, router, name lookups (DNS) and internet access, and says which step fails.", "Connect  /  Basic checks", "Open Basic checks"),
            new("Test Wi-Fi and response times", "Compares your router with the internet to show whether trouble starts at home or further out.", "Connect  /  Wi-Fi / latency", "Open Wi-Fi / latency"),
            new("Measure speed and compare DNS", "Runs a bounded speed test and compares DNS servers.", "Connect  /  Advanced / DNS repair", "Open Network tools"),
            new("Restart your router", "Unplug it for 30 seconds. This clears many home network problems."),
        ]),
        ("I'm running out of disk space", [
            new("Find the biggest files", "Choose your drive, then use Largest 100. Nothing is deleted without your review.", "Maintain  /  Files & storage", "Open Files"),
            new("Find duplicate files", "Finds identical copies so you can keep one.", "Maintain  /  Duplicates", "Open Duplicates"),
            new("Review large apps", "Sort installed apps by size and uninstall ones you don't use in Windows.", "Maintain  /  Apps & storage", "Open Apps"),
            new("Empty the Recycle Bin", "Recycled files still use space until the Recycle Bin is emptied."),
        ]),
        ("I'm worried about viruses or security", [
            new("Check Defender's protection", "Confirms real-time protection and other safeguards are on.", "Shield  /  Defender audit", "Open Defender audit"),
            new("Run a scan and review detections", "Start a Defender quick scan and see anything found in the last 30 days.", "Shield  /  Defender controls / alerts", "Open Scans & alerts"),
            new("Review startup apps", "Unknown programs that launch at sign-in are worth a look.", "Maintain  /  Startup / undo", "Open Startup entries"),
            new("Keep Windows updated", "Updates close security holes. Check that they're installing, and avoid antivirus exclusions you don't understand.", "Diagnose  /  Windows Update", "Open Windows Update check"),
        ]),
        ("Windows Update fails or is stuck", [
            new("Check Windows Update", "Shows when updates last installed, which ones failed and what their error codes mean, and whether a restart is waiting.", "Diagnose  /  Windows Update", "Open Windows Update check"),
            new("Free up disk space", "Updates need several gigabytes free on the system drive.", "Maintain  /  Files & storage", "Open Files"),
            new("Restart, then try again", "Use Restart, not Shut down: with Fast Startup, Shut down doesn't finish pending updates.", "Diagnose  /  Battery & startup", "Open Battery & startup"),
            new("Check Windows' own files", "A damaged component store blocks updates. The full scan checks it when Hanki runs as administrator.", "Fix My PC", "Open Fix My PC"),
            new("Use Windows' own troubleshooter", "Settings → System → Troubleshoot → Other troubleshooters → Windows Update."),
        ]),
        ("Windows says it isn't activated", [
            new("Check activation status", "Explains what Windows reports about its license, in plain language. No keys are collected.", "Diagnose  /  Windows Activation", "Open Windows Activation"),
            new("Use Windows' own activation settings", "Windows Settings → System → Activation offers troubleshooting and lets you enter a genuine product key."),
        ]),
    ];
    /// <summary>Opens the plan for a symptom, for example from the Home search.</summary>
    internal void ShowSymptom(int index) { if (index >= 0 && index < symptom.Items.Count) symptom.SelectedIndex = index; }
    private void ShowGuide()
    {
        if (symptom.SelectedIndex < 0) return;
        var (name, steps) = Guides[symptom.SelectedIndex];
        Output.Text = name + "\r\n\r\n" + string.Join("\r\n", steps.Select((s, i) => $"{i + 1}. {s.Title} — {s.Why}")) +
            "\r\n\r\nNearby events and individual measurements are clues, not proof. Do not run untrusted commands copied from logs or AI output.";
        var cards = steps.Select((s, i) => new ResultCard($"Step {i + 1}: {s.Title}", s.Why, CardStatus.Info,
            s.Route is null ? null : s.ActionLabel, s.Route is { } route ? () => OpenRequested?.Invoke(route) : null)).ToList();
        cards.Add(new("Good to know", "Work through the steps in order and change one thing at a time. Results are clues, not proof. Hanki records every supported change in Recovery so you can undo it."));
        ShowSummary(new Diagnosis(Output.Text, CardStatus.Info, name, cards));
    }
}
