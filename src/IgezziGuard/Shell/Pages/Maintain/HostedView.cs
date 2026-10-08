using System.Windows;
using System.Windows.Forms.Integration;

namespace IgezziGuard.Shell;

/// <summary>A page that is still a WinForms panel, shown inside a native page through its own host until its native version exists.</summary>
internal sealed class HostedView : System.Windows.Controls.Grid
{
    internal HostedView(IShellServices shell, string key)
    {
        Margin = new Thickness(24, 0, 24, 12);
        var panel = shell.HostedPanel(key) ?? throw new InvalidOperationException("No hosted page named " + key);
        panel.Dock = System.Windows.Forms.DockStyle.Fill;
        Children.Add(new WindowsFormsHost { Child = panel, Background = UiKit.Res("Canvas") });
    }
}
