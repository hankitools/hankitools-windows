using System.Security.Principal;
using System.Text;
namespace IgezziGuard;

/// <summary>Shared card building for the CPU, Memory and Storage checks.</summary>
internal static class PerformanceCards
{
    internal static ResultCard Card(Control owner, DiagnosticResult f, Action<DiagnosticResult>? review = null)
    {
        string body = f.Explanation + (f.Severity is FindingSeverity.Warning or FindingSeverity.Critical && f.Metadata.GetValueOrDefault("recommended") is { Length: > 0 } rec && rec != f.Metadata.GetValueOrDefault("current")
            ? $"\r\nNow: {f.Metadata.GetValueOrDefault("current")} · Recommended: {rec}" : "") + (f.Recommendation is { Length: > 0 } how && f.Severity is FindingSeverity.Warning ? "\r\n" + how : "");
        var remedy = f.Metadata.GetValueOrDefault("remedy");
        if (remedy == GamingHealth.RemedyProcessor && review is not null) return new(f.Title, body, GamingState.Status(f), "Review change", () => owner.BeginInvoke(() => review(f)));
        if (f.Metadata.GetValueOrDefault("settings") is { } uri)
            return new(f.Title, body, GamingState.Status(f), "Open settings", uri == "pagefile" ? () => DesktopShortcuts.Open(owner, "pagefile") : () => HealthSettings.Open(owner, uri, f.Title));
        return new(f.Title, body, GamingState.Status(f));
    }
    internal static string Report(string heading, IEnumerable<DiagnosticResult> findings, string extra = "") =>
        $"{heading} • {DateTimeOffset.Now:g}\r\nRead-only; nothing was changed.\r\n\r\n" + string.Join("\r\n\r\n", findings.Select(f => $"{f.Title} · {f.Severity}\r\n{f.Explanation}\r\n{f.Evidence}")) + (extra.Length > 0 ? "\r\n\r\n" + extra : "");
    internal static string Headline(IReadOnlyList<DiagnosticResult> findings, string fine)
    {
        int count = findings.Count(f => f.Severity is FindingSeverity.Warning or FindingSeverity.Critical);
        return count == 0 ? fine : $"{count} optimization {(count == 1 ? "opportunity" : "opportunities")} found";
    }
}

/// <summary>Performance → CPU → Processor: model, clocks and power configuration (HANKI-PERF-301/302).</summary>
public sealed class CpuPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    public CpuPanel() : base("How your processor is configured: cores and clocks, the power plan's processor limits on mains power and battery, and the Windows power mode. To see how busy each thread gets in a game, measure it in Performance Lab → Monitor. Changes are only made after you review them.")
    {
        Button("Check processor settings", async () => await Run(async token => {
            var (cpu, _, _) = await SystemFactsProbe.Collect(token);
            var windows = WindowsGamingProbe.Collect(); var power = GraphicsProbe.Power();
            var findings = SystemAnalyzers.Cpu(cpu, windows, power.Portable, power.OnAc, DateTimeOffset.UtcNow);
            return Diagnosis.From(PerformanceCards.Report("Processor", findings), findings.Select(f => PerformanceCards.Card(this, f, Review)).ToList(), PerformanceCards.Headline(findings, cpu.Name));
        }));
        Button("Open Power & battery settings", () => HealthSettings.Open(this, "ms-settings:powersleep", "Settings → System → Power & battery"));
    }
    private void Review(DiagnosticResult f)
    {
        var plan = f.Metadata.GetValueOrDefault("plan");
        if (string.IsNullOrEmpty(plan)) return;
        _ = ChangeReview.ReviewAndApply(this, [new ProposedChange(f.FindingId, ChangeSource.Windows, "Maximum processor state (plugged in)", f.Metadata["current"], "100%", f.Explanation, false, "Processor power", plan + "|ac", "100")],
            "Processor power", "Processor: maximum state 100%", text => Output.Text = text);
    }
}

/// <summary>Performance → Memory → Memory health: modules and speed, commit headroom and the pagefile (HANKI-PERF-303/304/305).</summary>
public sealed class MemoryHealthPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    public MemoryHealthPanel() : base("Your memory modules and the speed they run at, how much memory programs have reserved, and whether the pagefile is set up sensibly. Hanki doesn't change BIOS or pagefile settings for you; it points to where they are. For memory pressure while gaming, measure in Performance Lab → Monitor.")
    {
        Button("Check memory health", async () => await Run(async token => {
            var (_, memory, _) = await SystemFactsProbe.Collect(token);
            var findings = SystemAnalyzers.Memory(memory, DateTimeOffset.UtcNow);
            var extra = "Biggest memory users now:\r\n" + string.Join("\r\n", memory.TopProcesses.Select(p => $"  {p.Name}: {p.PrivateBytes / (1024d * 1024 * 1024):0.0} GB")) +
                "\r\nA program using a lot of memory isn't necessarily a problem; it matters when memory runs out.";
            return Diagnosis.From(PerformanceCards.Report("Memory", findings, extra), findings.Select(f => PerformanceCards.Card(this, f)).ToList(), PerformanceCards.Headline(findings, "Memory looks fine"));
        }));
    }
}

