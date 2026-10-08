using System.Security.Principal;
using System.Text;

namespace IgezziGuard.Shell;

/// <summary>The Performance area's report pages: GPU, CPU, Memory and Storage. Each is a few read-only checks with plain-language results.</summary>
internal static class SystemPages
{
    private static void Open(IShellServices shell, string uri, string manual) => HealthSettings.Open(shell.DialogOwner, uri, manual);

    internal static IReadOnlyList<ReportAction> GpuActions(IShellServices shell) => [
        new("Show graphics details", Diagnose: (_, token) => Task.Run(GpuPanel.Details, token), Primary: true),
        new("NVIDIA settings (technical)", Text: async (view, token) => { var text = await Task.Run(() => { try { return Nvidia.TechnicalReport([]); } catch (NvidiaException ex) { return ex.Message; } }, token); view.Result.Show(Diagnosis.From(text, [new("NVIDIA settings (technical)", "The raw values Hanki can read from the NVIDIA driver. Read-only.", CardStatus.Info)], "NVIDIA driver settings")); return "Report ready · " + DateTime.Now.ToString("t"); }),
        new("Windows Graphics settings", Open: () => Open(shell, "ms-settings:display-advancedgraphics", "Settings → System → Display → Graphics")),
    ];

    internal static IReadOnlyList<ReportAction> CpuActions(IShellServices shell) => [
        new("Check processor settings", Diagnose: async (view, token) => {
            var (cpu, _, _) = await SystemFactsProbe.Collect(token);
            var windows = await Task.Run(WindowsGamingProbe.Collect, token); var power = await Task.Run(GraphicsProbe.Power, token);
            var findings = SystemAnalyzers.Cpu(cpu, windows, power.Portable, power.OnAc, DateTimeOffset.UtcNow);
            void Review(DiagnosticResult f) {
                var plan = f.Metadata.GetValueOrDefault("plan");
                if (string.IsNullOrEmpty(plan)) return;
                _ = ChangeReview.ReviewAndApply(shell.DialogOwner, [new ProposedChange(f.FindingId, ChangeSource.Windows, "Maximum processor state (plugged in)", f.Metadata["current"], "100%", f.Explanation, false, "Processor power", plan + "|ac", "100")],
                    "Processor power", "Processor: maximum state 100%", text => view.Say(text));
            }
            return Diagnosis.From(PerformanceCards.Report("Processor", findings), findings.Select(f => PerformanceCards.Card(shell.DialogOwner, f, Review, post: a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a))).ToList(), PerformanceCards.Headline(findings, cpu.Name));
        }, Primary: true),
        new("Open Power & battery settings", Open: () => Open(shell, "ms-settings:powersleep", "Settings → System → Power & battery")),
    ];

    internal static IReadOnlyList<ReportAction> MemoryActions(IShellServices shell) => [
        new("Check memory health", Diagnose: async (_, token) => {
            var (_, memory, _) = await SystemFactsProbe.Collect(token);
            var findings = SystemAnalyzers.Memory(memory, DateTimeOffset.UtcNow);
            var extra = "Biggest memory users now:\r\n" + string.Join("\r\n", memory.TopProcesses.Select(p => $"  {p.Name}: {p.PrivateBytes / (1024d * 1024 * 1024):0.0} GB")) +
                "\r\nA program using a lot of memory isn't necessarily a problem; it matters when memory runs out.";
            return Diagnosis.From(PerformanceCards.Report("Memory", findings, extra), findings.Select(f => PerformanceCards.Card(shell.DialogOwner, f)).ToList(), PerformanceCards.Headline(findings, "Memory looks fine"));
        }, Primary: true),
    ];

    /// <summary>Storage keeps the facts of its last check so DirectStorage readiness and ReTrim can use them.</summary>
    internal sealed class StorageChecks(IShellServices shell)
    {
        private StorageFacts? last;
        internal IReadOnlyList<ReportAction> Actions => [
            new("Check storage", Diagnose: async (_, token) => {
                var (_, _, storage) = await SystemFactsProbe.Collect(token); last = storage;
                var findings = SystemAnalyzers.Storage(storage, GamesNow(), Path.GetPathRoot(Environment.SystemDirectory)?[0] ?? 'C', DateTimeOffset.UtcNow);
                return Diagnosis.From(PerformanceCards.Report("Storage", findings, string.Join("\r\n", storage.Notes)), findings.Select(f => PerformanceCards.Card(shell.DialogOwner, f)).ToList(), PerformanceCards.Headline(findings, "Storage looks fine"));
            }, Primary: true),
            new("DirectStorage readiness", Diagnose: async (_, token) => {
                last ??= (await SystemFactsProbe.Collect(token)).Storage;
                var lines = SystemAnalyzers.DirectStorage(last, GamesNow());
                return Diagnosis.From("DIRECTSTORAGE READINESS\r\n" + string.Join("\r\n", lines.Select(l => "• " + l)), lines.Select(l => new ResultCard("DirectStorage", l, CardStatus.Info)).ToList(), "DirectStorage readiness");
            }),
            new("ReTrim SSDs…", Text: (_, token) => ReTrim(token)),
            new("Optimize Drives", Open: () => DesktopShortcuts.Open(shell.DialogOwner, "defrag")),
        ];
        private static IReadOnlyList<GameEntry> GamesNow() => StoragePanel.Games();

        /// <summary>Windows' own ReTrim for SSD volumes: only for SSDs, only when it hasn't run recently, only as administrator and with approval.</summary>
        private async Task<string> ReTrim(CancellationToken token)
        {
            if (last is null) return "Check storage first, so Hanki knows which drives are SSDs.";
            if (last.OptimizeLastRun is { } recent && DateTimeOffset.UtcNow - recent < TimeSpan.FromDays(7)) return $"No change recommended: Windows optimized the drives on {recent.ToLocalTime():d}, so a ReTrim now wouldn't add anything.";
            var ssdVolumes = last.Volumes.Where(v => v.FileSystem is "NTFS" or "ReFS" && last.Disks.FirstOrDefault(d => d.Number == v.DiskNumber) is { } d && SystemAnalyzers.IsSsd(d)).Select(v => v.Letter).ToArray();
            if (ssdVolumes.Length == 0) return "No SSD drives with NTFS or ReFS were found, so there's nothing to trim.";
            using (var identity = WindowsIdentity.GetCurrent())
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return "ReTrim needs administrator rights. Close Hanki, run it as administrator and try again, or use Optimize Drives.";
            if (!ReviewPresenter.Ask($"Ask Windows to ReTrim {string.Join(", ", ssdVolumes.Select(l => l + ":"))}? This is the same operation Optimize Drives runs on a schedule. It tells the SSDs which blocks are free; it doesn't move or delete files. It can take a few minutes. Administrator rights are used only if Hanki was started as administrator.")) return "Nothing was changed.";
            var text = new StringBuilder("ReTrim\r\n");
            foreach (var letter in ssdVolumes) {
                try { await WindowsCommand.PowerShell($"Optimize-Volume -DriveLetter {letter} -ReTrim -ErrorAction Stop", token, 900); text.AppendLine($"✓ {letter}: trimmed."); }
                catch (IOException ex) { text.AppendLine($"✗ {letter}: {ex.Message}"); }
            }
            try {
                var none = new PerformanceMeasurement(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, double>(), "No measurement");
                PerformanceSessionsPanel.Store.Add(new PerformanceSession(Guid.NewGuid(), "Storage: ReTrim " + string.Join(" ", ssdVolumes.Select(l => l + ":")), DateTimeOffset.UtcNow, none, [], null, SessionOutcome.Measured, text.ToString()));
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            return text.ToString().Replace("\r\n", "\n").TrimEnd() + "\nThe result is kept in History → Performance sessions. ReTrim doesn't need undoing.";
        }
    }
}

