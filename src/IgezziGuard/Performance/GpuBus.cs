using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
namespace IgezziGuard;

/// <summary>
/// How a graphics card is connected (HANKI-GPU-113): its PCI Express link and the largest memory window the processor
/// has onto it. Null values mean Windows didn't report them.
/// </summary>
/// <param name="LinkGeneration">PCIe generation of the link now (1 = 2.5 GT/s … 5 = 32 GT/s). Cards slow the link down when idle.</param>
/// <param name="LargestWindowBytes">The largest memory range assigned to the card. With Resizable BAR it covers all video memory; without it, 256 MB.</param>
public sealed record GpuBusInfo(int? LinkWidth, int? MaxLinkWidth, int? LinkGeneration, int? MaxLinkGeneration, ulong? LargestWindowBytes);

public static class GpuBus
{
    /// <summary>Above this, the processor's window onto video memory is resized: Resizable BAR (Smart Access Memory) is on.</summary>
    public const ulong ClassicWindowBytes = 512UL << 20;
    public static bool? ResizableBarOn(GpuBusInfo? bus) => bus?.LargestWindowBytes is { } window ? window > ClassicWindowBytes : null;

    /// <summary>
    /// Cards whose drivers use Resizable BAR: NVIDIA RTX 30, 40 and 50, AMD Radeon RX 6000 and newer, and Intel Arc,
    /// which needs it to perform properly. By name, because Windows doesn't report support.
    /// </summary>
    public static bool SupportsResizableBar(GpuAdapter adapter) => adapter.Vendor switch {
        GpuVendor.Nvidia => Regex.IsMatch(adapter.Name, @"RTX\s*(30|40|50)\d0", RegexOptions.IgnoreCase),
        GpuVendor.Amd => Regex.IsMatch(adapter.Name, @"RX\s*(6|7|9)\d{3}", RegexOptions.IgnoreCase),
        GpuVendor.Intel => NeedsResizableBar(adapter),
        _ => false
    };
    public static bool NeedsResizableBar(GpuAdapter adapter) => adapter.Vendor == GpuVendor.Intel && adapter.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);

    public static string Link(int generation, int width) => $"PCIe {generation}.0 x{width}";
    public static string Describe(GpuBusInfo bus)
    {
        var parts = new List<string>();
        if (bus.LinkWidth is { } w && bus.LinkGeneration is { } g)
            parts.Add($"{Link(g, w)}" + (bus.MaxLinkWidth is { } mw && bus.MaxLinkGeneration is { } mg && (mw != w || mg != g) ? $" (the card supports {Link(mg, mw)})" : ""));
        if (ResizableBarOn(bus) is { } on) parts.Add(on ? $"Resizable BAR on ({Size(bus.LargestWindowBytes!.Value)} window)" : "Resizable BAR off");
        return string.Join("; ", parts);
    }
    internal static string Size(ulong bytes) => bytes >= 1UL << 30 ? $"{bytes / (double)(1UL << 30):0.#} GB" : $"{bytes >> 20} MB";
}

/// <summary>Reads the link and memory windows through Windows' device configuration (cfgmgr32). Read-only; standard user.</summary>
internal static class GpuBusProbe
{
    private const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private static readonly Guid PciDevice = new("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");

    internal static GpuBusInfo? Read(uint vendorId, uint deviceId)
    {
        try {
            string? instance = DisplayDevices().FirstOrDefault(id => id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)
                && id.Contains($"VEN_{vendorId:X4}&DEV_{deviceId:X4}", StringComparison.OrdinalIgnoreCase));
            if (instance is null || CM_Locate_DevNodeW(out uint node, instance, 0) != 0) return null;
            return new GpuBusInfo(Property(node, 10), Property(node, 12), Property(node, 9), Property(node, 11), LargestWindow(node));
        } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentException or OverflowException) { return null; }
    }

    private static IEnumerable<string> DisplayDevices()
    {
        const uint FilterClass = 0x200, FilterPresent = 0x100;
        if (CM_Get_Device_ID_List_SizeW(out uint length, DisplayClass, FilterClass | FilterPresent) != 0 || length == 0) return [];
        var buffer = new char[length];
        if (CM_Get_Device_ID_ListW(DisplayClass, buffer, length, FilterClass | FilterPresent) != 0) return [];
        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static int? Property(uint node, uint pid)
    {
        var key = new DevPropKey { Fmtid = PciDevice, Pid = pid };
        uint size = 4; var data = new byte[4];
        return CM_Get_DevNode_PropertyW(node, ref key, out uint type, data, ref size, 0) == 0 && type == 7 /* DEVPROP_TYPE_UINT32 */ && size == 4
            ? (int)BitConverter.ToUInt32(data, 0) : null;
    }

    /// <summary>The largest memory range in the card's allocated resources (ResType_Mem and, above 4 GB, ResType_MemLarge).</summary>
    private static ulong? LargestWindow(uint node)
    {
        if (CM_Get_First_Log_Conf(out var conf, node, 2 /* ALLOC_LOG_CONF */) != 0) return null;
        ulong? largest = null;
        try {
            var current = conf;
            while (CM_Get_Next_Res_Des(out var next, current, 0 /* ResType_All */, out uint type, 0) == 0) {
                if (current != conf) CM_Free_Res_Des_Handle(current);
                current = next;
                if (type is not (1 or 7) || CM_Get_Res_Des_Data_Size(out uint size, next, 0) != 0 || size < 24) continue;
                var data = new byte[size];
                if (CM_Get_Res_Des_Data(next, data, size, 0) != 0) continue;
                // MEM_DES and MEM_LARGE_DES: count, type, then the 64-bit start and end of the range.
                ulong start = BitConverter.ToUInt64(data, 8), end = BitConverter.ToUInt64(data, 16);
                if (end > start && (largest is null || end - start + 1 > largest)) largest = end - start + 1;
            }
            if (current != conf) CM_Free_Res_Des_Handle(current);
        } finally { CM_Free_Log_Conf_Handle(conf); }
        return largest;
    }

    [StructLayout(LayoutKind.Sequential)] private struct DevPropKey { public Guid Fmtid; public uint Pid; }
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint length, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_DevNode_PropertyW(uint node, ref DevPropKey key, out uint type, [Out] byte[] buffer, ref uint size, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_First_Log_Conf(out IntPtr conf, uint node, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Next_Res_Des(out IntPtr next, IntPtr current, uint forResource, out uint resourceId, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Res_Des_Data_Size(out uint size, IntPtr resDes, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Res_Des_Data(IntPtr resDes, [Out] byte[] buffer, uint size, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Free_Res_Des_Handle(IntPtr resDes);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Free_Log_Conf_Handle(IntPtr conf);
}
