namespace IgezziGuard;

/// <summary>Session-local back history. Restoring a location never creates another visit.</summary>
internal sealed class NavigationHistory
{
    private readonly List<string> previous = [];
    internal string Current { get; private set; } = "Home";
    internal string? BackTarget => previous.Count == 0 ? null : previous[^1];
    internal void Visit(string route)
    {
        if (route == Current) return;
        previous.Add(Current);
        if (previous.Count > 100) previous.RemoveAt(0);
        Current = route;
    }
    internal string? Back()
    {
        if (BackTarget is not { } route) return null;
        previous.RemoveAt(previous.Count - 1);
        return Current = route;
    }
}
