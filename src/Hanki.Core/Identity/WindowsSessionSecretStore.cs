using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace IgezziGuard;

/// <summary>Windows Credential Manager generic credential, scoped to the current Windows user.</summary>
internal sealed class WindowsSessionSecretStore : ISessionSecretStore
{
    private const string Target = "HankiTools/Identity/v1";
    public SessionCredential? Read() => WindowsCredentials.Read(Target) is { } value ? new(value) : null;
    public void Write(SessionCredential credential) => WindowsCredentials.Write(Target, "Hanki session", credential.Value);
    public void Clear() => WindowsCredentials.Delete(Target);
}

/// <summary>The Hanki Pro / Technician licence, in Windows Credential Manager for the current Windows user.</summary>
internal sealed class WindowsLicenseStore : ILicenseStore
{
    private const string Target = "HankiTools/License/v1";
    public StoredLicense? Read() => WindowsCredentials.Read(Target) is { } json ? System.Text.Json.JsonSerializer.Deserialize<StoredLicense>(json) : null;
    public void Write(StoredLicense license) => WindowsCredentials.Write(Target, "Hanki licence", System.Text.Json.JsonSerializer.Serialize(license));
    public void Clear() => WindowsCredentials.Delete(Target);
}

internal static class WindowsCredentials
{
    private const int NotFound = 1168;
    internal static string? Read(string target)
    {
        if (!CredRead(target, 1, 0, out var pointer)) {
            int error = Marshal.GetLastWin32Error(); if (error == NotFound) return null;
            throw new Win32Exception(error, "Could not read the Windows credential.");
        }
        byte[]? bytes = null;
        try { var value = Marshal.PtrToStructure<Credential>(pointer); if (value.BlobSize > 2560) throw new IOException("Credential too large.");
            bytes = new byte[checked((int)value.BlobSize)]; Marshal.Copy(value.Blob, bytes, 0, bytes.Length); return Encoding.UTF8.GetString(bytes);
        } finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); CredFree(pointer); }
    }
    internal static void Write(string target, string userName, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > 2560) { CryptographicOperations.ZeroMemory(bytes); throw new ArgumentException("Credential exceeds secure-storage limit."); }
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { var value = new Credential { Type = 1, TargetName = target, BlobSize = (uint)bytes.Length, Blob = pinned.AddrOfPinnedObject(), Persist = 2, UserName = userName };
            if (!CredWrite(ref value, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not save the Windows credential.");
        } finally { CryptographicOperations.ZeroMemory(bytes); pinned.Free(); }
    }
    internal static void Delete(string target) { if (!CredDelete(target, 1, 0) && Marshal.GetLastWin32Error() != NotFound) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not remove the Windows credential."); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential {
        public uint Flags, Type; public string? TargetName, Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize; public IntPtr Blob; public uint Persist, AttributeCount; public IntPtr Attributes; public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
}
