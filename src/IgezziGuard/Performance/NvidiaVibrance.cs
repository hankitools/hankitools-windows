using System.Runtime.InteropServices;
namespace IgezziGuard;

internal static partial class Nvidia
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DisplayHandleFn([MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int VibranceFn(IntPtr display, uint output, IntPtr info);

    // Private NVAPI DVC Ex ABI: version + current/min/max/default, all 32-bit.
    // Reference: https://github.com/jNizM/NVIDIA_NvAPI/blob/master/src/Class_NvAPI.ahk
    // Unsupported driver interfaces fail through Function/Check; no guessed fallback.
    internal static VibranceRange ReadVibrance(string display) => Vibrance(display, null);
    internal static void WriteVibrance(string display, int level) => Vibrance(display, level);

    private static VibranceRange Vibrance(string display, int? level)
    {
        if (!Available) throw new NvidiaException("NVIDIA Digital Vibrance is unavailable on this PC.");
        Check(Function<DisplayHandleFn>(0x35C29134)(display, out var handle), "Finding the NVIDIA display");
        var info = Buffer(20, 1);
        try {
            Check(Function<VibranceFn>(0x0E45002D)(handle, 0, info), "Reading Digital Vibrance");
            var range = new VibranceRange(Marshal.ReadInt32(info, 4), Marshal.ReadInt32(info, 8), Marshal.ReadInt32(info, 12), Marshal.ReadInt32(info, 16));
            range.Validate();
            if (level is { } value) {
                if (value < range.Minimum || value > range.Maximum) throw new IOException("Digital Vibrance is outside the driver's range.");
                Marshal.WriteInt32(info, 4, value);
                Check(Function<VibranceFn>(0x4A82C2B1)(handle, 0, info), "Setting Digital Vibrance");
            }
            return range;
        } finally { Marshal.FreeHGlobal(info); }
    }
}
