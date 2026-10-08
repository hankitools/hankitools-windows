namespace IgezziGuard;

/// <summary>A process's share of the PC in one sample: CPU as a percentage of the whole machine, disk traffic, private memory.</summary>
public sealed record ProcessActivity(int Pid, string Name, double CpuPercent, double IoMBps, double PrivateMb);

/// <summary>One second of measurements. Null means Windows didn't report that value on this PC.</summary>
/// <param name="NetworkReceiveMBps">Data arriving over all network adapters; null in runs saved before Hanki measured it.</param>
public sealed record MonitorSample(DateTimeOffset At, double CpuTotal, IReadOnlyList<double> Cores, double? CpuPerformancePercent, double? CpuEffectiveMhz,
    double AvailableMb, double CommitPercent, double HardFaultsPerSecond, double DiskActivePercent, double? DiskLatencyMs, double DiskReadMBps, double DiskWriteMBps,
    double? GpuBusy, double? GpuVramUsedMb, double? GpuTemperature, double? GpuClockMhz, double? TargetGpuBusy, IReadOnlyList<ProcessActivity> TopProcesses,
    double? NetworkReceiveMBps = null)
{
    public double BusiestCore => Cores.Count == 0 ? CpuTotal : Cores.Max();
}

/// <summary>Frame pacing for one process, from DirectX present events.</summary>
public sealed record FrameStats(int Count, double Seconds, double AverageFps, double Low1Fps, double AverageFrameMs, double P99FrameMs, IReadOnlyList<(double AtSeconds, double FrameMs)> FrameTimes);

/// <param name="GpuVramTotalMb">Dedicated video memory of the measured GPU, for pressure checks.</param>
/// <param name="GpuMaxClockMhz">The GPU's highest clock seen or reported, for throttling checks.</param>
public sealed record MonitorRun(DateTimeOffset Started, DateTimeOffset Ended, string? Target, int? TargetPid, IReadOnlyList<MonitorSample> Samples,
    FrameStats? Frames, double? GpuVramTotalMb, double? GpuMaxClockMhz, double InstalledMemoryMb, double? RefreshHz, IReadOnlyList<string> Notes);

public enum Limiter { Gpu, Cpu, Vram, Memory, Storage, Thermal, Background, Mixed, None, Insufficient }

/// <summary>A bottleneck verdict with the measurements behind it (HANKI-PERF-310, HANKI-GAME-206).</summary>
public sealed record Bottleneck(Limiter Limiter, string Confidence, string Diagnosis, IReadOnlyList<string> Observed, IReadOnlyList<string> Reasoning, IReadOnlyList<PerformanceRecommendation> Recommendations);

/// <param name="Evidence">The measurement that triggered it (HANKI-PERF-311: every recommendation links back to evidence).</param>
public sealed record PerformanceRecommendation(string Action, string Benefit, string Downside, string Evidence, bool Riskier = false);

