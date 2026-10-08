using System.Windows;
using System.Windows.Threading;

namespace IgezziGuard.Shell;

/// <summary>Creates the WPF application (Hanki dark palette from Theme.xaml) and runs the shell window.</summary>
internal static class ShellApp
{
    internal static System.Windows.Application Create()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        // Not ThemeMode.Dark: Fluent dark mode is process-wide and tints the hosted WinForms pages (native tab panes, transparent controls).
        // The shell and palette are fully styled by Theme.xaml; only the title bar follows dark via DarkTitleBar.
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HankiTools;component/Shell/Theme.xaml") });
        app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Program.ShowFatal(e.Exception); };
        return app;
    }

    internal static int Run()
    {
        var app = Create();
        return app.Run(new ShellWindow());
    }
}