/// <summary>Performance → Storage: drive types, free space, games on hard disks, TRIM and DirectStorage readiness (HANKI-PERF-306/307/308).</summary>
public sealed class StoragePanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    private StorageFacts? last;
    public StoragePanel() : base("Which drives are NVMe SSDs, SATA SSDs or hard disks, how full they are, which games sit on hard disks, and whether Windows keeps your SSDs trimmed. Faster storage speeds up loading and streaming, but not every game benefits equally. Nothing is moved or changed.")
    {
        Button("Check storage", async () => await Run(async token => {
            var (_, _, storage) = await SystemFactsProbe.Collect(token);
            last = storage;
            var findings = SystemAnalyzers.Storage(storage, Games(), Path.GetPathRoot(Environment.SystemDirectory)?[0] ?? 'C', DateTimeOffset.UtcNow);
            return Diagnosis.From(PerformanceCards.Report("Storage", findings, string.Join("\r\n", storage.Notes)), findings.Select(f => PerformanceCards.Card(this, f)).ToList(), PerformanceCards.Headline(findings, "Storage looks fine"));
        }));
        Button("DirectStorage readiness", async () => await Run(async token => {
            last ??= (await SystemFactsProbe.Collect(token)).Storage;
            return "DIRECTSTORAGE READINESS\r\n" + string.Join("\r\n", SystemAnalyzers.DirectStorage(last, Games()).Select(l => "• " + l));
        }));
        Button("ReTrim SSDs…", ReTrim);
        Button("Optimize Drives", () => DesktopShortcuts.Open(this, "defrag"));
    }
    private static IReadOnlyList<GameEntry> Games() { try { return GameLibrary.Read(GameLibrary.StorePath); } catch (IOException) { return []; } }

    /// <summary>Windows' own ReTrim for SSD volumes (HANKI-PERF-307): only for SSDs, only when it hasn't run recently, only with approval.</summary>
    private async void ReTrim()
    {
        if (last is null) { Output.Text = "Check storage first, so Hanki knows which drives are SSDs."; return; }
        if (last.OptimizeLastRun is { } recent && DateTimeOffset.UtcNow - recent < TimeSpan.FromDays(7)) { Output.Text = $"No change recommended: Windows optimized the drives on {recent.ToLocalTime():d}, so a ReTrim now wouldn't add anything."; return; }
        var ssdVolumes = last.Volumes.Where(v => v.FileSystem is "NTFS" or "ReFS" && last.Disks.FirstOrDefault(d => d.Number == v.DiskNumber) is { } d && SystemAnalyzers.IsSsd(d)).Select(v => v.Letter).ToArray();
        if (ssdVolumes.Length == 0) { Output.Text = "No SSD drives with NTFS or ReFS were found, so there's nothing to trim."; return; }
        using (var identity = WindowsIdentity.GetCurrent())
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) { Output.Text = "ReTrim needs administrator rights. Close Hanki, run it as administrator and try again, or use Optimize Drives."; return; }
        if (!Review($"Ask Windows to ReTrim {string.Join(", ", ssdVolumes.Select(l => l + ":"))}? This is the same operation Optimize Drives runs on a schedule. It tells the SSDs which blocks are free; it doesn't move or delete files. It can take a few minutes.")) return;
        await Run(async token => {
            var text = new StringBuilder("ReTrim\r\n");
            foreach (var letter in ssdVolumes) {
                try { await WindowsCommand.PowerShell($"Optimize-Volume -DriveLetter {letter} -ReTrim -ErrorAction Stop", token, 900); text.AppendLine($"✓ {letter}: trimmed."); }
                catch (IOException ex) { text.AppendLine($"✗ {letter}: {ex.Message}"); }
            }
            try {
                var none = new PerformanceMeasurement(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, double>(), "No measurement");
                PerformanceSessionsPanel.Store.Add(new PerformanceSession(Guid.NewGuid(), "Storage: ReTrim " + string.Join(" ", ssdVolumes.Select(l => l + ":")), DateTimeOffset.UtcNow, none, [], null, SessionOutcome.Measured, text.ToString()));
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            return text + "\r\nThe result is kept in History → Performance sessions. ReTrim doesn't need undoing.";
        });
    }
}
