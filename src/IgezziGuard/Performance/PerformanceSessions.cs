using System.Globalization;
namespace IgezziGuard;

/// <summary>One measured workload over a time window. Metric keys are the stable ids in <see cref="PerformanceMetrics"/>.</summary>
public sealed record PerformanceMeasurement(DateTimeOffset Started, DateTimeOffset Ended, IReadOnlyDictionary<string, double> Metrics, string Source, string Notes = "");

public enum SessionOutcome { Measured, Improved, NoClearChange, Worse, NotComparable }

/// <summary>
/// A Performance Session (HANKI-ARCH-200): a measured workload or an optimization test. It keeps the baseline, the
/// changes tested (ids of Recovery journal entries, so they can be kept or restored), the measurement afterwards and
/// the outcome. Sessions are Hanki Performance history; repairs and maintenance stay in System actions.
/// </summary>
public sealed record PerformanceSession(Guid Id, string Name, DateTimeOffset Created, PerformanceMeasurement Baseline,
    IReadOnlyList<Guid> ChangesTested, PerformanceMeasurement? After, SessionOutcome Outcome, string Summary);

public static class PerformanceMetrics
{
    public const string CpuAverage = "cpu.average", CpuPeak = "cpu.peak", CommitAverage = "memory.commit.average", CommitPeak = "memory.commit.peak",
        AvailableRamMinimum = "memory.available.minimum", DiskRead = "disk.read.average", DiskWrite = "disk.write.average",
        GpuAverage = "gpu.busiest.average", GpuPeak = "gpu.busiest.peak", FpsAverage = "fps.average", FpsLow1 = "fps.low1", FrameTime = "frametime.average",
        BusiestCore = "cpu.busiest.p90", DiskActive = "disk.active.average", HardFaults = "memory.hardfaults.average", GpuTemperature = "gpu.temperature.max", Vram = "gpu.vram.max";

    public static string Label(string key) => key switch {
        CpuAverage => "CPU, average", CpuPeak => "CPU, peak", CommitAverage => "Memory committed, average", CommitPeak => "Memory committed, peak",
        AvailableRamMinimum => "Lowest free RAM", DiskRead => "Disk reads, average", DiskWrite => "Disk writes, average",
        GpuAverage => "Busiest GPU engine, average", GpuPeak => "Busiest GPU engine, peak", FpsAverage => "Average FPS", FpsLow1 => "1% low FPS",
        FrameTime => "Average frame time", BusiestCore => "Busiest CPU thread (90th percentile)", DiskActive => "Disk busy, average",
        HardFaults => "Hard page faults per second", GpuTemperature => "GPU temperature, peak", Vram => "Video memory used, peak", _ => key
    };
    public static string Format(string key, double value) => key switch {
        AvailableRamMinimum => $"{value:0.00} GiB", DiskRead or DiskWrite => $"{value:0.0} MiB/s", FpsAverage or FpsLow1 => $"{value:0}",
        FrameTime => $"{value:0.0} ms", GpuTemperature => $"{value:0} °C", Vram => $"{value:0.0} GB", HardFaults => $"{value:0}/s", _ => $"{value:0.0}%"
    };

    /// <summary>A Performance Lab monitoring run as a measurement. Disk and GPU averages cover only the samples Windows reported.</summary>
    public static PerformanceMeasurement FromMonitoring(SavedSession run)
    {
        var metrics = new Dictionary<string, double> {
            [CpuAverage] = run.Cpu.AverageCpu, [CpuPeak] = run.Cpu.PeakCpu, [CommitAverage] = run.Cpu.AverageCommitPercent,
            [CommitPeak] = run.Cpu.PeakCommitPercent, [AvailableRamMinimum] = run.Cpu.MinimumAvailableRam / (1024d * 1024 * 1024)
        };
        void Average(string key, Func<DeviceSample, double?> pick, double scale = 1) {
            var values = run.Devices.Select(pick).Where(v => v is not null).Select(v => v!.Value / scale).ToArray();
            if (values.Length > 0) metrics[key] = values.Average();
        }
        Average(DiskRead, d => d.DiskRead, 1024 * 1024); Average(DiskWrite, d => d.DiskWrite, 1024 * 1024); Average(GpuAverage, d => d.GpuBusy);
        var gpu = run.Devices.Where(d => d.GpuBusy is not null).Select(d => d.GpuBusy!.Value).ToArray();
        if (gpu.Length > 0) metrics[GpuPeak] = gpu.Max();
        return new(run.Cpu.Started, run.Cpu.Ended, metrics, "Performance Lab monitor");
    }
}

