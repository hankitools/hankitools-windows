using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace IgezziGuard;

/// <summary>
/// Reads Home's "Your PC at a glance" facts: all read-only and quick (registry, drive, memory, graphics adapters and
/// Windows Security Center's health, which covers whichever antivirus and firewall are in use). A fact Windows doesn't
/// report stays null.
/// </summary>
internal static class PcGlanceProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus { public uint Length, MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    // wscapi.h: WscGetSecurityProviderHealth(WSC_SECURITY_PROVIDER, PWSC_SECURITY_PROVIDER_HEALTH).
    [DllImport("wscapi.dll")] private static extern int WscGetSecurityProviderHealth(int providers, out int health);
    private const int FirewallProvider = 0x1, AntivirusProvider = 0x4;

    internal static GlanceFacts Collect()
    {
        string? windows = null, build = null;
        try {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            int.TryParse(key?.GetValue("CurrentBuild") as string, out var number);
            var display = key?.GetValue("DisplayVersion") as string;
            windows = PcGlance.WindowsName(key?.GetValue("ProductName") as string, number) + (display is { Length: > 0 } ? " " + display : "");
            if (number > 0) build = number + (key?.GetValue("UBR") is int ubr ? "." + ubr : "");
        } catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }

        string drive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
        long? free = null, total = null;
        try { var d = new DriveInfo(drive); if (d.IsReady) { free = d.AvailableFreeSpace; total = d.TotalSize; } }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }

        long? used = null, memory = null;
        try {
            var m = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref m)) { memory = (long)m.TotalPhys; used = (long)(m.TotalPhys - m.AvailPhys); }
        } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }

        string? gpu = null, driver = null;
        try {
            var adapters = GraphicsProbe.Collect().Adapters.OrderBy(a => a.PreferenceRank).ToArray();
            if ((adapters.FirstOrDefault(a => !a.LikelyIntegrated) ?? adapters.FirstOrDefault()) is { } a) { gpu = a.Name; driver = GraphicsFacts.DriverVersion(a.Vendor, a.DriverVersion); }
        } catch (Exception ex) when (ex is IOException or InvalidOperationException or COMException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException) { }

        static SecurityHealth? Health(int provider)
        {
            try { return WscGetSecurityProviderHealth(provider, out var h) == 0 && h is >= 0 and <= 3 ? (SecurityHealth)h : null; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
        }
        return new(windows, build, TimeSpan.FromMilliseconds(Environment.TickCount64), drive, free, total, used, memory, gpu, driver, Health(AntivirusProvider), Health(FirewallProvider));
    }
}
