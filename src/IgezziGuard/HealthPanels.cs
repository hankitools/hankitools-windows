using System.Diagnostics;
namespace IgezziGuard;

/// <summary>Diagnose → Windows Update. Read-only: Windows Update history, restart and pause state.</summary>
public sealed class UpdateHealthPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    public UpdateHealthPanel() : base("See whether Windows is installing its updates: when the last Windows update installed, which updates failed and why, and whether a restart is waiting. Hanki only reads Windows Update history; nothing is changed or uploaded.")
    {
        Button("Check Windows Update", async () => await Run(async token => {
            var facts = await new WindowsUpdateDiagnostic().ReadAsync(token);
            var items = UpdateHealth.Evaluate(facts, DateTimeOffset.UtcNow);
            return HealthItems.Diagnose(items, UpdateHealth.Headline(items), HealthItems.Report("Windows Update", items, UpdateHealth.HistoryLines(facts), facts.Notes));
        }));
        Button("Open Windows Update settings", () => HealthSettings.Open(this, "ms-settings:windowsupdate", "Settings → Windows Update"));
        Button("Update history in Settings", () => HealthSettings.Open(this, "ms-settings:windowsupdate-history", "Settings → Windows Update → Update history"));
    }
}

/// <summary>Diagnose → Battery & startup. Read-only: battery wear, restarts and startup records.</summary>
public sealed class BatteryStartupPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    public BatteryStartupPanel() : base("Check how much of its original capacity a laptop battery still holds, when Windows last fully restarted and how the last startup went. Hanki reads Windows' own battery report and startup records; nothing is changed or uploaded.")
    {
        Button("Check battery and startup", async () => await Run(async token => {
            var facts = await new BatteryStartupDiagnostic().ReadAsync(token);
            var items = BatteryStartup.Evaluate(facts, DateTimeOffset.UtcNow);
            return HealthItems.Diagnose(items, BatteryStartup.Headline(items), HealthItems.Report("Battery and startup", items, BatteryStartup.BootLines(facts), facts.Notes));
        }));
        Button("Open Power & battery settings", () => HealthSettings.Open(this, "ms-settings:powersleep", "Settings → System → Power & battery"));
    }
}

internal static class HealthSettings
{
    internal static void Open(IWin32Window owner, string uri, string manual)
    {
        try { using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { MessageBox.Show(owner, "Could not open Settings. Open " + manual + " manually.", "Hanki Tools"); }
    }
}
