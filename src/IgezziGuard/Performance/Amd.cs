using System.Runtime.InteropServices;
namespace IgezziGuard;

/// <summary>
/// Radeon 3D settings through AMD's ADLX (amdadlx64.dll, installed with AMD Software). ADLX objects are C structs whose
/// first field points to a table of functions; the slot numbers below follow the method order in AMD's ADLX SDK
/// headers (ISystem.h, I3DSettings.h, version 2.0). Every call is checked, every object is released, and a missing
/// driver or failed call becomes a clear message. Not yet tested on AMD hardware.
/// </summary>
internal static class Amd
{
    private const string Library = "amdadlx64.dll";
    // ADLXDefines.h: ADLX_MAKE_FULL_VER(2, 0, 0, 125).
    private const ulong HeaderVersion = (2UL << 48) | (0UL << 32) | (0UL << 16) | 125UL;
    private const int Ok = 0, AlreadyEnabled = 1, AlreadyInitialized = 2;
    private static bool Succeeded(int result) => result is Ok or AlreadyEnabled or AlreadyInitialized;

    // IADLXSystem (no Acquire/Release): GetHybridGraphicsType 0, GetGPUs 1, QueryInterface 2, GetDisplaysServices 3,
    // GetDesktopsServices 4, GetGPUsChangedHandling 5, EnableLog 6, Get3DSettingsServices 7.
    private const int SystemGetGpus = 1, SystemGet3DSettings = 7;
    // Every other interface starts with Acquire 0, Release 1, QueryInterface 2.
    private const int Release = 1;
    // IADLXGPUList: Size 3, Empty 4, Begin 5, End 6, At 7, Clear 8, Remove_Back 9, Add_Back 10, At_GPUList 11.
    private const int ListSize = 3, ListAtGpu = 11;
    // IADLXGPU: VendorId 3, ASICFamilyType 4, Type 5, IsExternal 6, Name 7, DriverPath 8, PNPString 9, HasDesktops 10,
    // TotalVRAM 11, VRAMType 12, BIOSInfo 13, DeviceId 14, RevisionId 15, SubSystemId 16, SubSystemVendorId 17, UniqueId 18.
    private const int GpuTypeSlot = 5, GpuNameSlot = 7, GpuIdSlot = 18, GpuTypeDiscrete = 2;
    // IADLX3DSettingsServices: GetAntiLag 3, GetChill 4, GetBoost 5, GetImageSharpening 6, GetEnhancedSync 7,
    // GetWaitForVerticalRefresh 8, GetFrameRateTargetControl 9, GetAntiAliasing 10, GetMorphologicalAntiAliasing 11, GetAnisotropicFiltering 12.
    private static int ServiceSlot(AmdSettingKind kind) => kind switch {
        AmdSettingKind.AntiLag => 3, AmdSettingKind.Chill => 4, AmdSettingKind.Boost => 5, AmdSettingKind.ImageSharpening => 6, AmdSettingKind.EnhancedSync => 7,
        AmdSettingKind.WaitForVerticalRefresh => 8, AmdSettingKind.FrameRateTargetControl => 9, _ => 12
    };
    // Each feature: IsSupported 3, IsEnabled 4, then (from I3DSettings.h):
    //   AntiLag, EnhancedSync: SetEnabled 5
    //   Chill: GetFPSRange 5, GetMinFPS 6, GetMaxFPS 7, SetEnabled 8, SetMinFPS 9, SetMaxFPS 10
    //   Boost: GetResolutionRange 5, GetResolution 6, SetEnabled 7, SetResolution 8
    //   ImageSharpening: GetSharpnessRange 5, GetSharpness 6, SetEnabled 7, SetSharpness 8
    //   WaitForVerticalRefresh: GetMode 5, SetMode 6
    //   FrameRateTargetControl: GetFPSRange 5, GetFPS 6, SetEnabled 7, SetFPS 8
    //   AnisotropicFiltering: GetLevel 5, SetEnabled 6, SetLevel 7
    private const int IsSupported = 3, IsEnabled = 4;
    private sealed record Slots(int? Range, int? Get, int? Get2, int? SetEnabled, int? Set, int? Set2);
    private static Slots SlotsFor(AmdSettingKind kind) => kind switch {
        AmdSettingKind.AntiLag or AmdSettingKind.EnhancedSync => new(null, null, null, 5, null, null),
        AmdSettingKind.Chill => new(5, 6, 7, 8, 9, 10),
        AmdSettingKind.Boost or AmdSettingKind.ImageSharpening or AmdSettingKind.FrameRateTargetControl => new(5, 6, null, 7, 8, null),
        AmdSettingKind.WaitForVerticalRefresh => new(null, 5, null, null, 6, null),
        _ => new(null, 5, null, 6, 7, null)
    };

