namespace IgezziGuard.Shell;

/// <summary>Maintain: files and storage, installed apps, usage review, startup, duplicates and startup folders, as one page with tabs.</summary>
internal sealed class MaintainPage : NativePage
{
    private readonly SubTabs tabs = new();
    internal FilesView? Files => tabs.ContentOf("Files & storage") as FilesView;
    internal AppsView? Apps => tabs.ContentOf("Apps & storage") as AppsView;
    internal StartupView? Startup => tabs.ContentOf("Startup / undo") as StartupView;
    internal DuplicatesView? Duplicates => tabs.ContentOf("Duplicates") as DuplicatesView;
    internal string? CurrentTab => tabs.Current;
    internal IEnumerable<string> TabKeys => tabs.Keys;

    internal MaintainPage(IShellServices shell)
    {
        tabs.Add("Files & storage", "Files", () => new FilesView(shell));
        tabs.Add("Apps & storage", "Apps", () => new AppsView(shell));
        // Usage review and Startup folders are still the existing pages, shown through a host until their native versions exist.
        tabs.Add("Usage review", "Usage review", () => new HostedView(shell, "Usage review"));
        tabs.Add("Startup / undo", "Startup", () => new StartupView(shell));
        tabs.Add("Duplicates", "Duplicates", () => new DuplicatesView(shell));
        tabs.Add("Startup folders", "Startup folders", () => new HostedView(shell, "Startup folders"));
        Content = tabs;
    }

    internal override void OnShown() { if (tabs.Current is null) tabs.Select("Files & storage"); }
    internal override void OnRoute(IReadOnlyList<string> path)
    {
        if (path.Count > 1 && tabs.Keys.Contains(path[1])) tabs.Select(path[1]);
        if (path.Count > 2 && path[1] == "Startup / undo") Startup?.ShowSection(path[2]);
    }
    internal void Select(string key) => tabs.Select(key);
}
