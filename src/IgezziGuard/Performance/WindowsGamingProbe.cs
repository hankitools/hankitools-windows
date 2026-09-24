using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace IgezziGuard;

/// <summary>
/// Windows gaming settings as the Settings app stores them, and the active power configuration through the
/// documented power APIs. Read-only.
/// </summary>
internal static class WindowsGamingProbe
{
    internal const string GpuPreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private static readonly Guid ProcessorGroup = new("54533251-82be-4824-96c1-47b60b740d00"), ProcessorMaximum = new("bc5038f7-23e0-4960-96da-33abaf5935ec"),
        ProcessorMinimum = new("893dee8e-2bef-41e0-89c6-b55d0929964c");

    internal static WindowsGamingSettings Collect()
    {
        bool? gameMode = null, scheduling = null;
        var preferences = new List<AppGpuPreference>();
        string? global = null;
        using (var gameBar = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar"))
            if (gameBar?.GetValue("AutoGameModeEnabled") is int mode) gameMode = mode != 0;
        try {
            using var drivers = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            if (drivers?.GetValue("HwSchMode") is int hags) scheduling = hags == 2 ? true : hags == 1 ? false : null;
        } catch (System.Security.SecurityException) { }
        using (var apps = Registry.CurrentUser.OpenSubKey(GpuPreferencesKey)) {
            foreach (var name in apps?.GetValueNames() ?? []) {
                var data = apps!.GetValue(name) as string;
                if (name == "DirectXUserGlobalSettings") { global = data; continue; }
                if (WindowsGamingParsing.Preference(data) is { } preference) preferences.Add(new(name, preference));
            }
        }
        var plan = ActivePlan();
        return new WindowsGamingSettings(gameMode, scheduling, preferences,
            WindowsGamingParsing.Flag(global, "SwapEffectUpgradeEnable"), WindowsGamingParsing.Flag(global, "AutoHDREnable"),
            PowerMode(), plan, plan is { } p ? PlanName(p) : null,
            plan is { } a ? Read(a, ProcessorMaximum, ac: true) : null, plan is { } b ? Read(b, ProcessorMaximum, ac: false) : null, plan is { } c ? Read(c, ProcessorMinimum, ac: true) : null);
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr group, IntPtr setting, IntPtr buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    private static string? PowerMode()
    {
        try { return PowerGetEffectiveOverlayScheme(out var overlay) == 0 ? WindowsGamingParsing.PowerModeName(overlay) : null; }
        catch (EntryPointNotFoundException) { return null; }
    }
    private static Guid? ActivePlan()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0 || pointer == IntPtr.Zero) return null;
        try { return Marshal.PtrToStructure<Guid>(pointer); } finally { LocalFree(pointer); }
    }
    private static string? PlanName(Guid plan)
    {
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, ref plan, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size) != 0 || size == 0 || size > 4096) return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try { return PowerReadFriendlyName(IntPtr.Zero, ref plan, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0 ? Marshal.PtrToStringUni(buffer) : null; }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static int? Read(Guid plan, Guid setting, bool ac)
    {
        var group = ProcessorGroup;
        uint status = ac ? PowerReadACValueIndex(IntPtr.Zero, ref plan, ref group, ref setting, out var value) : PowerReadDCValueIndex(IntPtr.Zero, ref plan, ref group, ref setting, out value);
        return status == 0 && value <= 100 ? (int)value : null;
    }
}