/// <summary>
/// Conservative before/after reading. Utilization going up or down is not "better" by itself, so without frame-rate
/// data a session is only Measured. With frame rates, runs must be comparable (same source, similar length) and the
/// change must exceed normal run-to-run variation before Hanki calls it an improvement.
/// </summary>
public static class PerformanceComparison
{
    public const double MeaningfulChange = 0.03;
    public static bool Comparable(PerformanceMeasurement before, PerformanceMeasurement after)
    {
        double a = (before.Ended - before.Started).TotalSeconds, b = (after.Ended - after.Started).TotalSeconds;
        return before.Source == after.Source && a > 0 && b > 0 && Math.Min(a, b) / Math.Max(a, b) >= 0.75;
    }
    public static SessionOutcome Outcome(PerformanceMeasurement before, PerformanceMeasurement? after)
    {
        if (after is null) return SessionOutcome.Measured;
        if (!Comparable(before, after)) return SessionOutcome.NotComparable;
        if (!before.Metrics.TryGetValue(PerformanceMetrics.FpsAverage, out var fpsBefore) || !after.Metrics.TryGetValue(PerformanceMetrics.FpsAverage, out var fpsAfter) || fpsBefore <= 0)
            return SessionOutcome.Measured;
        double change = (fpsAfter - fpsBefore) / fpsBefore;
        double lowChange = before.Metrics.TryGetValue(PerformanceMetrics.FpsLow1, out var lowBefore) && after.Metrics.TryGetValue(PerformanceMetrics.FpsLow1, out var lowAfter) && lowBefore > 0
            ? (lowAfter - lowBefore) / lowBefore : change;
        if (change >= MeaningfulChange && lowChange > -MeaningfulChange) return SessionOutcome.Improved;
        if (change <= -MeaningfulChange || lowChange <= -2 * MeaningfulChange) return SessionOutcome.Worse;
        return SessionOutcome.NoClearChange;
    }
    public static string Describe(SessionOutcome outcome) => outcome switch {
        SessionOutcome.Improved => "Performance improved during this comparison.",
        SessionOutcome.Worse => "Performance was lower after the change. Consider restoring the previous settings.",
        SessionOutcome.NoClearChange => "No clear difference: the change was within normal run-to-run variation.",
        SessionOutcome.NotComparable => "The two runs aren't comparable (different length or source), so Hanki doesn't draw a conclusion.",
        _ => "Measured. Without frame-rate data, higher or lower utilization doesn't show whether performance got better."
    };
    /// <summary>A table of every metric in either measurement, with the difference where both have it.</summary>
    public static string Table(PerformanceMeasurement before, PerformanceMeasurement? after)
    {
        var keys = before.Metrics.Keys.Concat(after?.Metrics.Keys ?? []).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        var lines = new List<string> { after is null ? "Measurement" : $"{"",-30} {"Before",12} {"After",12}" };
        foreach (var key in keys) {
            string B(PerformanceMeasurement? m) => m is not null && m.Metrics.TryGetValue(key, out var v) ? PerformanceMetrics.Format(key, v) : "—";
            lines.Add(after is null ? $"{PerformanceMetrics.Label(key)}: {B(before)}" : $"{PerformanceMetrics.Label(key),-30} {B(before),12} {B(after),12}");
        }
        return string.Join("\r\n", lines);
    }
}

public sealed record PerformanceSessionDocument(int Version, IReadOnlyList<PerformanceSession> Sessions);

/// <summary>Local Performance history (newest 100 sessions), separate from system scan history.</summary>
public sealed class PerformanceSessionStore(string path)
{
    public const int Limit = 100;
    public IReadOnlyList<PerformanceSession> Read()
    {
        var document = LocalJson.Read<PerformanceSessionDocument>(path);
        if (document is null) return [];
        if (document.Version != 1 || document.Sessions is null || document.Sessions.Count > Limit || document.Sessions.Any(s => s?.Baseline?.Metrics is null || s.ChangesTested is null || s.Name is null))
            throw new IOException("Performance history is damaged or from a newer version. Original file preserved.");
        return document.Sessions;
    }
    public void Add(PerformanceSession session)
    {
        if (session.Name.Length is 0 or > 120 || session.Name.Any(char.IsControl)) throw new ArgumentException("Use a short single-line session name.");
        using var gate = LocalJson.Lock(path);
        var sessions = Read().Where(s => s.Id != session.Id).Prepend(session).OrderByDescending(s => s.Created).Take(Limit).ToArray();
        LocalJson.Write(path, new PerformanceSessionDocument(1, sessions));
    }
    public void Remove(Guid id)
    {
        using var gate = LocalJson.Lock(path);
        LocalJson.Write(path, new PerformanceSessionDocument(1, Read().Where(s => s.Id != id).ToArray()));
    }
    public static string DefaultName(string kind, DateTimeOffset at) => $"{kind}, {at.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture)}";
}
