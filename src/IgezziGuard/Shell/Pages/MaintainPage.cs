namespace IgezziGuard.Shell;

/// <summary>Maintain: files and storage, installed apps, usage review, startup, duplicates and startup folders, as one page with tabs.</summary>
internal sealed class MaintainPage : NativePage
{
    private readonly SubTabs tabs = new();
    internal FilesView? Files => tabs.ContentOf("Files & storage") as FilesView;
    internal string? CurrentTab => tabs.Current;
    internal IEnumerable<string> TabKeys => tabs.Keys;

    internal MaintainPage(IShellServices shell)
    {
        tabs.Add("Files & storage", "Files", () => new FilesView(shell));
        tabs.Add("Apps & storage", "Apps", () => new HostedView(shell, "Apps & storage"));
        tabs.Add("Usage review", "Usage review", () => new HostedView(shell, "Usage review"));
        tabs.Add("Startup / undo", "Startup", () => new HostedView(shell, "Startup / undo"));
        tabs.Add("Duplicates", "Duplicates", () => new HostedView(shell, "Duplicates"));
        tabs.Add("Startup folders", "Startup folders", () => new HostedView(shell, "Startup folders"));
        Content = tabs;
    }

    internal override void OnShown() { if (tabs.Current is null) tabs.Select("Files & storage"); }
    internal override void OnRoute(IReadOnlyList<string> path) { if (path.Count > 1 && tabs.Keys.Contains(path[1])) tabs.Select(path[1]); }
    internal void Select(string key) => tabs.Select(key);
}