    [StructLayout(LayoutKind.Sequential)] private struct IntRange { public int Min, Max, Step; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitializeFn(ulong version, out IntPtr system);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NoArgsFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FullVersionFn(out ulong version);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UnaryFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint SizeFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPointerFn(IntPtr self, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AtFn(IntPtr self, uint index, out IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FeatureFn(IntPtr self, IntPtr gpu, out IntPtr feature);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBoolFn(IntPtr self, out byte value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetBoolFn(IntPtr self, byte value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetIntFn(IntPtr self, out int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetIntFn(IntPtr self, int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetRangeFn(IntPtr self, out IntRange range);

    private static T Method<T>(IntPtr self, int slot) where T : Delegate
    {
        if (self == IntPtr.Zero) throw new AmdException("The AMD driver returned an empty object.");
        var table = Marshal.ReadIntPtr(self);
        var function = Marshal.ReadIntPtr(table, slot * IntPtr.Size);
        return function == IntPtr.Zero ? throw new AmdException("The AMD driver doesn't provide this function.") : Marshal.GetDelegateForFunctionPointer<T>(function);
    }
    private static void Check(int result, string action) { if (!Succeeded(result)) throw new AmdException($"{action} failed (ADLX result {result})."); }
    private static void Free(IntPtr self) { if (self != IntPtr.Zero) Method<UnaryFn>(self, Release)(self); }

    /// <summary>An initialized ADLX session with the first discrete Radeon GPU (or the first one). Dispose terminates ADLX.</summary>
    internal sealed class Session : IDisposable
    {
        private readonly IntPtr library;
        private readonly NoArgsFn? terminate;
        private bool initialized;
        private IntPtr gpus, gpu, services;
        internal int GpuId { get; }
        internal string GpuName { get; } = "AMD Radeon";

        internal Session()
        {
            if (!NativeLibrary.TryLoad(Library, out library)) throw new AmdException("AMD's driver interface (ADLX) isn't installed. It comes with AMD Software: Adrenalin Edition.");
            try {
                if (!NativeLibrary.TryGetExport(library, "ADLXInitialize", out var start) || !NativeLibrary.TryGetExport(library, "ADLXTerminate", out var stop))
                    throw new AmdException("AMD's driver interface (ADLX) on this PC is missing functions Hanki needs. Updating AMD Software should fix this.");
                terminate = Marshal.GetDelegateForFunctionPointer<NoArgsFn>(stop);
                ulong version = HeaderVersion;
                // An older driver gets the version it knows; the interfaces Hanki uses are in every ADLX version.
                if (NativeLibrary.TryGetExport(library, "ADLXQueryFullVersion", out var query) && Marshal.GetDelegateForFunctionPointer<FullVersionFn>(query)(out var installed) == Ok && installed < version)
                    version = installed;
                Check(Marshal.GetDelegateForFunctionPointer<InitializeFn>(start)(version, out var system), "Starting AMD's driver interface");
                initialized = true;
                Check(Method<GetPointerFn>(system, SystemGetGpus)(system, out gpus), "Listing AMD graphics cards");
                uint count = Method<SizeFn>(gpus, ListSize)(gpus);
                if (count == 0) throw new AmdException("AMD's driver interface found no Radeon graphics card.");
                // The first discrete GPU, otherwise the first one.
                bool chosenDiscrete = false;
                for (uint i = 0; i < count && i < 8 && !chosenDiscrete; i++) {
                    Check(Method<AtFn>(gpus, ListAtGpu)(gpus, i, out var candidate), "Reading an AMD graphics card");
                    bool discrete = Method<GetIntFn>(candidate, GpuTypeSlot)(candidate, out var type) == Ok && type == GpuTypeDiscrete;
                    if (gpu == IntPtr.Zero || discrete) { Free(gpu); gpu = candidate; chosenDiscrete = discrete; } else Free(candidate);
                }
                Check(Method<GetIntFn>(gpu, GpuIdSlot)(gpu, out var id), "Reading the graphics card's id");
                GpuId = id;
                if (Method<GetPointerFn>(gpu, GpuNameSlot)(gpu, out var name) == Ok && name != IntPtr.Zero) GpuName = Marshal.PtrToStringAnsi(name) ?? GpuName;
                Check(Method<GetPointerFn>(system, SystemGet3DSettings)(system, out services), "Opening Radeon 3D settings");
            } catch { Dispose(); throw; }
        }
        private IntPtr Feature(AmdSettingKind kind)
        {
            Check(Method<FeatureFn>(services, ServiceSlot(kind))(services, gpu, out var feature), "Opening " + AmdSettings.Name(kind));
            return feature;
        }

        /// <summary>The setting, or null when this GPU or driver doesn't support it.</summary>
        internal AmdSetting? Read(AmdSettingKind kind)
        {
            var feature = Feature(kind);
            try {
                Check(Method<GetBoolFn>(feature, IsSupported)(feature, out var supported), "Checking " + AmdSettings.Name(kind));
                if (supported == 0) return null;
                var slots = SlotsFor(kind);
                bool enabled = true;
                if (slots.SetEnabled is not null) { Check(Method<GetBoolFn>(feature, IsEnabled)(feature, out var on), "Reading " + AmdSettings.Name(kind)); enabled = on != 0; }
                int? value = null, value2 = null, min = null, max = null;
                if (slots.Get is { } get) { Check(Method<GetIntFn>(feature, get)(feature, out var v), "Reading " + AmdSettings.Name(kind)); value = v; }
                if (slots.Get2 is { } get2) { Check(Method<GetIntFn>(feature, get2)(feature, out var v2), "Reading " + AmdSettings.Name(kind)); value2 = v2; }
                if (slots.Range is { } range && Method<GetRangeFn>(feature, range)(feature, out var r) == Ok) { min = r.Min; max = r.Max; }
                if (kind == AmdSettingKind.AnisotropicFiltering) { min = 2; max = 16; }
                return new AmdSetting(kind, enabled, value, value2, min, max);
            } finally { Free(feature); }
        }

        /// <summary>Applies a Recovery state: values first, then on or off. Hanki reads it back afterwards to verify.</summary>
        internal void Write(AmdSettingKind kind, string state)
        {
            if (AmdSettings.Parse(kind, state) is not { } s) throw new AmdException("Invalid Radeon setting value.");
            var feature = Feature(kind);
            try {
                Check(Method<GetBoolFn>(feature, IsSupported)(feature, out var supported), "Checking " + AmdSettings.Name(kind));
                if (supported == 0) throw new AmdException(AmdSettings.Name(kind) + " isn't supported on this graphics card or driver.");
                var slots = SlotsFor(kind);
                if (slots.Set is { } set && s.Value is { } v) Check(Method<SetIntFn>(feature, set)(feature, v), "Changing " + AmdSettings.Name(kind));
                if (slots.Set2 is { } set2 && s.Value2 is { } v2) Check(Method<SetIntFn>(feature, set2)(feature, v2), "Changing " + AmdSettings.Name(kind));
                if (slots.SetEnabled is { } setEnabled) Check(Method<SetBoolFn>(feature, setEnabled)(feature, s.Enabled ? (byte)1 : (byte)0), "Turning " + AmdSettings.Name(kind) + (s.Enabled ? " on" : " off"));
            } finally { Free(feature); }
        }

        internal AmdGpuSettings ReadAll() =>
            new(GpuId, GpuName, Enum.GetValues<AmdSettingKind>().Select(k => { try { return Read(k); } catch (AmdException) { return null; } }).OfType<AmdSetting>().ToArray());

        public void Dispose()
        {
            // Objects first, then ADLX itself; ADLX reports orphaned objects otherwise.
            try { Free(services); Free(gpu); Free(gpus); } catch (AmdException) { }
            services = gpu = gpus = IntPtr.Zero;
            if (initialized) terminate?.Invoke();
            initialized = false;
            if (library != IntPtr.Zero) NativeLibrary.Free(library);
        }
    }

    /// <summary>The state of one setting for Recovery; the GPU must be the one the target names.</summary>
    internal static string ReadState(string target)
    {
        var (gpu, kind) = AmdSettings.ParseTarget(target);
        using var session = new Session();
        if (session.GpuId != gpu) throw new AmdException("The Radeon graphics card this change was made on isn't the one AMD's driver reports now.");
        return session.Read(kind)?.State ?? throw new AmdException(AmdSettings.Name(kind) + " isn't supported on this graphics card or driver.");
    }
    internal static void WriteState(string target, string state)
    {
        var (gpu, kind) = AmdSettings.ParseTarget(target);
        using var session = new Session();
        if (session.GpuId != gpu) throw new AmdException("The Radeon graphics card this change was made on isn't the one AMD's driver reports now.");
        session.Write(kind, state);
    }
    internal static AmdGpuSettings ReadSettings() { using var session = new Session(); return session.ReadAll(); }
}

internal sealed class AmdException(string message) : Exception(message);
