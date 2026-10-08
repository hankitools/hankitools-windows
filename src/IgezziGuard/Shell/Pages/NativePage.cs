using UserControl = System.Windows.Controls.UserControl;

namespace IgezziGuard.Shell;

/// <summary>What a native page may ask of the shell. The shell implements it; pages never reference the window.</summary>
internal interface IShellServices
{
    /// <summary>Opens a destination by its page name (a workspace tab, for example "Maintain").</summary>
    void Navigate(string page);
    /// <summary>Every navigable tool, as listed in Find a tool.</summary>
    IReadOnlyList<ToolLauncher.Route> Routes { get; }
    /// <summary>Opens the guided checks with a symptom chosen.</summary>
    void OpenGuide(int index);
    /// <summary>Opens Fix my PC and starts its scan.</summary>
    void StartFixMyPc();
    /// <summary>The window as an owner for WinForms dialogs.</summary>
    IWin32Window DialogOwner { get; }
    /// <summary>Hands report text to the Assistant to redact and review.</summary>
    void PrepareForAssistant(string text);
    /// <summary>The one full-scan engine; the footer and Cancel tasks use it too.</summary>
    FullScanController Scan { get; }
    /// <summary>Work started by native pages: listed in the footer, cancelled by Cancel tasks, waited for on close.</summary>
    TaskTracker Tasks { get; }
    /// <summary>A message for the footer (for example a Defender protection alert).</summary>
    void Say(string text);
    /// <summary>A page the native page still shows through a host (for example "Usage review"), or null.</summary>
    System.Windows.Forms.Control? HostedPanel(string key);
    /// <summary>Maps an installed app for usage review.</summary>
    void MapUsage(InstalledApp app);
}

/// <summary>A page built natively in WPF. It replaces a hosted WinForms page of the same name.</summary>
internal abstract class NativePage : UserControl
{
    /// <summary>Called each time the page becomes the current destination: refresh anything that may have changed.</summary>
    internal virtual void OnShown() { }
    /// <summary>Called when a route or navigation selected a nested tab: the path is the page and its tabs, outermost first.</summary>
    internal virtual void OnRoute(IReadOnlyList<string> path) { }
}