/// <summary>
/// The Unified Bottleneck Engine. It combines CPU, GPU, memory, storage, thermal and background measurements and
/// only names a limiter when several signals agree. Thresholds are documented here, and the observations are always
/// shown next to the conclusion. Thin evidence gives "no clear bottleneck" or "not enough data", never a guess.
/// </summary>
public static class BottleneckEngine
{
    public const int MinimumSamples = 20;
    public static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return 0;
        double rank = p / 100 * (sorted.Length - 1); int low = (int)Math.Floor(rank), high = (int)Math.Ceiling(rank);
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }
    private static double? Average(IEnumerable<double?> values) { var v = values.Where(x => x is not null).Select(x => x!.Value).ToArray(); return v.Length == 0 ? null : v.Average(); }
    private static double? Max(IEnumerable<double?> values) { var v = values.Where(x => x is not null).Select(x => x!.Value).ToArray(); return v.Length == 0 ? null : v.Max(); }

    public static Bottleneck Analyze(MonitorRun run)
    {
        var s = run.Samples;
        if (s.Count < MinimumSamples)
            return new(Limiter.Insufficient, "None", $"Not enough data: {s.Count} seconds measured, and at least {MinimumSamples} are needed for a fair reading.", [], [], []);
        double? gpu = Average(s.Select(x => x.TargetGpuBusy ?? x.GpuBusy));
        double gpuHigh = Percentile(s.Select(x => x.TargetGpuBusy ?? x.GpuBusy ?? 0), 50);
        double busiestCore = Percentile(s.Select(x => x.BusiestCore), 90), cpuTotal = s.Average(x => x.CpuTotal);
        double availableMin = s.Min(x => x.AvailableMb), commitMax = s.Max(x => x.CommitPercent), faults = Percentile(s.Select(x => x.HardFaultsPerSecond), 90);
        double diskBusyShare = s.Count(x => x.DiskActivePercent >= 90) / (double)s.Count;
        double? vram = Max(s.Select(x => x.GpuVramUsedMb)), temp = Max(s.Select(x => x.GpuTemperature));
        double? clockLow = s.Where(x => x.GpuClockMhz is not null && (x.TargetGpuBusy ?? x.GpuBusy) >= 80).Select(x => x.GpuClockMhz!.Value).DefaultIfEmpty().Min() is var c && c > 0 ? c : null;
        double? cpuPerf = s.Where(x => x.CpuPerformancePercent is not null && x.BusiestCore >= 80).Select(x => x.CpuPerformancePercent!.Value).DefaultIfEmpty().Average() is var p && p > 0 ? p : null;
        var background = s.SelectMany(x => x.TopProcesses.Where(t => t.Pid != run.TargetPid)).GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Cpu: g.Sum(t => t.CpuPercent) / s.Count, Io: g.Sum(t => t.IoMBps) / s.Count)).OrderByDescending(g => g.Cpu + g.Io / 10).FirstOrDefault();

        var observed = new List<string> {
            gpu is { } g ? $"GPU load: {g:0}% on average" : "GPU load: not reported on this PC",
            $"Busiest CPU thread: {busiestCore:0}% most of the time (all cores: {cpuTotal:0}% on average)",
            $"Free memory: at least {availableMin / 1024:0.0} GB; memory committed up to {commitMax:0}%{(faults > 50 ? $"; hard page faults up to {faults:0}/s" : "")}",
            $"Disk: 90–100% busy in {diskBusyShare:P0} of seconds"
        };
        var network = s.Where(x => x.NetworkReceiveMBps is not null).ToArray();
        if (network.Length > 0) {
            int downloading = network.Count(x => x.NetworkReceiveMBps >= DownloadMBps);
            observed.Add(downloading == 0 ? "Downloads: none (network traffic stayed low)"
                : $"Downloads: {downloading} of {network.Length} seconds above {DownloadMBps:0} MB/s, up to {network.Max(x => x.NetworkReceiveMBps!.Value):0.#} MB/s");
        }
        if (vram is { } used && run.GpuVramTotalMb is { } total) observed.Add($"Video memory: up to {used / 1024:0.0} of {total / 1024:0.0} GB");
        if (temp is { } t) observed.Add($"GPU temperature: up to {t:0} °C");
        if (run.Frames is { } f) observed.Add($"Frame rate: {f.AverageFps:0} FPS on average, 1% low {f.Low1Fps:0} FPS");

        var found = new List<(Limiter Limiter, string Confidence, string Reason)>();
        bool gpuReported = gpu is not null;
        if (gpuReported && gpuHigh >= 95)
            found.Add((Limiter.Gpu, gpuHigh >= 97 && busiestCore < 95 ? "High" : "Medium", $"The GPU stays {gpuHigh:0}% busy or more half of the time, so it sets the pace."));
        if (busiestCore >= 90 && (!gpuReported || gpuHigh < 85))
            found.Add((Limiter.Cpu, gpuReported && gpuHigh < 75 && busiestCore >= 95 ? "High" : gpuReported ? "Medium" : "Low",
                cpuTotal >= 85 ? $"All CPU cores are busy ({cpuTotal:0}% on average) while the GPU has spare capacity."
                    : $"One CPU thread is saturated ({busiestCore:0}%) while the GPU has spare capacity{(gpuReported ? $" ({gpuHigh:0}%)" : "")}. Total CPU load ({cpuTotal:0}%) looks low because the other cores wait on that thread."));
        if (vram is { } v && run.GpuVramTotalMb is { } vt && v >= vt * 0.95)
            found.Add((Limiter.Vram, "Medium", $"Video memory is nearly full ({v / 1024:0.0} of {vt / 1024:0.0} GB); textures may be streamed from system memory."));
        if ((availableMin < 700 || commitMax >= 92) && faults >= 300)
            found.Add((Limiter.Memory, availableMin < 400 ? "High" : "Medium", $"Free memory dropped to {availableMin:0} MB and Windows read {faults:0} pages per second from disk."));
        if (diskBusyShare >= 0.25)
            found.Add((Limiter.Storage, diskBusyShare >= 0.5 ? "Medium" : "Low", $"The disk was fully busy in {diskBusyShare:P0} of the seconds measured."));
        if (temp is >= 83 && clockLow is { } lowClock && run.GpuMaxClockMhz is { } maxClock && lowClock < maxClock * 0.85)
            found.Add((Limiter.Thermal, "Medium", $"The GPU reached {temp:0} °C and its clock fell to {lowClock:0} MHz of {maxClock:0} MHz while busy."));
        if (cpuPerf is < 80 && busiestCore >= 80)
            found.Add((Limiter.Thermal, "Low", $"The processor ran at {cpuPerf:0}% of its rated performance while busy, which can mean power or thermal limits."));
        if (background.Name is not null && (background.Cpu >= 15 || background.Io >= 50))
            found.Add((Limiter.Background, "Low", $"{background.Name} used {background.Cpu:0}% of the CPU and {background.Io:0} MB/s of disk on average at the same time."));

        var recommendations = new List<PerformanceRecommendation>();
        foreach (var (limiter, _, reason) in found) recommendations.AddRange(Recommend(limiter, reason, run));
        if (found.Count == 0) {
            bool capped = run.Frames is { } fr && run.RefreshHz is { } hz && Math.Abs(fr.AverageFps - hz) <= hz * 0.05;
            return new(Limiter.None, gpuReported ? "Medium" : "Low", capped
                    ? $"No hardware limit: the game runs at about the display's {run.RefreshHz:0} Hz, so vertical sync or a frame cap is likely what holds it there."
                    : "No clear bottleneck: neither the CPU nor the GPU was consistently at its limit. The game may be capped by a frame limit or vertical sync, or it was waiting on something else.",
                observed, ["No measurement crossed a limit consistently."], [new PerformanceRecommendation("No change recommended", "Settings that lower CPU or GPU load won't raise the frame rate here.", "None", "No limit was reached.")]);
        }
        var primary = found.OrderBy(f => f.Limiter == Limiter.Background ? 1 : 0).ThenByDescending(f => f.Confidence == "High" ? 2 : f.Confidence == "Medium" ? 1 : 0).First();
        bool mixed = found.Count(f => f.Limiter is not Limiter.Background) > 1 && found.Where(f => f.Limiter is not Limiter.Background).Select(f => f.Limiter).Distinct().Count() > 1;
        var limiterName = primary.Limiter switch {
            Limiter.Gpu => "GPU-limited", Limiter.Cpu => "CPU-limited", Limiter.Vram => "limited by video memory", Limiter.Memory => "limited by system memory",
            Limiter.Storage => "limited by the disk", Limiter.Thermal => "held back by power or temperature limits", _ => "slowed by background activity"
        };
        return new(mixed ? Limiter.Mixed : primary.Limiter, mixed ? "Low" : primary.Confidence,
            mixed ? $"Mixed: several limits showed up ({string.Join(", ", found.Select(f => f.Limiter).Distinct())}); the most consistent is that it's likely {limiterName}." : $"Likely {limiterName}.",
            observed, found.Select(f => f.Reason).ToArray(), recommendations);
    }

    private static IEnumerable<PerformanceRecommendation> Recommend(Limiter limiter, string evidence, MonitorRun run) => limiter switch {
        Limiter.Gpu => [
            new("Lower GPU-heavy settings: resolution, ray tracing, shadows, volumetric effects.", "Higher frame rate.", "Lower image quality.", evidence),
            new("Use the game's upscaling (DLSS, FSR or XeSS) if it has it.", "A large frame-rate gain for a small quality cost.", "Some softness or artefacts.", evidence),
            new("Don't lower CPU-heavy settings such as view distance first.", "Avoids losing detail for no gain.", "None.", evidence)],
        Limiter.Cpu => [
            new("Lower CPU-heavy settings: view distance, crowd or NPC density, simulation and physics detail.", "Higher and steadier frame rate.", "Less detail in the distance or in crowds.", evidence),
            new("Don't lower texture quality or resolution for this.", "Keeps image quality; the GPU isn't the limit.", "None.", evidence),
            new("Close programs using the CPU in the background.", "Frees processor time for the game.", "None.", evidence)],
        Limiter.Vram => [new("Lower texture quality or resolution one step.", "Removes stutter from texture streaming.", "Slightly blurrier textures.", evidence)],
        Limiter.Memory => [
            new("Close large programs (browsers with many tabs, editors) while playing.", "Fewer pauses while Windows reads memory back from disk.", "None.", evidence),
            new("Check that the pagefile is system-managed (Memory page).", "Avoids running out of committed memory.", "Uses some disk space.", evidence),
            new("Consider more RAM if this happens with little else open.", "Removes the limit for good.", "Cost.", evidence)],
        Limiter.Storage => [
            new("Install the game on an SSD, ideally NVMe (Storage page).", "Faster loading and fewer streaming stutters.", "Needs free SSD space.", evidence),
            new("Pause downloads, cloud sync or updates while playing.", "Frees the disk for the game.", "They finish later.", evidence)],
        Limiter.Thermal => [
            new("Check cooling: dust in fans and filters, airflow around the PC, laptop vents not blocked.", "Restores full clocks.", "None.", evidence),
            new("Don't change unrelated Windows settings for this.", "Avoids changes that can't help.", "None.", evidence)],
        _ => [new("Pause the background program while playing, or schedule it for later.", "Frees CPU or disk time for the game.", "It runs later.", evidence)]
    };

    // ---- Stutter (HANKI-GAME-209) ---------------------------------------------------------------------------
    /// <summary>
    /// Frame-time spikes (over 2.5 times the median frame time) matched against the same second's measurements. A cause
    /// is only suggested when a measurement shows it at most spikes, and always as a possibility.
    /// </summary>
    public static IReadOnlyList<string> Stutter(MonitorRun run)
    {
        if (run.Frames is not { } f || f.FrameTimes.Count < 100) return ["Stutter analysis needs frame times, which Hanki records for DirectX games when it runs as administrator. Measure while the game is running."];
        double median = Percentile(f.FrameTimes.Select(x => x.FrameMs), 50);
        var spikes = f.FrameTimes.Where(x => x.FrameMs > Math.Max(median * 2.5, median + 8)).ToArray();
        var notes = new List<string>();
        if (spikes.Length == 0 || spikes.Length < f.FrameTimes.Count / 1000.0) { notes.Add($"No stutter to speak of: frame times stayed close to the typical {median:0.0} ms."); return notes; }
        notes.Add($"{spikes.Length} frame-time spikes (over {Math.Max(median * 2.5, median + 8):0} ms, typical frame {median:0.0} ms), about {spikes.Length / Math.Max(1, f.Seconds / 60):0.#} per minute.");
        var matched = spikes.Select(sp => run.Samples.OrderBy(x => Math.Abs((x.At - run.Started).TotalSeconds - sp.AtSeconds)).First()).ToArray();
        double Share(Func<MonitorSample, bool> test) => matched.Count(test) / (double)matched.Length;
        double gpuTypical = Percentile(run.Samples.Select(x => x.TargetGpuBusy ?? x.GpuBusy ?? 0), 50);
        if (Share(x => x.BusiestCore >= 95) >= 0.6) notes.Add("Possible CPU spikes: at most spikes, one CPU thread was fully busy.");
        if (gpuTypical >= 80 && Share(x => (x.TargetGpuBusy ?? x.GpuBusy) < gpuTypical - 25) >= 0.6)
            notes.Add("Possible shader compilation or loading: GPU load dropped at the spikes while the game waited. This is common just after a driver or game update and settles as shaders are cached; let it finish before changing settings.");
        if (run.GpuVramTotalMb is { } total && Share(x => x.GpuVramUsedMb >= total * 0.95) >= 0.6) notes.Add("Possible video-memory pressure: video memory was nearly full at most spikes. Lower texture quality.");
        if (Share(x => x.HardFaultsPerSecond >= 300) >= 0.5) notes.Add("Possible memory pressure: Windows was reading memory back from disk at most spikes.");
        if (Share(x => x.DiskActivePercent >= 90) >= 0.5) notes.Add("Possible disk bottleneck: the disk was fully busy at most spikes.");
        if (Share(x => x.GpuTemperature >= 83) >= 0.6) notes.Add("Possible thermal throttling: the GPU was at 83 °C or more at most spikes; check cooling.");
        if (Share(x => x.NetworkReceiveMBps >= DownloadMBps) >= 0.6)
            notes.Add($"Possible background download: something was downloading at most spikes{(Downloader(run) is { } who ? $", most likely {who}" : "")}. Pause downloads and updates while you play.");
        if (notes.Count == 1) notes.Add("No single measurement lines up with the spikes, so Hanki doesn't name a cause.");
        return notes;
    }

    // ---- Background interference (HANKI-PERF-309) ------------------------------------------------------------
    /// <summary>Known background workloads by process name, to say what a busy process is.</summary>
    public static string? Category(string process) => process.ToLowerInvariant() switch {
        "msmpeng" or "mpdefendercoreservice" => "Microsoft Defender scan",
        "tiworker" or "trustedinstaller" or "wuauclt" or "usoclient" or "musnotification" or "mousocoreworker" => "Windows Update",
        "onedrive" or "dropbox" or "googledrivefs" or "icloudservices" => "cloud sync",
        "searchindexer" or "searchprotocolhost" or "searchfilterhost" => "search indexing",
        "steam" or "steamwebhelper" or "epicgameslauncher" or "battle.net" or "agent" or "eadesktop" or "upc" => "game launcher (downloads or updates)",
        "compattelrunner" or "devicecensus" => "Windows telemetry",
        "chrome" or "msedge" or "firefox" or "opera" or "brave" => "web browser",
        _ => null
    };
    /// <summary>Seconds where another process was busy while the machine was under pressure, as correlation only.</summary>
    public static IReadOnlyList<string> Interference(MonitorRun run)
    {
        var busy = run.Samples.Where(x => x.DiskActivePercent >= 90 || x.BusiestCore >= 95 || x.CpuTotal >= 85).ToArray();
        var lines = new List<string>();
        foreach (var sample in busy) {
            var other = sample.TopProcesses.Where(t => t.Pid != run.TargetPid && (t.CpuPercent >= 10 || t.IoMBps >= 20)).OrderByDescending(t => t.CpuPercent + t.IoMBps / 5).FirstOrDefault();
            if (other is null) continue;
            lines.Add($"{sample.At.ToLocalTime():T}: {other.Name}{(Category(other.Name) is { } c ? $" ({c})" : "")} used {other.CpuPercent:0}% CPU and {other.IoMBps:0} MB/s of disk and network data while the {(sample.DiskActivePercent >= 90 ? $"disk was {sample.DiskActivePercent:0}% busy" : $"CPU was {sample.CpuTotal:0}% busy")}.");
        }
        var result = lines.Count == 0 ? new List<string> { "No background program stood out while the PC was busy." }
            : lines.Take(12).Prepend("These happened at the same time, which doesn't prove they caused slowdowns. Hanki never closes programs; you can pause them yourself.").ToList();
        if (Downloads(run) is { } download) result.Insert(0, download);
        return result;
    }

    /// <summary>
    /// 2 MB/s (16 Mbit/s) arriving over the network: far more than online games use, so it means a download or stream.
    /// </summary>
    internal const double DownloadMBps = 2;
    /// <summary>Downloads during the run (HANKI-PERF-315), with the program most likely receiving them. Null when nothing downloaded.</summary>
    public static string? Downloads(MonitorRun run)
    {
        var measured = run.Samples.Where(x => x.NetworkReceiveMBps is not null).ToArray();
        var busy = measured.Where(x => x.NetworkReceiveMBps >= DownloadMBps).ToArray();
        if (measured.Length < 5 || busy.Length < Math.Max(3, measured.Length / 10)) return null;
        double average = busy.Average(x => x.NetworkReceiveMBps!.Value), peak = busy.Max(x => x.NetworkReceiveMBps!.Value);
        string who = Downloader(run) is { } name ? $" The program moving the most data then was {name}." : "";
        return $"Something was downloading for {busy.Length} of {measured.Length} seconds, at {average:0.#} MB/s on average (peak {peak:0.#} MB/s).{who} " +
            "Downloads compete with online games for your connection and can keep the disk busy; pause them while you play. Hanki doesn't pause anything.";
    }
    /// <summary>The background program with the most disk and network data while downloads were arriving, named for a person.</summary>
    internal static string? Downloader(MonitorRun run)
    {
        var busy = run.Samples.Where(x => x.NetworkReceiveMBps >= DownloadMBps).ToArray();
        var top = busy.SelectMany(x => x.TopProcesses).Where(p => p.Pid != run.TargetPid && p.IoMBps >= 1)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Sum(p => p.IoMBps)).FirstOrDefault();
        if (top is null) return null;
        // svchost hosts many services; the ones that download are usually Windows Update and Delivery Optimization.
        return top.Key.Equals("svchost", StringComparison.OrdinalIgnoreCase) ? "a Windows service (usually Windows Update or Delivery Optimization)"
            : Category(top.Key) is { } c ? $"{top.Key} ({c})" : top.Key;
    }

    /// <summary>A run as a Performance session measurement (HANKI-PERF-312, HANKI-GAME-212, HANKI-GPU-109).</summary>
    public static PerformanceMeasurement Measurement(MonitorRun run)
    {
        var s = run.Samples; var m = new Dictionary<string, double>();
        if (s.Count > 0) {
            m[PerformanceMetrics.CpuAverage] = s.Average(x => x.CpuTotal); m[PerformanceMetrics.CpuPeak] = s.Max(x => x.CpuTotal);
            m[PerformanceMetrics.BusiestCore] = Percentile(s.Select(x => x.BusiestCore), 90);
            m[PerformanceMetrics.CommitPeak] = s.Max(x => x.CommitPercent); m[PerformanceMetrics.AvailableRamMinimum] = s.Min(x => x.AvailableMb) / 1024;
            m[PerformanceMetrics.DiskActive] = s.Average(x => x.DiskActivePercent); m[PerformanceMetrics.HardFaults] = s.Average(x => x.HardFaultsPerSecond);
            if (Average(s.Select(x => x.TargetGpuBusy ?? x.GpuBusy)) is { } g) m[PerformanceMetrics.GpuAverage] = g;
            if (Max(s.Select(x => x.GpuTemperature)) is { } t) m[PerformanceMetrics.GpuTemperature] = t;
            if (Max(s.Select(x => x.GpuVramUsedMb)) is { } v) m[PerformanceMetrics.Vram] = v / 1024;
        }
        if (run.Frames is { } f) { m[PerformanceMetrics.FpsAverage] = f.AverageFps; m[PerformanceMetrics.FpsLow1] = f.Low1Fps; m[PerformanceMetrics.FrameTime] = f.AverageFrameMs; }
        return new(run.Started, run.Ended, m, run.Target is null ? "Performance Lab monitor" : "Performance Lab monitor: " + run.Target);
    }

    /// <summary>Frame statistics from present timestamps in seconds: average FPS, 1% low (from the 99th-percentile frame time) and the frame times.</summary>
    public static FrameStats? Frames(IReadOnlyList<double> presentSeconds)
    {
        if (presentSeconds.Count < 30) return null;
        var times = new List<(double, double)>(presentSeconds.Count);
        for (int i = 1; i < presentSeconds.Count; i++) {
            double ms = (presentSeconds[i] - presentSeconds[i - 1]) * 1000;
            if (ms > 0 && ms < 2000) times.Add((presentSeconds[i] - presentSeconds[0], ms));
        }
        if (times.Count < 30) return null;
        double seconds = presentSeconds[^1] - presentSeconds[0], p99 = Percentile(times.Select(t => t.Item2), 99), average = times.Average(t => t.Item2);
        return new(times.Count, seconds, times.Count / seconds, 1000 / p99, average, p99, times);
    }
}
