using System.Diagnostics;
using Microsoft.Win32;
namespace IgezziGuard;

/// <summary>
/// Opens Windows' own Quick Assist for remote help. Hanki takes no part in the session: Microsoft's service
/// connects the two PCs, and the person being helped decides whether to share the screen or allow control.
/// </summary>
internal static class QuickAssist
{
    internal const string Protocol = "ms-quick-assist:";
    internal const string StorePage = "ms-windows-store://pdp/?ProductId=9P7BP5VNWKX5";
    internal const string HowItWorks = "Quick Assist is built into Windows. The person helping you chooses Help someone, signs in with a Microsoft account and gets a short security code. You choose Get help, type that code, and decide whether they only see your screen or may also control it. Either of you can end the session at any time.";
    internal const string ScamWarning = "Only share a Quick Assist code with someone you know and contacted yourself, such as a family member, a friend or your own IT support.\n\n" +
        "Microsoft, Hanki, your bank and real support staff never phone you, email you or show pop-ups asking for a code or remote access. If someone did, close Quick Assist.\n\n" +
        "Whoever has the code can see everything on your screen, and can control your PC if you allow it.\n\nOpen Quick Assist?";

    internal static bool Installed()
    {
        try { using var key = Registry.ClassesRoot.OpenSubKey("ms-quick-assist"); return key is not null; }
        catch { return false; }
    }

    internal static void Open(IWin32Window owner, bool gettingHelp)
    {
        if (gettingHelp && MessageBox.Show(owner, ScamWarning, "Before you let someone help", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        if (!Installed()) {
            if (MessageBox.Show(owner, "Quick Assist isn't installed on this PC. It's a free Microsoft app. Open its Microsoft Store page?", "Quick Assist", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Start(owner, StorePage, "Open the Microsoft Store and search for Quick Assist.");
            return;
        }
        Start(owner, Protocol, "Open Start, type Quick Assist and select it.");
    }

    private static void Start(IWin32Window owner, string target, string manual)
    {
        try { using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { MessageBox.Show(owner, "Could not open it from Hanki. " + manual, "Quick Assist"); }
    }
}
