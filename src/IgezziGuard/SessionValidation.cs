namespace IgezziGuard;

public sealed record PerformanceRun(DateTimeOffset Started, DateTimeOffset Ended, int Samples, double AverageCpu,
    double PeakCpu, double AverageCommitPercent, double PeakCommitPercent, ulong MinimumAvailableRam);

public sealed record DeviceSample(DateTimeOffset At, double? DiskRead, double? DiskWrite, double? GpuBusy, string? Notes);
public sealed record SavedSession(int Version, string Machine, PerformanceRun Cpu, List<DeviceSample> Devices);
internal static class SessionValidation
{
    public static void Validate(SavedSession saved)
    {
        if (saved.Version != 1 || string.IsNullOrWhiteSpace(saved.Machine) || saved.Machine.Length > 256 || saved.Cpu is null || saved.Devices is null || saved.Cpu.Samples is < 1 or > 900 || saved.Devices.Count > 1000)
            throw new IOException("Unsupported or incomplete session format.");
        var c = saved.Cpu;
        static bool Percent(double d) => double.IsFinite(d) && d >= 0 && d <= 100;
        if (c.Started == default || c.Ended <= c.Started || !Percent(c.AverageCpu) || !Percent(c.PeakCpu) || c.PeakCpu < c.AverageCpu || !Percent(c.AverageCommitPercent) || !Percent(c.PeakCommitPercent) || c.PeakCommitPercent < c.AverageCommitPercent)
            throw new IOException("Invalid session times or CPU/memory percentages.");
        foreach (var d in saved.Devices) {
            if (d is null || d.At == default || (d.DiskRead is double read && (!double.IsFinite(read) || read < 0)) || (d.DiskWrite is double write && (!double.IsFinite(write) || write < 0)) || (d.GpuBusy is double gpu && !Percent(gpu)) || d.Notes?.Length > 10000)
                throw new IOException("Invalid device sample.");
        }
    }
}
