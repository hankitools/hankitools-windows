using System.Diagnostics;
using System.Runtime.InteropServices;
namespace IgezziGuard;

/// <summary>Runs a measurement: counters every second, and frame times when a game is chosen (HANKI-PERF-301/303/309, HANKI-GAME-206).</summary>
internal static class PerformanceRecorder
{
    [DllImport("kernel32.dll")] private static extern bool GetPhysicallyInstalledSystemMemory(out ulong kilobytes);

    /// <summary>Measures for the given seconds. Stopping early keeps what was measured, with a note.</summary>
    internal static async Task<MonitorRun> Record(int seconds, Process? target, IProgress<string>? progress, CancellationToken token)
    {
        var graphics = GraphicsProbe.Collect();
        var gpu = graphics.HighPerformance;
        using var monitor = new SystemMonitor(gpu?.Luid);
        using var frames = target is null ? null : new FrameCapture(target.Id);
        var samples = new List<MonitorSample>(seconds);
        var started = DateTimeOffset.UtcNow; var notes = new List<string>(monitor.Notes);
        try {
            await Task.Delay(1000, token);
            for (int i = 0; i < seconds; i++) {
                if (target is not null && target.HasExited) { notes.Add($"{target.ProcessName} closed after {i} seconds; the measurement stopped there."); break; }
                var sample = monitor.Sample(target?.Id);
                samples.Add(sample);
                string gpuText = (sample.TargetGpuBusy ?? sample.GpuBusy) is { } g ? $"{g:0}%" : "not reported";
                string tempText = sample.GpuTemperature is { } t ? $" at {t:0} °C" : "";
                int frameCount = frames?.Presents().Count ?? 0;
                progress?.Report($"Measuring: {i + 1} of {seconds} seconds.\r\nCPU {sample.CpuTotal:0}% (busiest thread {sample.BusiestCore:0}%) · GPU {gpuText}{tempText} · free memory {sample.AvailableMb / 1024:0.0} GB · disk {sample.DiskActivePercent:0}% busy" +
                    (frameCount > 0 ? $" · {frameCount} frames so far" : "") + "\r\n\r\nKeep playing or working as usual. Stop keeps what has been measured.");
                await Task.Delay(1000, token);
            }
        } catch (OperationCanceledException) { notes.Add($"Stopped early after {samples.Count} seconds."); }
        FrameStats? stats = null;
        if (frames is not null) {
            if (frames.Unavailable is { } why) notes.Add(why);
            else if ((stats = BottleneckEngine.Frames(frames.Presents())) is null)
                notes.Add($"No DirectX frames were seen from {target!.ProcessName}. It may use Vulkan or OpenGL, or it wasn't rendering during the measurement.");
        }
        double? maxClock = monitor.GpuMaxClockMhz() ?? samples.Select(s => s.GpuClockMhz).Where(c => c is not null).DefaultIfEmpty(null).Max();
        ulong installed = GetPhysicallyInstalledSystemMemory(out var kb) ? kb / 1024 : 0;
        double? refresh = graphics.Displays.Where(d => d.Primary).Select(d => (double?)d.Current.RefreshHz).FirstOrDefault();
        return new MonitorRun(started, DateTimeOffset.UtcNow, target?.ProcessName, target?.Id, samples, stats,
            gpu is null ? null : gpu.DedicatedMemory / (1024d * 1024), maxClock, installed, refresh, notes.Distinct().ToArray());
    }

    /// <summary>Running programs with a window, for choosing what to measure; games from the library first.</summary>
    internal static IReadOnlyList<Process> Candidates(IEnumerable<GameEntry> games)
    {
        var gameExes = games.Select(g => Path.GetFileNameWithoutExtension(g.Executable)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<Process>();
        foreach (var process in Process.GetProcesses()) {
            try { if (process.MainWindowHandle != IntPtr.Zero && process.Id != Environment.ProcessId) { result.Add(process); continue; } } catch (InvalidOperationException) { }
            process.Dispose();
        }
        return result.OrderByDescending(p => gameExes.Contains(p.ProcessName)).ThenBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>A plain report of a run: observations, then the per-second table, so the raw data stays visible.</summary>
    internal static string Report(MonitorRun run)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"Measured {run.Samples.Count} seconds from {run.Started.ToLocalTime():g}{(run.Target is { } t ? $" while {t} was running" : "")}.");
        if (run.Frames is { } f) text.AppendLine($"Frames: {f.Count} in {f.Seconds:0} s · average {f.AverageFps:0.0} FPS · 1% low {f.Low1Fps:0.0} FPS · average frame {f.AverageFrameMs:0.0} ms · 99th percentile {f.P99FrameMs:0.0} ms");
        foreach (var note in run.Notes) text.AppendLine("Note: " + note);
        text.AppendLine();
        text.AppendLine("Time      CPU  Busiest  GPU   GPU°C  VRAM GB  Free GB  Commit  Faults/s  Disk%  Top process");
        static string Cell(double? value, int width, string format) => value is { } v ? v.ToString(format).PadLeft(width) : "–".PadLeft(width);
        foreach (var s in run.Samples) {
            var top = s.TopProcesses.FirstOrDefault(p => p.Pid != run.TargetPid);
            string process = top is null ? "" : $"{top.Name} {top.CpuPercent:0}% cpu {top.IoMBps:0} MB/s";
            text.AppendLine($"{s.At.ToLocalTime():HH:mm:ss}  {s.CpuTotal,3:0}  {s.BusiestCore,7:0}  {Cell(s.TargetGpuBusy ?? s.GpuBusy, 3, "0")}   {Cell(s.GpuTemperature, 5, "0")}  {Cell(s.GpuVramUsedMb / 1024, 7, "0.0")}  {s.AvailableMb / 1024,7:0.0}  {s.CommitPercent,6:0}  {s.HardFaultsPerSecond,8:0}  {s.DiskActivePercent,5:0}  {process}");
        }
        return text.ToString();
    }
}
