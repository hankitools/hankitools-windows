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
}

/// <summary>A page built natively in WPF. It replaces a hosted WinForms page of the same name.</summary>
internal abstract class NativePage : UserControl
{
    /// <summary>Called each time the page becomes the current destination: refresh anything that may have changed.</summary>
    internal virtual void OnShown() { }
}
