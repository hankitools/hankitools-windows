using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace IgezziGuard;

/// <summary>
/// Windows gaming settings as the Settings app stores them, and the active power configuration through the
/// documented power APIs. Read-only.
/// </summary>
internal static class WindowsGamingProbe
{
    internal const string GpuPreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences", DirectXGlobalValue = "DirectXUserGlobalSettings",
        GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR", GameBarKey = @"Software\Microsoft\GameBar";
    private static readonly Guid ProcessorGroup = new("54533251-82be-4824-96c1-47b60b740d00"), ProcessorMaximum = new("bc5038f7-23e0-4960-96da-33abaf5935ec"),
        ProcessorMinimum = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    // SUB_ENERGYSAVER / ESBATTTHRESHOLD: the battery level at which Energy saver (battery saver) turns on.
    internal static readonly Guid EnergySaverGroup = new("de830923-a562-41af-a086-e3a2c6bad2da"), EnergySaverThreshold = new("e69653ca-cf7f-4f05-aa73-cb833fa90ad4");

    internal static WindowsGamingSettings Collect()
    {
        bool? gameMode = null, scheduling = null;
        var preferences = new List<AppGpuPreference>();
        string? global = null;
        using (var gameBar = Registry.CurrentUser.OpenSubKey(GameBarKey))
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
        bool? history = null, capture = null;
        using (var dvr = Registry.CurrentUser.OpenSubKey(GameDvrKey)) {
            if (dvr?.GetValue("HistoricalCaptureEnabled") is int h) history = h != 0;
            if (dvr?.GetValue("AppCaptureEnabled") is int a) capture = a != 0;
        }
        var plan = ActivePlan();
        return new WindowsGamingSettings(gameMode, scheduling, preferences,
            WindowsGamingParsing.Flag(global, "SwapEffectUpgradeEnable"), WindowsGamingParsing.Flag(global, "AutoHDREnable"),
            PowerMode(), plan, plan is { } p ? PlanName(p) : null,
            plan is { } a1 ? Read(a1, ProcessorMaximum, ac: true) : null, plan is { } b ? Read(b, ProcessorMaximum, ac: false) : null, plan is { } c ? Read(c, ProcessorMinimum, ac: true) : null,
            WindowsGamingParsing.Flag(global, "VRROptimizeEnable"), history, capture, Mouse(), plan is { } d ? Read(d, EnergySaverThreshold, ac: false, EnergySaverGroup) : null);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfoW(uint action, uint parameter, int[] values, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfoW(uint action, uint parameter, IntPtr values, uint flags);
    private const uint GetMouseAction = 0x0003, SetMouseAction = 0x0004, UpdateIniFile = 0x01, SendChange = 0x02;
    /// <summary>Mouse thresholds and acceleration (“Enhance pointer precision”), or null when Windows doesn't say.</summary>
    internal static int[]? Mouse()
    {
        var values = new int[3];
        try { return SystemParametersInfoW(GetMouseAction, 0, values, 0) ? values : null; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }
    /// <summary>Sets the mouse parameters the way the Mouse control panel does, saved for this user and announced to apps.</summary>
    internal static void SetMouse(int[] values)
    {
        var buffer = Marshal.AllocHGlobal(12);
        try {
            Marshal.Copy(values, 0, buffer, 3);
            if (!SystemParametersInfoW(SetMouseAction, 0, buffer, UpdateIniFile | SendChange)) throw new IOException($"Windows didn't accept the mouse setting (error {Marshal.GetLastWin32Error()}).");
        } finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);
    // Sets the power mode as Settings → System → Power does (for the current power source). Exported by powrprof.dll
    // since Windows 10 1709, like the getter above, but not in the Windows SDK headers.
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(Guid overlay);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr group, IntPtr setting, IntPtr buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    internal static string? PowerMode()
    {
        try { return PowerGetEffectiveOverlayScheme(out var overlay) == 0 ? WindowsGamingParsing.PowerModeName(overlay) : null; }
        catch (EntryPointNotFoundException) { return null; }
    }
    internal static void SetPowerMode(Guid overlay)
    {
        uint status;
        try { status = PowerSetActiveOverlayScheme(overlay); }
        catch (EntryPointNotFoundException) { throw new IOException("This version of Windows doesn't let apps change the power mode."); }
        if (status != 0) throw new IOException($"Windows didn't accept the power mode (error {status}).");
    }
    internal static Guid? ActivePlan()
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
    private static int? Read(Guid plan, Guid setting, bool ac, Guid? subgroup = null)
    {
        var group = subgroup ?? ProcessorGroup;
        uint status = ac ? PowerReadACValueIndex(IntPtr.Zero, ref plan, ref group, ref setting, out var value) : PowerReadDCValueIndex(IntPtr.Zero, ref plan, ref group, ref setting, out value);
        return status == 0 && value <= 100 ? (int)value : null;
    }
}
