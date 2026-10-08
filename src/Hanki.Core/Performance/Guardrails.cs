namespace IgezziGuard;

/// <summary>
/// Performance safety guardrails (HANKI-GAME-211, HANKI-PERF-314). Hanki only applies the change kinds in
/// Navigation.PerformanceChangeKinds, each with a documented reason and a Recovery entry. The tweaks below are often
/// suggested online; Hanki explains them and recommends no change.
/// </summary>
public static class Guardrails
{
    public sealed record Refused(string Tweak, string Why);
    public static readonly IReadOnlyList<Refused> NotRecommended = [
        new("Disabling Microsoft Defender or security mitigations for FPS", "Protection matters more than a few frames, and modern mitigations cost little in games. Hanki never weakens security."),
        new("Disabling HPET or changing timer settings (bcdedit useplatformclock, disabledynamictick)", "Timer changes can cause stutter, drift or instability, and there's no evidence they help on current Windows."),
        new("Random BCDEdit “performance” commands", "Boot configuration changes can stop Windows from starting and rarely help."),
        new("Turning off services in bulk", "Services often back features you rely on (updates, audio, networking). Hanki checks the ones that matter instead."),
        new("Scheduler, priority or CPU affinity registry tweaks", "Windows already schedules games well; fixed affinities can make things worse when a game changes threads."),
        new("Network “gaming” tweaks (Nagle, TCPAckFrequency, throttling index)", "They don't lower in-game ping, which depends on your connection and the game server."),
        new("Undocumented graphics driver flags", "Hanki changes only documented driver settings, checked against the driver's own names."),
        new("Forcing MSI mode on devices", "The driver chooses its interrupt mode; forcing it can break the device."),
        new("Memory cleaners and standby-list purging", "Windows frees cached memory instantly when a game needs it; purging it makes Windows reload files from disk."),
        new("Disabling the pagefile", "Without it, programs crash when memory runs out and Windows can't save crash dumps."),
        new("Clearing shader caches at every launch", "Games then rebuild shaders while you play, which causes stutter. Hanki only suggests a reset when evidence points to a damaged cache."),
        new("Defragmenting SSDs", "SSDs don't benefit; Windows sends them ReTrim instead."),
        new("Overclocking from a gaming profile", "Profiles and Fix My PC never overclock. Vendor auto-tuning, when it arrives, stays separate in Performance Lab → Advanced Tuning."),
    ];

    /// <summary>Whether Hanki may apply a proposed change: a known kind, never anything else.</summary>
    public static bool Allowed(ProposedChange change) => change.Kind is null || Navigation.IsPerformanceChange(change.Kind);
}
