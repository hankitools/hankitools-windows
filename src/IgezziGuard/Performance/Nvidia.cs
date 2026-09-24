using System.Runtime.InteropServices;
using System.Text;
namespace IgezziGuard;

/// <summary>
/// NVIDIA driver settings through NVAPI (nvapi64.dll, installed with the NVIDIA driver). The DRS functions read and
/// write the same profiles as NVIDIA Control Panel. Every call is checked; a missing driver or failed call becomes a
/// clear message, never a guessed value.
/// </summary>
internal static class Nvidia
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NoArgs();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OutHandle(out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OneHandle(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SessionOut(IntPtr session, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSettingFn(IntPtr session, IntPtr profile, uint id, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSettingFn(IntPtr session, IntPtr profile, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SettingIdFn(IntPtr session, IntPtr profile, uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FindApplicationFn(IntPtr session, IntPtr name, out IntPtr profile, IntPtr application);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ProfileInfoFn(IntPtr session, IntPtr profile, IntPtr info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateProfileFn(IntPtr session, IntPtr info, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ErrorMessageFn(int status, IntPtr text);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ProfileFn(IntPtr session, IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SettingNameFn(uint id, IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SettingValuesFn(uint id, ref uint count, IntPtr values);
    private const uint SettingNameId = 0xD61CBE6E, SettingValuesId = 0x2EC39F90;
    private const int ValuesSize = 12 + 4100 + 100 * 4100, DefaultValueOffset = 12;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, bool> trusted = new();
    /// <summary>Whether the driver names this id as Hanki expects (checked once per run).</summary>
    internal static bool Trusted(NvidiaSetting setting) =>
        trusted.GetOrAdd(setting.Id, id => { try { return SettingName(id) is { Length: > 0 } name && NvidiaSettings.NameMatches(setting, name); } catch (NvidiaException) { return false; } });

    /// <summary>The driver's name for a setting id, or null when the driver doesn't know the id.</summary>
    internal static string? SettingName(uint id)
    {
        var name = Marshal.AllocHGlobal(UnicodeBytes);
        try {
            for (int i = 0; i < UnicodeBytes; i += 4) Marshal.WriteInt32(name, i, 0);
            return Function<SettingNameFn>(SettingNameId)(id, name) == Ok ? ReadUnicode(name, 0) : null;
        } finally { Marshal.FreeHGlobal(name); }
    }
    /// <summary>The driver's default value for a DWORD setting, or null when it doesn't say.</summary>
    internal static uint? DefaultValue(uint id)
    {
        var values = Buffer(ValuesSize, 1);
        try {
            uint count = 100;
            return Function<SettingValuesFn>(SettingValuesId)(id, ref count, values) == Ok && Marshal.ReadInt32(values, 8) == 0 ? (uint)Marshal.ReadInt32(values, DefaultValueOffset) : null;
        } finally { Marshal.FreeHGlobal(values); }
    }

    // Interface ids from NVIDIA's NVAPI SDK (nvapi_interface.h).
    private const uint InitializeId = 0x0150E828, ErrorMessageId = 0x6C2D048C, CreateSessionId = 0x0694D52E, DestroySessionId = 0xDAD9CFF8,
        LoadSettingsId = 0x375DBD6B, SaveSettingsId = 0xFCBC7E14, BaseProfileId = 0xDA8466A0, GlobalProfileId = 0x617BFF9F, GetSettingId = 0x73BF8338,
        SetSettingId = 0x577DD202, DeleteSettingId = 0xE4A26362, RestoreDefaultId = 0x53F0381E, FindApplicationId = 0xEEE566B2, ProfileInfoId = 0x61CD6FD6,
        CreateProfileId = 0xCC176068, CreateApplicationId = 0x4347A9DE, DeleteProfileId = 0x17093206;
    internal const int Ok = 0, SettingNotFound = -160, ProfileNotFound = -163, ExecutableNotFound = -166;

    private static readonly Lazy<bool> available = new(() => {
        try { return Function<NoArgs>(InitializeId)() == Ok; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or NvidiaException) { return false; }
    });
    /// <summary>The NVIDIA driver is installed and NVAPI initialized.</summary>
    internal static bool Available => available.Value;

    private static T Function<T>(uint id) where T : Delegate
    {
        var pointer = QueryInterface(id);
        return pointer == IntPtr.Zero ? throw new NvidiaException($"This NVIDIA driver doesn't provide function 0x{id:X8}.") : Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }
    internal static string Message(int status)
    {
        if (status == Ok) return "OK";
        var buffer = Marshal.AllocHGlobal(64);
        try {
            for (int i = 0; i < 64; i++) Marshal.WriteByte(buffer, i, 0);
            return Function<ErrorMessageFn>(ErrorMessageId)(status, buffer) == Ok ? $"{Marshal.PtrToStringAnsi(buffer)} ({status})" : $"NVAPI error {status}";
        } catch (NvidiaException) { return $"NVAPI error {status}"; }
    }
    private static void Check(int status, string action) { if (status != Ok) throw new NvidiaException($"{action} failed: {Message(status)}"); }

    // Structure sizes from the SDK; each version field is size | (version << 16).
    private const int UnicodeBytes = 4096, SettingSize = 12320, ProfileSize = 4116, ApplicationSize = 12296;
    private const int SettingNameOffset = 4, SettingIdOffset = 4100, SettingTypeOffset = 4104, SettingLocationOffset = 4108, CurrentPredefinedOffset = 4112, PredefinedValueOffset = 4120, CurrentValueOffset = 8220;
    private static IntPtr Buffer(int size, int version)
    {
        var buffer = Marshal.AllocHGlobal(size);
        for (int i = 0; i < size; i += 4) Marshal.WriteInt32(buffer, i, 0);
        Marshal.WriteInt32(buffer, 0, size | (version << 16));
        return buffer;
    }
    private static void WriteUnicode(IntPtr buffer, int offset, string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text.Length > 2000 ? text[..2000] : text);
        Marshal.Copy(bytes, 0, buffer + offset, bytes.Length);
        Marshal.WriteInt16(buffer, offset + bytes.Length, 0);
    }
    private static string ReadUnicode(IntPtr buffer, int offset) => Marshal.PtrToStringUni(buffer + offset) ?? "";

    /// <summary>A loaded DRS session. Dispose destroys it; nothing is saved unless Save is called.</summary>
    internal sealed class Session : IDisposable
    {
        internal IntPtr Handle { get; }
        internal Session()
        {
            if (!Available) throw new NvidiaException("The NVIDIA driver interface (NVAPI) isn't available on this PC.");
            Check(Function<OutHandle>(CreateSessionId)(out var handle), "Opening NVIDIA driver settings");
            Handle = handle;
            try { Check(Function<OneHandle>(LoadSettingsId)(Handle), "Loading NVIDIA driver settings"); }
            catch { Dispose(); throw; }
        }
        public void Dispose() { try { Function<OneHandle>(DestroySessionId)(Handle); } catch (NvidiaException) { } }
        internal IntPtr GlobalProfile() { Check(Function<SessionOut>(GlobalProfileId)(Handle, out var profile), "Reading the global profile"); return profile; }

        /// <summary>The profile that holds an executable, or zero when NVIDIA has no profile for it.</summary>
        internal IntPtr FindApplication(string executable)
        {
            var name = Marshal.AllocHGlobal(UnicodeBytes); var application = Buffer(ApplicationSize, 1);
            try {
                for (int i = 0; i < UnicodeBytes; i += 4) Marshal.WriteInt32(name, i, 0);
                WriteUnicode(name, 0, executable);
                int status = Function<FindApplicationFn>(FindApplicationId)(Handle, name, out var profile, application);
                if (status is ExecutableNotFound or ProfileNotFound) return IntPtr.Zero;
                Check(status, "Finding the application profile");
                return profile;
            } finally { Marshal.FreeHGlobal(name); Marshal.FreeHGlobal(application); }
        }
        /// <summary>A new user profile "Hanki: game.exe" holding just this executable.</summary>
        internal IntPtr CreateApplicationProfile(string executable)
        {
            var info = Buffer(ProfileSize, 1); var application = Buffer(ApplicationSize, 1);
            try {
                WriteUnicode(info, 4, HankiProfilePrefix + executable);
                Check(Function<CreateProfileFn>(CreateProfileId)(Handle, info, out var profile), "Creating a profile for the game");
                WriteUnicode(application, 8, executable);
                int status = Function<SetSettingFn>(CreateApplicationId)(Handle, profile, application);
                if (status != Ok) { Function<ProfileFn>(DeleteProfileId)(Handle, profile); Check(status, "Adding the game to its profile"); }
                return profile;
            } finally { Marshal.FreeHGlobal(info); Marshal.FreeHGlobal(application); }
        }
        internal (string Name, bool Predefined) ProfileInfo(IntPtr profile)
        {
            var info = Buffer(ProfileSize, 1);
            try { Check(Function<ProfileInfoFn>(ProfileInfoId)(Handle, profile, info), "Reading the profile"); return (ReadUnicode(info, 4), Marshal.ReadInt32(info, 4104) != 0); }
            finally { Marshal.FreeHGlobal(info); }
        }
        /// <summary>A catalog setting as the driver reports it for this profile, or null when it isn't set anywhere.</summary>
        internal (uint Value, NvidiaSettingSource Source, bool Predefined, string DriverName, uint Type)? Read(IntPtr profile, uint id)
        {
            var setting = Buffer(SettingSize, 1);
            try {
                int status = Function<GetSettingFn>(GetSettingId)(Handle, profile, id, setting);
                if (status == SettingNotFound) return null;
                Check(status, "Reading a driver setting");
                return ((uint)Marshal.ReadInt32(setting, CurrentValueOffset), NvidiaSettings.Source((uint)Marshal.ReadInt32(setting, SettingLocationOffset)),
                    Marshal.ReadInt32(setting, CurrentPredefinedOffset) != 0, ReadUnicode(setting, SettingNameOffset), (uint)Marshal.ReadInt32(setting, SettingTypeOffset));
            } finally { Marshal.FreeHGlobal(setting); }
        }
        internal NvidiaProfileView View(IntPtr profile, string? application)
        {
            var (name, predefined) = ProfileInfo(profile);
            var values = new List<NvidiaValue>();
            foreach (var setting in NvidiaSettings.Catalog) {
                // Only ids the driver itself names as expected, holding a DWORD value, are read.
                if (!Trusted(setting)) continue;
                var read = Read(profile, setting.Id);
                if (read is { } r && r.Type != 0) continue;
                values.Add(read is { } v ? new NvidiaValue(setting, v.Value, v.Source, v.Predefined)
                    : new NvidiaValue(setting, DefaultValue(setting.Id), NvidiaSettingSource.DriverDefault, false));
            }
            return new(name, application, predefined, values);
        }
    }

    /// <summary>Profiles Hanki creates for games NVIDIA has no profile for are named with this prefix.</summary>
    internal const string HankiProfilePrefix = "Hanki: ";

    /// <summary>
    /// The state of one setting in a game's own profile: "0x…" (a value set by the user), "predefined:0x…" (NVIDIA's
    /// value for this game) or "default" (not set for this game, so the global value applies).
    /// </summary>
    internal static string ReadApplicationSetting(string executable, uint id)
    {
        var setting = NvidiaSettings.Get(id);
        if (!Trusted(setting)) throw new NvidiaException("This driver doesn't name the setting as expected, so Hanki leaves it alone.");
        using var session = new Session();
        var profile = session.FindApplication(executable);
        if (profile == IntPtr.Zero) return "default";
        var read = session.Read(profile, id);
        return read is { Source: NvidiaSettingSource.ThisProfile } r ? (r.Predefined ? "predefined:" : "") + NvidiaSettings.Hex(r.Value) : "default";
    }

    /// <summary>Writes one of the three states read by ReadApplicationSetting and saves the driver settings.</summary>
    internal static void WriteApplicationSetting(string executable, uint id, string value)
    {
        var setting = NvidiaSettings.Get(id);
        if (!Trusted(setting)) throw new NvidiaException("This driver doesn't name the setting as expected, so Hanki leaves it alone.");
        using var session = new Session();
        var profile = session.FindApplication(executable);
        if (value == "default") {
            if (profile == IntPtr.Zero) return;
            int status = Function<SettingIdFn>(DeleteSettingId)(session.Handle, profile, id);
            if (status is not (Ok or SettingNotFound)) Check(status, "Removing the game setting");
        } else if (value.StartsWith("predefined:", StringComparison.Ordinal)) {
            if (profile == IntPtr.Zero) throw new NvidiaException("The game's NVIDIA profile is gone.");
            Check(Function<SettingIdFn>(RestoreDefaultId)(session.Handle, profile, id), "Restoring NVIDIA's value for the game");
        } else {
            if (!value.StartsWith("0x", StringComparison.Ordinal) || !uint.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out var number)) throw new NvidiaException("Invalid setting value.");
            if (profile == IntPtr.Zero) profile = session.CreateApplicationProfile(executable);
            var buffer = Buffer(SettingSize, 1);
            try {
                Marshal.WriteInt32(buffer, SettingIdOffset, (int)id);
                Marshal.WriteInt32(buffer, SettingTypeOffset, 0); // NVDRS_DWORD_TYPE
                Marshal.WriteInt32(buffer, CurrentValueOffset, (int)number);
                Check(Function<SetSettingFn>(SetSettingId)(session.Handle, profile, buffer), "Changing the game setting");
            } finally { Marshal.FreeHGlobal(buffer); }
        }
        Check(Function<OneHandle>(SaveSettingsId)(session.Handle), "Saving NVIDIA driver settings");
    }

    /// <summary>Removes a profile Hanki created for a game. NVIDIA's own and other profiles are never removed.</summary>
    internal static bool RemoveHankiProfile(string executable)
    {
        using var session = new Session();
        var profile = session.FindApplication(executable);
        if (profile == IntPtr.Zero) return false;
        var (name, predefined) = session.ProfileInfo(profile);
        if (predefined || !name.StartsWith(HankiProfilePrefix, StringComparison.Ordinal)) throw new NvidiaException($"{executable} is in the profile “{name}”, which Hanki didn't create, so it isn't removed.");
        Check(Function<ProfileFn>(DeleteProfileId)(session.Handle, profile), "Removing the Hanki profile");
        Check(Function<OneHandle>(SaveSettingsId)(session.Handle), "Saving NVIDIA driver settings");
        return true;
    }

    /// <summary>Raw ids, the driver's own setting names and values, for technical details and support.</summary>
    internal static string TechnicalReport(IEnumerable<string> executables)
    {
        using var session = new Session();
        var text = new StringBuilder();
        text.AppendLine("Setting ids as the driver names them:");
        foreach (var s in NvidiaSettings.Catalog) {
            string? name = null; uint? standard = null;
            try { name = SettingName(s.Id); standard = DefaultValue(s.Id); } catch (NvidiaException ex) { name = ex.Message; }
            text.AppendLine($"  {NvidiaSettings.Hex(s.Id)} {s.Name}: \"{name ?? "unknown id"}\"{(name is not null && NvidiaSettings.NameMatches(s, name) ? "" : " (NOT TRUSTED)")}, default {(standard is { } v ? $"{NvidiaSettings.Hex(v)} ({s.Describe(v)})" : "unknown")}");
        }
        void Profile(IntPtr profile, string label) {
            var (name, predefined) = session.ProfileInfo(profile);
            text.AppendLine($"{label}: {name}{(predefined ? " (NVIDIA predefined profile)" : "")}");
            foreach (var s in NvidiaSettings.Catalog) {
                var read = session.Read(profile, s.Id);
                text.AppendLine(read is { } r
                    ? $"  {NvidiaSettings.Hex(s.Id)} {s.Name}: driver name \"{r.DriverName}\", {(r.Type == 0 && NvidiaSettings.NameMatches(s, r.DriverName) ? "" : "NOT TRUSTED, ")}value {NvidiaSettings.Hex(r.Value)} ({s.Describe(r.Value)}), {NvidiaSettings.SourceText(r.Source)}"
                    : $"  {NvidiaSettings.Hex(s.Id)} {s.Name}: not set");
            }
        }
        Profile(session.GlobalProfile(), "Global profile");
        foreach (var exe in executables) {
            var profile = session.FindApplication(exe);
            if (profile == IntPtr.Zero) text.AppendLine($"{exe}: no NVIDIA profile"); else Profile(profile, exe);
        }
        return text.ToString();
    }

    /// <summary>The global settings, and the profile of each executable given (null where NVIDIA has none).</summary>
    internal static (NvidiaProfileView Global, IReadOnlyDictionary<string, NvidiaProfileView?> Applications) ReadProfiles(IEnumerable<string> executables)
    {
        using var session = new Session();
        var global = session.View(session.GlobalProfile(), null);
        var applications = new Dictionary<string, NvidiaProfileView?>(StringComparer.OrdinalIgnoreCase);
        foreach (var exe in executables.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase)) {
            var profile = session.FindApplication(exe);
            applications[exe] = profile == IntPtr.Zero ? null : session.View(profile, exe);
        }
        return (global, applications);
    }
}

internal sealed class NvidiaException(string message) : Exception(message);
