using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace IgezziGuard;

/// <summary>
/// Reads graphics hardware and display configuration through documented Windows APIs (DXGI, the display
/// configuration API, EnumDisplaySettings and the display-adapter class key). Read-only; failures become notes.
/// </summary>
internal static class GraphicsProbe
{
    internal static GraphicsInventory Collect()
    {
        var notes = new List<string>();
        IReadOnlyList<GpuAdapter> adapters = [];
        IReadOnlyList<DisplayInfo> displays = [];
        try { adapters = Adapters(); } catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException) { notes.Add("Graphics adapters couldn't be read: " + ex.Message); }
        try { displays = Displays(); } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException) { notes.Add("Display configuration couldn't be read: " + ex.Message); }
        var power = Power();
        return new(adapters, displays, power.Portable, power.OnAc, Nvidia.Available, File.Exists(Path.Combine(Environment.SystemDirectory, "amdadlx64.dll")), notes);
    }

    // ---- DXGI: adapters in Windows' high-performance order -----------------------------------------------
    private static readonly Guid Factory1 = new("770aae78-f26f-4dba-a829-253c83d1b387"), Factory6 = new("c1b6694f-ff09-44a9-b03c-77900a0a1d17"), Adapter1 = new("29038f61-3839-4626-91fd-086879011a05");
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
    private delegate int QueryInterfaceFn(IntPtr self, ref Guid riid, out IntPtr result);
    private delegate uint ReleaseFn(IntPtr self);
    private delegate int EnumAdapters1Fn(IntPtr self, uint index, out IntPtr adapter);
    private delegate int EnumByPreferenceFn(IntPtr self, uint index, int preference, ref Guid riid, out IntPtr adapter);
    private delegate int GetDesc1Fn(IntPtr self, out AdapterDesc1 desc);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1 {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh; public uint Flags;
    }
    private static T Method<T>(IntPtr com, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(com), slot * IntPtr.Size));
    private static void Release(IntPtr com) { if (com != IntPtr.Zero) Method<ReleaseFn>(com, 2)(com); }

    private static IReadOnlyList<GpuAdapter> Adapters()
    {
        var factoryId = Factory1;
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref factoryId, out var factory));
        var found = new List<(AdapterDesc1 Desc, int Rank)>();
        try {
            var id6 = Factory6; var adapterId = Adapter1;
            if (Method<QueryInterfaceFn>(factory, 0)(factory, ref id6, out var factory6) == 0) {
                // IDXGIFactory6::EnumAdapterByGpuPreference (slot 29) with DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE.
                try { for (uint i = 0; Method<EnumByPreferenceFn>(factory6, 29)(factory6, i, 2, ref adapterId, out var adapter) == 0; i++) found.Add((Describe(adapter), (int)i)); }
                finally { Release(factory6); }
            } else {
                for (uint i = 0; Method<EnumAdapters1Fn>(factory, 12)(factory, i, out var adapter) == 0; i++) found.Add((Describe(adapter), (int)i));
            }
        } finally { Release(factory); }
        var drivers = DriverDetails();
        return found.Where(f => (f.Desc.Flags & 2) == 0) // DXGI_ADAPTER_FLAG_SOFTWARE: Microsoft Basic Render Driver
            .Select(f => {
                var vendor = GraphicsFacts.Vendor(f.Desc.VendorId);
                ulong dedicated = f.Desc.DedicatedVideoMemory.ToUInt64();
                drivers.TryGetValue((f.Desc.VendorId, f.Desc.DeviceId), out var driver);
                return new GpuAdapter(f.Desc.Description.Trim(), vendor, f.Desc.VendorId, f.Desc.DeviceId, ((long)f.Desc.LuidHigh << 32) | f.Desc.LuidLow,
                    Math.Max(dedicated, driver.Memory), f.Desc.SharedSystemMemory.ToUInt64(), f.Rank, driver.Version, driver.Date, GraphicsFacts.LikelyIntegrated(vendor, Math.Max(dedicated, driver.Memory)),
                    GpuBusProbe.Read(f.Desc.VendorId, f.Desc.DeviceId));
            }).ToArray();
    }
    private static AdapterDesc1 Describe(IntPtr adapter)
    {
        try { Marshal.ThrowExceptionForHR(Method<GetDesc1Fn>(adapter, 10)(adapter, out var desc)); return desc; }
        finally { Release(adapter); }
    }

    /// <summary>Driver version, date and memory size from the display-adapter class key, keyed by PCI vendor and device.</summary>
    private static Dictionary<(uint, uint), (string? Version, DateTime? Date, ulong Memory)> DriverDetails()
    {
        var result = new Dictionary<(uint, uint), (string?, DateTime?, ulong)>();
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (root is null) return result;
        foreach (var name in root.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit))) {
            try {
                using var key = root.OpenSubKey(name);
                var match = System.Text.RegularExpressions.Regex.Match(key?.GetValue("MatchingDeviceId") as string ?? "", @"ven_([0-9a-f]{4})&dev_([0-9a-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (key is null || !match.Success) continue;
                var ids = (Convert.ToUInt32(match.Groups[1].Value, 16), Convert.ToUInt32(match.Groups[2].Value, 16));
                DateTime? date = DateTime.TryParse(key.GetValue("DriverDate") as string, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
                ulong memory = key.GetValue("HardwareInformation.qwMemorySize") switch { long l => (ulong)l, byte[] b when b.Length == 8 => BitConverter.ToUInt64(b), int i => (uint)i, byte[] b when b.Length == 4 => BitConverter.ToUInt32(b), _ => 0 };
                result[ids] = (key.GetValue("DriverVersion") as string, date, memory);
            } catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return result;
    }

    // ---- Displays: the display configuration API, plus EnumDisplaySettings for the modes each one offers ----
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; public readonly long Value => ((long)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator, Denominator; public readonly double Hz => Denominator == 0 ? 0 : (double)Numerator / Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct PathSource { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] private struct PathTarget { public Luid Adapter; public uint Id, ModeIndex, OutputTechnology, Rotation, Scaling; public Rational Refresh; public uint ScanLine; public int Available; public uint Status; }
    [StructLayout(LayoutKind.Sequential)] private struct PathInfo { public PathSource Source; public PathTarget Target; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] private struct ModeInfo { [FieldOffset(0)] public uint InfoType; }
    [StructLayout(LayoutKind.Sequential)] private struct InfoHeader { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetName {
        public InfoHeader Header; public uint Flags, OutputTechnology; public ushort EdidManufacturer, EdidProduct; public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName { public InfoHeader Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiName; }
    [StructLayout(LayoutKind.Sequential)] private struct AdvancedColor { public InfoHeader Header; public uint Value, Encoding, BitsPerChannel; }
    // DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 (Windows 11 24H2): bit 6 wideColorSupported, bit 7 wideColorUserEnabled (ACM).
    [StructLayout(LayoutKind.Sequential)] private struct AdvancedColor2 { public InfoHeader Header; public uint Value, Encoding, BitsPerChannel, ActiveColorMode; }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] ModeInfo[] modes, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetTargetName(ref TargetName info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetSourceName(ref SourceName info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetAdvancedColor(ref AdvancedColor info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetAdvancedColor2(ref AdvancedColor2 info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevMode {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra; public uint Fields;
        public int PositionX, PositionY; public uint DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels; public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool EnumDisplaySettingsW(string device, int mode, ref DevMode devMode);
    internal static DevMode NewDevMode() => new() { DeviceName = "", FormName = "", Size = (ushort)Marshal.SizeOf<DevMode>() };

    internal static IReadOnlyList<DisplayInfo> Displays(bool includeModes = true)
    {
        const uint OnlyActivePaths = 2;
        for (int attempt = 0; attempt < 3; attempt++) {
            if (GetDisplayConfigBufferSizes(OnlyActivePaths, out var pathCount, out var modeCount) != 0) throw new InvalidOperationException("Display configuration unavailable.");
            var paths = new PathInfo[pathCount]; var modes = new ModeInfo[modeCount];
            int status = QueryDisplayConfig(OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (status == 122) continue; // ERROR_INSUFFICIENT_BUFFER: the configuration changed between the two calls.
            if (status != 0) throw new InvalidOperationException($"Display configuration unavailable (error {status}).");
            var result = new List<DisplayInfo>();
            foreach (var path in paths.Take((int)pathCount)) {
                var source = new SourceName { Header = new() { Type = 1, Size = (uint)Marshal.SizeOf<SourceName>(), Adapter = path.Source.Adapter, Id = path.Source.Id }, GdiName = "" };
                if (GetSourceName(ref source) != 0) continue;
                var target = new TargetName { Header = new() { Type = 2, Size = (uint)Marshal.SizeOf<TargetName>(), Adapter = path.Target.Adapter, Id = path.Target.Id }, FriendlyName = "", DevicePath = "" };
                string name = GetTargetName(ref target) == 0 && !string.IsNullOrWhiteSpace(target.FriendlyName) ? target.FriendlyName : "Display";
                var color = new AdvancedColor { Header = new() { Type = 9, Size = (uint)Marshal.SizeOf<AdvancedColor>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
                bool colorKnown = GetAdvancedColor(ref color) == 0;
                var color2 = new AdvancedColor2 { Header = new() { Type = 15, Size = (uint)Marshal.SizeOf<AdvancedColor2>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
                bool acmKnown = GetAdvancedColor2(ref color2) == 0; // Fails before Windows 11 24H2.
                var current = NewDevMode();
                if (!EnumDisplaySettingsW(source.GdiName, -1, ref current)) continue;
                double refresh = path.Target.Refresh.Hz > 0 ? path.Target.Refresh.Hz : current.DisplayFrequency;
                var supported = new HashSet<DisplayMode>();
                var mode = NewDevMode();
                for (int i = 0; includeModes && i < 2000 && EnumDisplaySettingsW(source.GdiName, i, ref mode); i++)
                    if (mode.DisplayFrequency > 1) supported.Add(new DisplayMode((int)mode.PelsWidth, (int)mode.PelsHeight, mode.DisplayFrequency));
                result.Add(new DisplayInfo(name, source.GdiName, path.Source.Adapter.Value, new DisplayMode((int)current.PelsWidth, (int)current.PelsHeight, refresh),
                    supported.OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.RefreshHz).ToArray(),
                    colorKnown ? (color.Value & 1) != 0 : null, colorKnown ? (color.Value & 2) != 0 : null,
                    current.PositionX == 0 && current.PositionY == 0, Connection(path.Target.OutputTechnology),
                    colorKnown ? (int)color.Encoding : null, colorKnown && color.BitsPerChannel is > 0 and <= 16 ? (int)color.BitsPerChannel : null,
                    acmKnown ? (color2.Value & (1 << 6)) != 0 : null, acmKnown ? (color2.Value & (1 << 7)) != 0 : null));
            }
            return result;
        }
        throw new InvalidOperationException("The display configuration kept changing while it was read.");
    }
    private static string Connection(uint technology) => technology switch {
        0 => "VGA", 4 => "DVI", 5 => "HDMI", 6 => "Built-in (LVDS)", 10 => "DisplayPort", 11 => "Built-in (DisplayPort)", 15 => "Miracast", 18 => "USB-C (DisplayPort)", 0x80000000 => "Built-in", _ => "Other"
    };

    // ---- Power source and battery ------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte AcLine, BatteryFlag, BatteryPercent, SystemStatus; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    internal static (bool? Portable, bool OnAc) Power()
    {
        if (!GetSystemPowerStatus(out var status)) return (null, true);
        // BatteryFlag 128 = no system battery; 255 = unknown.
        bool? portable = status.BatteryFlag == 255 ? null : (status.BatteryFlag & 128) == 0;
        return (portable, status.AcLine != 0);
    }
}
