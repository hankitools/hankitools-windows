namespace IgezziGuard;

public sealed class DumpAnalysisPanel : ToolPage
{
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

public sealed class TroubleshootingPanel : ToolPage
{
    private readonly ComboBox symptom = new() { Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckedListBox steps = new() { Dock = DockStyle.Right, Width = 350, CheckOnClick = true };
    public TroubleshootingPanel() : base("Choose a symptom for a local investigation checklist. These are read-only evidence-gathering steps; completing a checklist does not establish a diagnosis. Checklist ticks stay in memory until exit.") {
        symptom.Items.AddRange(["Unexpected restart / blue screen", "Freeze / black screen", "Memory / pagefile pressure", "Slow network / DNS errors", "Security concern"]); symptom.SelectedIndex = 0; Bar.Controls.Add(symptom);
        Button("Build guided checklist", () => {
            string[] items = symptom.SelectedIndex switch {
                0 => ["Record actual crash time and symptom", "Build Diagnose timeline around that time", "Review bugcheck / WHEA / storage / volmgr evidence", "Inspect available dump locally", "Compare repeated failures and recent changes", "Change one supported setting only; retain undo"],
                1 => ["Record whether audio/input continue during the freeze", "Inspect Display, WHEA and storage events", "Monitor same workload for 5–15 minutes", "Compare GPU engines / disk activity / commit", "Check vendor driver history and temperatures separately", "Capture a dump if Windows generated one"],
                2 => ["Take snapshot during representative heavy use", "Inspect current commit headroom and peak since boot", "Review process working sets without summing shared pages", "Check pagefile configuration and drive free space", "Use system-managed sizing as starting point", "Retest the same workload; do not disable pagefile for FPS"],
                3 => ["Run basic Connect and Wi-Fi checks", "Compare gateway / external ICMP results", "Trace route without treating missing hops as proof", "Compare DNS answers and timings", "Run bounded speed test if data usage is acceptable", "Review selected adapter DNS; use Recovery if worse"],
                _ => ["Refresh Defender protection status", "Read recent detections and resource paths", "Run an appropriate Defender scan", "Review Windows Protection History", "Keep protection enabled; do not add broad exclusions", "Escalate persistent findings to a trusted administrator"]
            };
            steps.Items.Clear(); steps.Items.AddRange(items);
            Output.Text = symptom.Text + "\r\n\r\n" + string.Join("\r\n", items.Select((s, i) => $"{i + 1}. {s}")) + "\r\n\r\nNearby events and individual measurements are clues, not proof. Do not run untrusted commands copied from logs or AI output.";
        });
        Controls.Add(steps); Controls.SetChildIndex(steps, 1);
    }
}
