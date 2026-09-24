using System.Diagnostics;
namespace IgezziGuard;

/// <summary>
/// Launch & measure (HANKI-GAME-219): start a game from the list, wait for its window, let it load, then measure a
/// fixed stretch of play so runs of the same game stay comparable. Each run is compared with the previous run of that
/// game, together with the changes recorded in Recovery in between.
/// </summary>
internal static class LaunchMeasure
{
    internal const int WarmupSeconds = 20, MeasureSeconds = 120;
    internal static readonly TimeSpan WindowTimeout = TimeSpan.FromMinutes(2);

    internal static string SessionName(string game) => "Launch & measure: " + game;

    /// <summary>The newest running process of the game's executable that has a window; launchers often restart the game.</summary>
    internal static int? PickProcess(IEnumerable<(int Pid, string Name, DateTime? Started, bool Window)> running, string executableName) =>
        running.Where(p => p.Window && p.Name.Equals(executableName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Started ?? DateTime.MinValue).Select(p => (int?)p.Pid).FirstOrDefault();

    /// <summary>This game's most recent run, to compare against.</summary>
    internal static PerformanceSession? Previous(IEnumerable<PerformanceSession> sessions, string game) =>
        sessions.Where(s => s.Name == SessionName(game)).OrderByDescending(s => s.Created).FirstOrDefault();

    /// <summary>A session for this run: compared with the previous run when there is one, with the changes made in between.</summary>
    internal static PerformanceSession Session(string game, PerformanceMeasurement current, PerformanceSession? previous, IReadOnlyList<SettingChange> changes, string summary)
    {
        var last = previous?.After ?? previous?.Baseline;
        var between = last is null ? Array.Empty<Guid>() : changes.Where(c => Navigation.IsPerformanceChange(c.Kind) && c.Status == "Applied" && c.At >= last.Ended && c.At <= current.Started).Select(c => c.Id).ToArray();
        return last is null
            ? new(Guid.NewGuid(), SessionName(game), DateTimeOffset.UtcNow, current, [], null, SessionOutcome.Measured, summary)
            : new(Guid.NewGuid(), SessionName(game), DateTimeOffset.UtcNow, last, between, current, PerformanceComparison.Outcome(last, current), summary);
    }

    /// <summary>Waits for the game's window. Returns null when it doesn't appear in time.</summary>
    internal static async Task<Process?> WaitForGame(string executableName, CancellationToken token)
    {
        var until = DateTime.UtcNow + WindowTimeout;
        while (DateTime.UtcNow < until) {
            var running = new List<(int, string, DateTime?, bool)>();
            foreach (var process in Process.GetProcessesByName(executableName)) {
                try { DateTime? started = null; try { started = process.StartTime; } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                    running.Add((process.Id, process.ProcessName, started, process.MainWindowHandle != IntPtr.Zero)); }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }
            if (PickProcess(running, executableName) is { } pid) {
                try { return Process.GetProcessById(pid); } catch (ArgumentException) { }
            }
            await Task.Delay(1000, token);
        }
        return null;
    }
}
