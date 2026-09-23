using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace IgezziGuard;

/// <summary>Windows Credential Manager generic credential, scoped to the current Windows user.</summary>
internal sealed class WindowsSessionSecretStore : ISessionSecretStore
{
    private const string Target = "HankiTools/Identity/v1";
    public SessionCredential? Read()
    {
        if (!CredRead(Target, 1, 0, out var pointer)) {
            int error = Marshal.GetLastWin32Error(); if (error == 1168) return null;
            throw new Win32Exception(error, "Could not read the Windows session credential.");
        }
        byte[]? bytes = null;
        try { var value = Marshal.PtrToStructure<Credential>(pointer); if (value.BlobSize > 2560) throw new IOException("Session credential too large.");
            bytes = new byte[checked((int)value.BlobSize)]; Marshal.Copy(value.Blob, bytes, 0, bytes.Length); return new(Encoding.UTF8.GetString(bytes));
        } finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); CredFree(pointer); }
    }
    public void Write(SessionCredential credential)
    {
        var bytes = Encoding.UTF8.GetBytes(credential.Value);
        if (bytes.Length > 2560) { CryptographicOperations.ZeroMemory(bytes); throw new ArgumentException("Credential exceeds secure-storage limit."); }
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { var value = new Credential { Type = 1, TargetName = Target, BlobSize = (uint)bytes.Length, Blob = pinned.AddrOfPinnedObject(), Persist = 2, UserName = "Hanki session" };
            if (!CredWrite(ref value, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not save the Windows session credential.");
        } finally { CryptographicOperations.ZeroMemory(bytes); pinned.Free(); }
    }
    public void Clear() { if (!CredDelete(Target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not remove the Windows session credential."); }
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
