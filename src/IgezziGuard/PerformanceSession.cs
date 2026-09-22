using System.ComponentModel;
using System.Runtime.InteropServices;

namespace IgezziGuard;

internal static class PerformanceSession
{
    public static async Task<PerformanceRun> Sample(CancellationToken token, int seconds = 30, IProgress<string>? progress = null)
    {
        if (seconds is < 30 or > 900) throw new ArgumentOutOfRangeException(nameof(seconds));
        var started = DateTimeOffset.Now;
        var cpus = new List<double>(); var commits = new List<double>(); ulong minimum = ulong.MaxValue;
        ReadCpu(out var idle, out var kernel, out var user);
        for (int i = 0; i < seconds; i++)
        {
            await Task.Delay(1000, token);
            ReadCpu(out var nextIdle, out var nextKernel, out var nextUser);
            var cpu = DiagnosticRules.CpuPercent(idle, kernel, user, nextIdle, nextKernel, nextUser);
            if (!cpu.HasValue) throw new IOException("CPU sampling counter discontinuity; session discarded.");
            cpus.Add(cpu.Value); idle = nextIdle; kernel = nextKernel; user = nextUser;
            var m = new Memory { Size = (uint)Marshal.SizeOf<Memory>() };
            if (!GetPerformanceInfo(out m, m.Size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (m.CommitLimit.ToUInt64() == 0) throw new IOException("Commit limit unavailable.");
            commits.Add(100d * m.CommitTotal.ToUInt64() / m.CommitLimit.ToUInt64());
            minimum = Math.Min(minimum, checked(m.Available.ToUInt64() * m.PageSize.ToUInt64()));
            if (i % 5 == 0 || i == seconds - 1) progress?.Report($"Collecting: {i + 1}/{seconds} CPU/memory samples. Latest CPU {cpu.Value:0.0}%, commit {commits[^1]:0.0}%.\r\nDevice counters run separately; final report follows when both collectors finish. Cancel discards incomplete runs.");
        }
        return new(started, DateTimeOffset.Now, cpus.Count, cpus.Average(), cpus.Max(), commits.Average(), commits.Max(), minimum);
    }
    public static string Describe(PerformanceRun run) => $"{run.Started:O} to {run.Ended:O}\r\n{run.Samples} samples, about 1 second apart.\r\n" +
        $"CPU mean/peak: {run.AverageCpu:0.0}% / {run.PeakCpu:0.0}%\r\nCommit mean/peak: {run.AverageCommitPercent:0.0}% / {run.PeakCommitPercent:0.0}% of contemporaneous limit\r\n" +
        $"Minimum available RAM: {run.MinimumAvailableRam / (1024d * 1024 * 1024):0.00} GiB\r\n";
    private static void ReadCpu(out ulong idle, out ulong kernel, out ulong user)
    { if (!GetSystemTimes(out idle, out kernel, out user)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("psapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(out Memory memory, uint size);
    [StructLayout(LayoutKind.Sequential)] private struct Memory {
        public uint Size;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, Available, Cache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint Handles, Processes, Threads;
    }
}

public sealed class SamplingPanel : UserControl
{
    private PerformanceRun? baseline;
    private readonly DiagnosticPanel panel;
    public bool IsBusy => panel.IsBusy;
    public void Cancel() => panel.Cancel();
    public event Action<string>? PrepareRequested;
    public SamplingPanel()
    {
        Dock = DockStyle.Fill;
        panel = new DiagnosticPanel("Sample CPU / memory (30s)",
            "First completed session becomes the baseline. Later sessions are compared with it. Repeat the same workload for meaningful comparison.\r\n" +
            "Local read-only sampling; no tuning, process termination or upload. Disk/GPU counters are not collected. CPU readings on systems with over 64 logical processors may cover only the calling processor group.", async token => {
                var current = await PerformanceSession.Sample(token);
                if (baseline is null) { baseline = current; return "BASELINE\r\n" + PerformanceSession.Describe(current) + "\r\nRun another session for comparison. Baseline exists only until reset/app exit."; }
                return "BASELINE\r\n" + PerformanceSession.Describe(baseline) + "\r\nCURRENT\r\n" + PerformanceSession.Describe(current) +
                    $"\r\nChange in mean CPU: {current.AverageCpu - baseline.AverageCpu:+0.0;-0.0;0.0} percentage points\r\n" +
                    $"Change in mean commit: {current.AverageCommitPercent - baseline.AverageCommitPercent:+0.0;-0.0;0.0} percentage points\r\n" +
                    "These are observations, not proof that a tweak helped. Workload differences, pagefile growth, cache, background activity and thermal conditions affect comparisons. Thirty seconds is a short observation window; repeat under representative load. No settings changed.";
            });
        panel.PrepareRequested += text => PrepareRequested?.Invoke(text);
        var reset = new HankiButton { Text = "Reset baseline (next completed run becomes baseline)", Dock = DockStyle.Bottom, Height = 40 };
        reset.Click += (_, _) => { if (!panel.IsBusy) { baseline = null; MessageBox.Show(this, "Baseline reset. Existing displayed report is historical.", "Hanki Performance"); } };
        Controls.Add(panel); Controls.Add(reset);
    }
}
