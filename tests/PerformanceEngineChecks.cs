using IgezziGuard;

// HANKI-PERF-300 and the Lab: bottleneck engine, stutter and interference, frame statistics, CPU/memory/storage rules, Fix My PC gaming filter.
internal static class PerformanceEngineChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    internal static void Run() { Bottlenecks(); FramesAndStutter(); Analyzers(); FixMyPc(); Safety(); }

    private static void Safety()
    {
        Check(!Guardrails.Allowed(new ProposedChange("x", ChangeSource.Windows, "Timer", "HPET on", "HPET off", "internet tip", false, "BCDEdit", "useplatformclock", "false")) && Guardrails.Allowed(new ProposedChange("y", ChangeSource.Display, "Refresh", "60", "165", "why", false, "Display mode", @"\.DISPLAY1", "1920x1080@165")),
            "guardrails: only documented change kinds can be applied");
        Check(Guardrails.NotRecommended.Any(g => g.Tweak.Contains("HPET")) && Guardrails.NotRecommended.Any(g => g.Tweak.Contains("Defender")) && Guardrails.NotRecommended.Any(g => g.Tweak.Contains("Overclocking")) && Guardrails.NotRecommended.All(g => g.Why.Length > 20),
            "guardrails: common internet tweaks are listed with the reason Hanki won't make them");
    }

    private static MonitorSample Sample(int second, double gpu = 50, double busiest = 50, double total = 30, double available = 8000, double commit = 50, double faults = 0,
        double disk = 5, double? temp = 60, double? clock = 2800, double? vram = 4000, params ProcessActivity[] top) =>
        new(Now.AddSeconds(second), total, [busiest, total, total], 100, 4000, available, commit, faults, disk, 1, 10, 5, gpu, vram, temp, clock, gpu, top);
    private static MonitorRun Run(IEnumerable<MonitorSample> samples, FrameStats? frames = null, double? refresh = 165) =>
        new(Now, Now.AddSeconds(60), "game", 42, samples.ToArray(), frames, 12 * 1024, 3000, 16 * 1024, refresh, []);
    private static IEnumerable<MonitorSample> Seconds(int count, Func<int, MonitorSample> make) => Enumerable.Range(0, count).Select(make);

    private static void Bottlenecks()
    {
        Check(BottleneckEngine.Analyze(Run(Seconds(10, i => Sample(i)))).Limiter == Limiter.Insufficient, "bottleneck: too few seconds give 'not enough data', not a guess");
        var gpu = BottleneckEngine.Analyze(Run(Seconds(60, i => Sample(i, gpu: 99, busiest: 60))));
        Check(gpu.Limiter == Limiter.Gpu && gpu.Confidence == "High" && gpu.Recommendations.Any(r => r.Action.Contains("upscaling")) && gpu.Recommendations.Any(r => r.Action.Contains("Don't lower CPU-heavy")),
            "bottleneck: a GPU at 99% is GPU-limited, with GPU-side advice");
        var cpu = BottleneckEngine.Analyze(Run(Seconds(60, i => Sample(i, gpu: 67, busiest: 99, total: 43))));
        Check(cpu.Limiter == Limiter.Cpu && cpu.Confidence == "High" && cpu.Reasoning[0].Contains("One CPU thread is saturated") && cpu.Recommendations.Any(r => r.Action.Contains("view distance"))
            && cpu.Recommendations.Any(r => r.Action.Contains("Don't lower texture")), "bottleneck: one saturated thread with idle GPU is CPU-limited even at 43% total CPU");
        Check(BottleneckEngine.Analyze(Run(Seconds(60, i => Sample(i, gpu: 99, busiest: 60, total: 95)))).Limiter == Limiter.Gpu, "bottleneck: a busy CPU isn't called the limit while the GPU is the one at 99%");
        var none = BottleneckEngine.Analyze(Run(Seconds(60, i => Sample(i, gpu: 60, busiest: 60)), new FrameStats(9000, 60, 164, 150, 6.1, 6.7, [])));
        Check(none.Limiter == Limiter.None && none.Diagnosis.Contains("165 Hz") && none.Recommendations.Single().Action == "No change recommended", "bottleneck: a game held at the refresh rate has no hardware limit, and no change is recommended");
        Check(BottleneckEngine.Analyze(Run(Seconds(60, i => Sample(i, gpu: 80, busiest: 60, vram: 12 * 1024 * 0.98)))).Reasoning.Any(r => r.Contains("Video memory is nearly full")), "bottleneck: nearly full video memory");
        Check(BottleneckEngine.Analyze(Run(Seconds(60, i => Sample(i, gpu: 60, busiest: 60, available: 350, commit: 95, faults: 900)))).Limiter == Limiter.Memory, "bottleneck: low memory with heavy paging is a memory limit");
        Check(BottleneckEngine.Analyze(Run(Seconds(60, i => Sample(i, gpu: 50, busiest: 50, disk: 100)))).Limiter == Limiter.Storage, "bottleneck: a disk that's always busy");
        Check(BottleneckEngine.Analyze(Run(Seconds(60, i => Sample(i, gpu: 96, busiest: 50, temp: 87, clock: 2100)))).Reasoning.Any(r => r.Contains("87 °C") && r.Contains("2100")),
            "bottleneck: a hot GPU losing clock speed while busy is reported as possible throttling");
        var busy = BottleneckEngine.Analyze(Run(Seconds(60, i => Sample(i, gpu: 50, busiest: 50, top: new ProcessActivity(7, "OneDrive", 30, 80, 200)))));
        Check(busy.Reasoning.Any(r => r.Contains("OneDrive")) && busy.Confidence == "Low", "bottleneck: a busy background program is reported with low confidence");
        var mixed = BottleneckEngine.Analyze(Run(Seconds(60, i => Sample(i, gpu: 99, busiest: 50, disk: 100))));
        Check(mixed.Limiter == Limiter.Mixed && mixed.Confidence == "Low", "bottleneck: several limits give 'mixed' with low confidence");
        Check(gpu.Observed.Any(o => o.StartsWith("GPU load")) && gpu.Recommendations.All(r => r.Evidence.Length > 0), "bottleneck: observations are shown and every recommendation cites its evidence");
    }

    private static void FramesAndStutter()
    {
        var presents = Enumerable.Range(0, 1201).Select(i => i / 120.0).ToList(); // 120 FPS for 10 s
        var steady = BottleneckEngine.Frames(presents)!;
        Check(Math.Abs(steady.AverageFps - 120) < 0.5 && Math.Abs(steady.Low1Fps - 120) < 0.5 && Math.Abs(steady.AverageFrameMs - 8.33) < 0.05, "frames: steady 120 FPS gives 120 average and 1% low");
        var withSpikes = new List<double>(); double t = 0;
        for (int i = 0; i < 2000; i++) { t += i % 50 == 25 ? 0.050 : 1 / 120.0; withSpikes.Add(t); }
        var spiky = BottleneckEngine.Frames(withSpikes)!;
        Check(spiky.Low1Fps < 25 && spiky.AverageFps > 90 && spiky.P99FrameMs > 40, "frames: occasional 50 ms spikes pull the 1% low down while the average stays high");
        Check(BottleneckEngine.Frames([0, 0.01, 0.02]) is null, "frames: too few frames give no statistics");

        var spikeSeconds = spiky.FrameTimes.Where(f => f.FrameMs > 40).Select(f => (int)Math.Round(f.AtSeconds)).ToHashSet();
        var run = Run(Seconds((int)spiky.Seconds + 1, i => Sample(i, gpu: 90, busiest: spikeSeconds.Contains(i) ? 99 : 60)), spiky);
        var stutter = BottleneckEngine.Stutter(run);
        Check(stutter[0].Contains("frame-time spikes") && stutter.Any(l => l.Contains("Possible CPU spikes")), "stutter: spikes that line up with CPU spikes are reported as a possible cause");
        var quiet = BottleneckEngine.Stutter(Run(Seconds(12, i => Sample(i, gpu: 90, busiest: 60)), spiky));
        Check(quiet.Any(l => l.Contains("doesn't name a cause")), "stutter: without a matching measurement, no cause is named");
        Check(BottleneckEngine.Stutter(Run(Seconds(30, i => Sample(i))))[0].Contains("needs frame times"), "stutter: without frame times, Hanki says what's needed");
        Check(BottleneckEngine.Stutter(Run(Seconds(12, i => Sample(i)), steady))[0].Contains("No stutter"), "stutter: steady frames are reported as fine");

        var interference = BottleneckEngine.Interference(Run(Seconds(30, i => Sample(i, disk: i == 5 ? 100 : 10, top: new ProcessActivity(9, "TiWorker", 12, 184, 300)))));
        Check(interference[0].Contains("doesn't prove") && interference.Count == 2 && interference[1].Contains("TiWorker (Windows Update)") && interference[1].Contains("184 MB/s"),
            "interference: busy moments are matched to background programs by time, as correlation only");
        Check(BottleneckEngine.Category("MsMpEng") == "Microsoft Defender scan" && BottleneckEngine.Category("OneDrive") == "cloud sync" && BottleneckEngine.Category("game") is null, "interference: known background workloads are named");

        // HANKI-PERF-315: downloads during a run.
        MonitorSample Net(int i, double mbps, params ProcessActivity[] top) => Sample(i, top: top) with { NetworkReceiveMBps = mbps };
        var downloading = BottleneckEngine.Interference(Run(Seconds(30, i => Net(i, i < 20 ? 25 : 0.2, new ProcessActivity(7, "steam", 2, 26, 400)))));
        Check(downloading[0].Contains("downloading for 20 of 30 seconds") && downloading[0].Contains("25 MB/s on average") && downloading[0].Contains("steam (game launcher"),
            "downloads: a download during the run is reported with the program most likely receiving it");
        Check(BottleneckEngine.Downloads(Run(Seconds(30, i => Net(i, 0.3)))) is null && BottleneckEngine.Downloads(Run(Seconds(30, i => Sample(i)))) is null,
            "downloads: online-game traffic, and runs saved before Hanki measured the network, report no download");
        Check(BottleneckEngine.Downloads(Run(Seconds(30, i => Net(i, 30, new ProcessActivity(5, "svchost", 1, 30, 50))))) is { } service && service.Contains("Windows Update or Delivery Optimization"),
            "downloads: a download by a Windows service host is explained as Windows Update or Delivery Optimization");
        Check(BottleneckEngine.Analyze(Run(Seconds(30, i => Net(i, i < 10 ? 12 : 0.1)))).Observed.Any(o => o.StartsWith("Downloads: 10 of 30 seconds")),
            "downloads: the bottleneck analysis lists downloads among its observations");
        Check(BottleneckEngine.Stutter(Run(Seconds(12, i => Net(i, 20, new ProcessActivity(3, "OneDrive", 1, 20, 100))), spiky)).Any(l => l.StartsWith("Possible background download") && l.Contains("OneDrive (cloud sync)")),
            "stutter: spikes during a download point to the download and the program behind it");

        var measurement = BottleneckEngine.Measurement(Run(Seconds(60, i => Sample(i, gpu: 80, busiest: 90)), steady));
        Check(measurement.Metrics[PerformanceMetrics.FpsAverage] > 119 && measurement.Metrics[PerformanceMetrics.BusiestCore] == 90 && measurement.Metrics.ContainsKey(PerformanceMetrics.GpuTemperature),
            "comparison: a run becomes a session measurement with frame rate, busiest thread and GPU temperature");
    }

    private static void Analyzers()
    {
        var power = new WindowsGamingSettings(null, null, [], null, null, "Balanced", Guid.NewGuid(), "Balanced", 80, 50, 100);
        var cpu = SystemAnalyzers.Cpu(new CpuFacts("i7", 20, 28, 2100), power, true, true, Now);
        Check(cpu.Any(r => r.FindingId == "processor-maximum" && r.Severity == FindingSeverity.Warning && r.Metadata["remedy"] == GamingHealth.RemedyProcessor && r.Metadata["plan"].Length > 0)
            && cpu.Any(r => r.FindingId == "processor-battery" && r.Severity == FindingSeverity.Informational) && cpu.Any(r => r.FindingId == "processor-minimum"),
            "cpu: a capped processor on mains power is an opportunity; battery limits are normal; 100% minimum is a note");
        Check(cpu.Single(r => r.FindingId == "core-parking").Explanation.StartsWith("No change recommended"), "cpu: core parking tweaks get 'no change recommended'");

        var pagefile = new PagefileFacts(true, [], [(@"C:\pagefile.sys", 5706, 1150)], 7);
        MemoryFacts Memory(IReadOnlyList<MemoryModule> modules, ulong commit = 10, PagefileFacts? pf = null, ulong peak = 12) =>
            new(16UL << 30, 2, modules, commit << 30, 21UL << 30, peak << 30, 4UL << 30, pf ?? pagefile, [("chrome", 3UL << 30)]);
        var xmpOff = SystemAnalyzers.Memory(Memory([new("A1", 8UL << 30, 6000, 4800, "G.Skill", "F5-6000J3038F16G", 34), new("B1", 8UL << 30, 6000, 4800, "G.Skill", "F5-6000J3038F16G", 34)]), Now);
        Check(xmpOff.Any(r => r.FindingId == "memory-speed" && r.Severity == FindingSeverity.Warning && r.Recommendation!.Contains("XMP")), "memory: modules running below their reported rating point to XMP/EXPO in the BIOS");
        var hinted = SystemAnalyzers.Memory(Memory([new("A1", 16UL << 30, 4800, 4800, "Corsair", "CMK32GX5M2B6000C36", 34)]), Now);
        Check(hinted.Any(r => r.FindingId == "memory-speed" && r.Severity == FindingSeverity.Informational && r.Confidence == FindingConfidence.Possible) && hinted.Any(r => r.FindingId == "channels"),
            "memory: a part-number speed is only a labelled hint; one module suggests dual channel");
        var fine = SystemAnalyzers.Memory(Memory([new("A1", 8UL << 30, 3200, 3200, "X", "WPBH32D408UWA-8G", 26), new("B1", 8UL << 30, 3200, 3200, "X", "WPBH32D408UWA-8G", 26)]), Now);
        Check(!fine.Any(r => r.FindingId is "memory-speed" or "channels") && fine.Single(r => r.FindingId == "pagefile").Severity == FindingSeverity.Healthy && fine.First().Title == "16 GB DDR4",
            "memory: matched modules at their rated speed with a system-managed pagefile look fine");
        Check(SystemAnalyzers.Memory(Memory([], commit: 20), Now).Single(r => r.FindingId == "commit") is { Severity: FindingSeverity.Warning } high && high.Explanation.Contains("chrome"), "memory: nearly full commit is flagged with the biggest users");
        Check(SystemAnalyzers.Memory(Memory([], peak: 28), Now).Single(r => r.FindingId == "commit") is { Severity: FindingSeverity.Informational, Title: "Memory ran short earlier" } shortage && shortage.Explanation.Contains("28 GB")
            && SystemAnalyzers.Memory(Memory([]), Now).Single(r => r.FindingId == "commit") is { Severity: FindingSeverity.Healthy } fine2 && fine2.Explanation.Contains("12 GB") && !fine2.Explanation.Contains("%)"),
            "memory: a peak above today's limit is explained in GB instead of as a percentage over 100");
        Check(SystemAnalyzers.Memory(Memory([], pf: new PagefileFacts(false, [], [], 7)), Now).Single(r => r.FindingId == "pagefile") is { Severity: FindingSeverity.Warning } off && off.Explanation.Contains("crash dumps"),
            "memory: a disabled pagefile is flagged, including the effect on crash dumps");
        Check(SystemAnalyzers.PartNumberSpeed("F5-6000J3038F16G") == 6000 && SystemAnalyzers.PartNumberSpeed("KF432C16BB/8") is null, "memory: part-number speeds are read only when clear");

        var disks = new[] { new DiskFacts(0, "NVMe", "SSD", "NVMe", 1000UL << 30, "Healthy", null, null), new DiskFacts(1, "Barracuda", "HDD", "SATA", 2000UL << 30, "Healthy", null, null) };
        var volumes = new[] { new VolumeFacts('C', 0, "NTFS", 1000UL << 30, 400UL << 30), new VolumeFacts('D', 1, "NTFS", 2000UL << 30, 50UL << 30) };
        var games = new[] { new GameEntry(Guid.NewGuid(), "Cyberpunk 2077", @"D:\Games\Cyberpunk\bin\x64\Cyberpunk2077.exe", "Steam", GamingGoal.Balanced) };
        var storage = SystemAnalyzers.Storage(new StorageFacts(disks, volumes, true, "Disabled", null, 26200, true, []), games, 'C', Now);
        Check(storage.Any(r => r.FindingId == "game-hdd:cyberpunk 2077" && r.Explanation.Contains("C: is an SSD with room") && r.Explanation.Contains("how much depends on the game")),
            "storage: a game on a hard disk, with an SSD that has room, and no promise every game benefits");
        Check(storage.Any(r => r.FindingId == "trim" && r.Severity == FindingSeverity.Warning) && storage.Any(r => r.FindingId == "optimize" && r.Severity == FindingSeverity.Warning)
            && storage.Any(r => r.FindingId == "space-D") && storage.Single(r => r.FindingId == "defrag-ssd").Explanation.StartsWith("No change recommended"),
            "storage: TRIM off, optimization disabled and a nearly full drive are flagged; SSD defrag gets 'no change recommended'");
        var ready = SystemAnalyzers.DirectStorage(new StorageFacts(disks, volumes, false, "Ready", Now, 26200, true, []), games);
        Check(ready.Any(l => l.Contains("NVMe SSD: yes")) && ready.Any(l => l.StartsWith("Cyberpunk 2077: installed on hard disk")) && ready.Last().Contains("up to the game"),
            "directstorage: system readiness is separate from whether a game uses it");
    }

    private static void FixMyPc()
    {
        var display = new DisplayInfo("Monitor", @"\\.\DISPLAY1", 1, new(2560, 1440, 60), [new(2560, 1440, 165), new(2560, 1440, 60)], null, null, true, "DisplayPort");
        var graphics = new GraphicsInventory([new("RTX", GpuVendor.Nvidia, 0, 0, 1, 12UL << 30, 0, 0, null, null, false)], [display], false, true, true, false, []);
        var windows = new WindowsGamingSettings(false, null, [], null, null, "Balanced", null, null, 100, 100, 5);
        var significant = GamingDiagnostic.Significant(GamingHealth.Evaluate(graphics, windows, null, Now));
        Check(significant.Count == 1 && significant[0].FindingId == "refresh-1", "fix my pc: only high-impact gaming problems appear (the refresh rate, not Game Mode)");
        Check(FindingAnalysis.Recommend(significant[0]) is { RepairActionId: null } advice && advice.ManualAction.Contains("Tune my PC → Gaming"), "fix my pc: gaming findings point to Tune my PC → Gaming and are never repaired automatically");
        var healthy = GamingDiagnostic.Significant(GamingHealth.Evaluate(graphics with { Displays = [display with { Current = new(2560, 1440, 165) }] }, windows with { GameMode = true }, null, Now));
        Check(healthy.Count == 1 && healthy[0].Severity == FindingSeverity.Healthy && healthy[0].FindingId == "summary", "fix my pc: a PC without gaming problems gets one quiet 'no significant problems' result");
    }
}
