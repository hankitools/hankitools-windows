using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IgezziGuard.Shell;

/// <summary>Dark title bar for one WPF window, without switching the whole process to dark mode.</summary>
internal static class DarkTitleBar
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private const int UseImmersiveDarkMode = 20;

    internal static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) => {
            if (System.Windows.SystemParameters.HighContrast) return;
            int on = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, UseImmersiveDarkMode, ref on, sizeof(int));
        };
    }
}
