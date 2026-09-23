namespace IgezziGuard;

/// <summary>Plain-language reading of CPU, memory, disk and GPU samples. Short samples describe that moment only.</summary>
public static class PerformanceInsights
{
    internal static (CardStatus Status, string Text) Cpu(double average, double peak) => average switch {
        >= 85 => (CardStatus.Review, $"The processor was very busy: {average:0}% on average, peaking at {peak:0}%. It is likely what's slowing things down right now."),
        >= 50 => (CardStatus.Info, $"The processor was moderately busy: {average:0}% on average, peaking at {peak:0}%."),
        _ => (CardStatus.Good, $"The processor had plenty of headroom: {average:0}% on average, peaking at {peak:0}%.")
    };
    internal static (CardStatus Status, string Text) Memory(double peakCommitPercent, double minimumAvailableGib) => peakCommitPercent switch {
        >= 90 => (CardStatus.Problem, $"Memory was nearly full: Windows had promised {peakCommitPercent:0}% of what it can back, and as little as {minimumAvailableGib:0.0} GB of RAM was free. Close heavy apps or add RAM if this is typical."),
        >= 80 => (CardStatus.Review, $"Memory was getting tight: up to {peakCommitPercent:0}% of the commit limit in use, with at least {minimumAvailableGib:0.0} GB of RAM free."),
        _ => (CardStatus.Good, $"Memory was comfortable: up to {peakCommitPercent:0}% of the commit limit in use, with at least {minimumAvailableGib:0.0} GB of RAM free.")
    };

    public static Diagnosis Sample(PerformanceRun current, PerformanceRun? baseline, string report)
    {
        var cpu = Cpu(current.AverageCpu, current.PeakCpu);
        var memory = Memory(current.PeakCommitPercent, current.MinimumAvailableRam / (1024d * 1024 * 1024));
        var cards = new List<ResultCard> { new("Processor (CPU)", cpu.Text, cpu.Status), new("Memory", memory.Text, memory.Status) };
        cards.Add(baseline is null
            ? new("Compare later", "This run is now your baseline. Change one thing (close an app, switch power plan), repeat the same activity, and sample again to compare.")
            : new("Compared with your baseline", Change("Average CPU", current.AverageCpu - baseline.AverageCpu) + "\n" + Change("Average memory commit", current.AverageCommitPercent - baseline.AverageCommitPercent) +
                "\nOnly meaningful if you repeated the same activity; background tasks and heat also affect results."));
        cards.Add(new("What to do next", memory.Status >= CardStatus.Review && memory.Status != CardStatus.Unknown ? "Open the Overview tab to see which apps use the most memory, then close or update them."
            : cpu.Status == CardStatus.Review ? "Open Task Manager (Quick access) to see which app uses the processor, or monitor longer in the Monitoring tab."
            : "Nothing stands out in these 30 seconds. If the PC feels slow during a specific task, sample again while doing it."));
        string headline = memory.Status == CardStatus.Problem ? "Memory was nearly full during this sample"
            : cpu.Status == CardStatus.Review ? "The processor was very busy during this sample"
            : memory.Status == CardStatus.Review ? "Memory was getting tight during this sample"
            : "Your PC had headroom during this sample";
        return Diagnosis.From(report, cards, headline);
    }

    public static Diagnosis Monitoring(SavedSession session, SavedSession? baseline, string report)
    {
        var run = session.Cpu;
        var cpu = Cpu(run.AverageCpu, run.PeakCpu);
        var memory = Memory(run.PeakCommitPercent, run.MinimumAvailableRam / (1024d * 1024 * 1024));
        var cards = new List<ResultCard> {
            new("Session", $"{(run.Ended - run.Started).TotalMinutes:0.#} minutes, {run.Samples} samples."),
            new("Processor (CPU)", cpu.Text, cpu.Status), new("Memory", memory.Text, memory.Status)
        };
        double[] Values(Func<DeviceSample, double?> pick) => session.Devices.Select(pick).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        var reads = Values(d => d.DiskRead); var writes = Values(d => d.DiskWrite); var gpu = Values(d => d.GpuBusy);
        cards.Add(reads.Length == 0 && writes.Length == 0 ? new("Disk activity", "Disk counters were not available on this PC.", CardStatus.Unknown)
            : new("Disk activity", $"Reading up to {Max(reads) / 1048576:0.#} MB/s and writing up to {Max(writes) / 1048576:0.#} MB/s (averages {Avg(reads) / 1048576:0.#} and {Avg(writes) / 1048576:0.#} MB/s). High numbers are normal while installing, updating or copying files."));
        cards.Add(gpu.Length == 0 ? new("Graphics (GPU)", "GPU counters were not available on this PC.", CardStatus.Unknown)
            : new("Graphics (GPU)", $"The busiest GPU engine averaged {Avg(gpu):0}% and peaked at {Max(gpu):0}%." + (Max(gpu) >= 95 ? " Full use is normal in games and video work." : "")));
        if (baseline is not null)
            cards.Add(new("Compared with your baseline", Change("Average CPU", run.AverageCpu - baseline.Cpu.AverageCpu) + "\n" + Change("Average memory commit", run.AverageCommitPercent - baseline.Cpu.AverageCommitPercent) +
                (baseline.Machine != session.Machine ? "\nThe baseline came from a different PC, so this isn't a like-for-like comparison." : "\nOnly meaningful if the activity and duration were the same.")));
        string headline = memory.Status == CardStatus.Problem ? "Memory ran nearly full during monitoring"
            : cpu.Status == CardStatus.Review ? "The processor was the busiest part during monitoring"
            : memory.Status == CardStatus.Review ? "Memory got tight during monitoring"
            : "No resource was maxed out on average";
        cards.Add(new("What to do next", "Save this run, change one thing, and monitor the same activity again to compare. Short spikes are normal; look for values that stay high."));
        return Diagnosis.From(report, cards, headline);
    }
    private static double Max(double[] values) => values.Length == 0 ? 0 : values.Max();
    private static double Avg(double[] values) => values.Length == 0 ? 0 : values.Average();
    internal static string Change(string label, double points) => Math.Abs(points) < 2
        ? $"{label}: about the same ({points:+0.0;-0.0;0.0} points)."
        : $"{label}: {(points < 0 ? "lower" : "higher")} by {Math.Abs(points):0.0} percentage points.";
}