/// <summary>GPU: graphics hardware, drivers, video memory and displays.</summary>
internal sealed class GpuPage : NativePage
{
    internal ReportView View { get; }
    internal IReadOnlyList<ReportAction> Actions { get; }
    internal GpuPage(IShellServices shell) { Actions = SystemPages.GpuActions(shell); View = new(shell, "GPU", "Your graphics hardware, driver versions, video memory and displays, and which vendor interfaces Hanki can use on this PC. Reading this changes nothing.", Actions); Content = View; }
}

/// <summary>Storage: drive types, free space, games on hard disks, TRIM and DirectStorage readiness.</summary>
internal sealed class StoragePage : NativePage
{
    internal ReportView View { get; }
    internal IReadOnlyList<ReportAction> Actions { get; }
    internal StoragePage(IShellServices shell)
    {
        Actions = new SystemPages.StorageChecks(shell).Actions;
        View = new(shell, "Storage", "Which drives are NVMe SSDs, SATA SSDs or hard disks, how full they are, which games sit on hard disks, and whether Windows keeps your SSDs trimmed. Faster storage speeds up loading and streaming, but not every game benefits.", Actions); Content = View;
    }
}

/// <summary>CPU: the processor's configuration check, and the Windows power plans.</summary>
internal sealed class CpuPage : NativePage
{
    private readonly SubTabs tabs = new();
    internal ReportView? Processor => tabs.ContentOf("Processor") as ReportView;
    internal string? CurrentTab => tabs.Current;
    internal CpuPage(IShellServices shell)
    {
        tabs.Add("Processor", "Processor", () => new ReportView(shell, "CPU", "How your processor is configured: cores and clocks, the power plan's processor limits on mains power and battery, and the Windows power mode. To see how busy each thread gets in a game, measure it in Performance Lab → Monitor.", SystemPages.CpuActions(shell)));
        tabs.Add("Power plans", "Power plans", () => new PowerPlansView(shell));
        Content = tabs;
    }
    internal override void OnShown() { if (tabs.Current is null) tabs.Select("Processor"); }
    internal override void OnRoute(IReadOnlyList<string> path) { if (path.Count > 1 && tabs.Keys.Contains(path[1])) tabs.Select(path[1]); }
}

/// <summary>Memory: the pagefile and commit page (still the existing page), and the memory health check.</summary>
internal sealed class MemoryPage : NativePage
{
    private readonly SubTabs tabs = new();
    internal ReportView? Health => tabs.ContentOf("Memory health") as ReportView;
    internal string? CurrentTab => tabs.Current;
    internal MemoryPage(IShellServices shell)
    {
        tabs.Add("Memory & pagefile", "Memory & pagefile", () => new ReportView(shell, "Memory", "A read-only snapshot of free memory, the apps using the most, drive space and the pagefile. Nothing is changed or uploaded. Reads memory counters, active pagefiles, configured pagefile/dump settings, fixed-drive free space and process working sets.", MemoryActions.Memory(shell)));
        tabs.Add("Memory health", "Memory health", () => new ReportView(shell, "Memory", "Your memory modules and the speed they run at, how much memory programs have reserved, and whether the pagefile is set up sensibly. Hanki doesn't change BIOS or pagefile settings for you; it points to where they are.", SystemPages.MemoryActions(shell)));
        Content = tabs;
    }
    internal override void OnShown() { if (tabs.Current is null) tabs.Select("Memory & pagefile"); }
    internal override void OnRoute(IReadOnlyList<string> path) { if (path.Count > 1 && tabs.Keys.Contains(path[1])) tabs.Select(path[1]); }
}
