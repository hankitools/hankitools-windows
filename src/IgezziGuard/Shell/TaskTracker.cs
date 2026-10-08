namespace IgezziGuard.Shell;

/// <summary>
/// Work started by native pages. The footer lists it under "Running", Cancel tasks cancels it, and closing the window waits for it,
/// the same as for the hosted pages.
/// </summary>
internal sealed class TaskTracker
{
    private readonly List<(string Name, Action Cancel)> active = [];
    internal event Action? Changed;
    internal bool Any => active.Count > 0;
    internal IReadOnlyList<string> Names => active.Select(a => a.Name).Distinct().ToList();

    /// <summary>Registers a running task; dispose the ticket when it ends.</summary>
    internal IDisposable Begin(string name, Action cancel)
    {
        var entry = (name, cancel); active.Add(entry); Changed?.Invoke();
        return new Ticket(() => { active.Remove(entry); Changed?.Invoke(); });
    }
    internal void CancelAll() { foreach (var (_, cancel) in active.ToArray()) { try { cancel(); } catch (ObjectDisposedException) { } } }

    private sealed class Ticket(Action end) : IDisposable { private Action? end = end; public void Dispose() { end?.Invoke(); end = null; } }
}
