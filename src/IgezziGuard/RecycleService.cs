using System.Runtime.InteropServices;

namespace IgezziGuard;

// Uses the Windows 8+ recycle-only flag, not legacy SHFileOperation/ALLOWUNDO.
// No File.Delete fallback. A per-item callback vetoes non-recycling transfers.
internal static class RecycleService
{
    public static void Recycle(InventoryFile entry, IntPtr owner)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Recycle operations require an STA thread.");
        var reason = CleanupPolicy.BlockReason(entry);
        if (reason is not null) throw new IOException(reason);
        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!)!;
        IShellItem? shellItem = null;
        try
        {
            var iid = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(entry.FullPath, IntPtr.Zero, ref iid, out shellItem));
            var sink = new RecycleGuard(entry);
            operation.SetOwnerWindow(owner);
            // RECYCLEONDELETE | EARLYFAILURE | NOERRORUI | NO_CONNECTED_ELEMENTS |
            // ADDUNDORECORD. No YES-TO-ALL, no elevation request, no silent permanent delete.
            operation.SetOperationFlags(0x00080000 | 0x00100000 | 0x0400 | 0x2000 | 0x20000000);
            operation.DeleteItem(shellItem, sink);
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            GC.KeepAlive(sink);
            if (aborted || !sink.Recycled)
                throw new IOException(sink.Failure ?? "Recycling was cancelled or could not be confirmed. Inspect the file and Recycle Bin; no fallback was attempted.");
        }
        finally
        {
            if (shellItem is not null) Marshal.ReleaseComObject(shellItem);
            Marshal.ReleaseComObject(operation);
        }
    }

    /// <summary>
    /// Deletes a file for good, after the same checks as recycling (protected places, links, unchanged since the
    /// scan). Only after the person chose "Delete permanently" and confirmed that it can't be undone.
    /// </summary>
    public static void DeletePermanently(InventoryFile entry)
    {
        var reason = CleanupPolicy.BlockReason(entry);
        if (reason is not null) throw new IOException(reason);
        File.Delete(entry.FullPath);
        if (File.Exists(entry.FullPath)) throw new IOException("Windows kept the file. It may be in use.");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IShellItem item);
}

[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem
{
    void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
    void GetParent(out IShellItem parent);
    void GetDisplayName(uint type, out IntPtr name);
    void GetAttributes(uint mask, out uint attributes);
    void Compare(IShellItem other, uint hint, out int order);
}

[ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperation
{
    void Advise(IFileOperationProgressSink sink, out uint cookie);
    void Unadvise(uint cookie);
    void SetOperationFlags(uint flags);
    void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
    void SetProgressDialog(IntPtr dialog);
    void SetProperties(IntPtr properties);
    void SetOwnerWindow(IntPtr owner);
    void ApplyPropertiesToItem(IShellItem item);
    void ApplyPropertiesToItems(IntPtr items);
    void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
    void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
    void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
    void MoveItems(IntPtr items, IShellItem destination);
    void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
    void CopyItems(IntPtr items, IShellItem destination);
    void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
    void DeleteItems(IntPtr items);
    void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IFileOperationProgressSink sink);
    void PerformOperations();
    void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}

[ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFileOperationProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int result);
    [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
    [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
    [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
    [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
    [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created);
    [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IShellItem? created);
    [PreserveSig] int UpdateProgress(uint total, uint completed);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class RecycleGuard : IFileOperationProgressSink
{
    private const int Abort = unchecked((int)0x80004004);
    private readonly InventoryFile entry;
    public bool Recycled { get; private set; }
    public string? Failure { get; private set; }
    public RecycleGuard(InventoryFile entry) => this.entry = entry;
    public int PreDeleteItem(uint flags, IShellItem item)
    {
        try
        {
            // Abort if the Shell proposes a non-recycling transfer.
            if ((flags & 0x80) == 0) { Failure = "Windows cannot recycle this file. Operation blocked."; return Abort; }
            item.GetDisplayName(0x80058000, out var pointer); // SIGDN_FILESYSPATH
            string? path;
            try { path = Marshal.PtrToStringUni(pointer); } finally { Marshal.FreeCoTaskMem(pointer); }
            if (!string.Equals(path, entry.FullPath, StringComparison.OrdinalIgnoreCase))
            { Failure = "Unexpected target. Operation blocked."; return Abort; }
            Failure = CleanupPolicy.BlockReason(entry);
            return Failure is null ? 0 : Abort;
        }
        catch (Exception ex) { Failure = ex.Message; return Abort; }
    }
    public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created)
    {
        Recycled = result >= 0 && created is not null;
        if (!Recycled) Failure = $"Recycling not confirmed (0x{result:X8}). Check the file and Recycle Bin.";
        return 0;
    }
    public int StartOperations() => 0;
    public int FinishOperations(int result) => 0;
    public int PreRenameItem(uint f, IShellItem i, string n) => Abort;
    public int PostRenameItem(uint f, IShellItem i, string n, int r, IShellItem? c) => 0;
    public int PreMoveItem(uint f, IShellItem i, IShellItem d, string n) => Abort;
    public int PostMoveItem(uint f, IShellItem i, IShellItem d, string n, int r, IShellItem? c) => 0;
    public int PreCopyItem(uint f, IShellItem i, IShellItem d, string n) => Abort;
    public int PostCopyItem(uint f, IShellItem i, IShellItem d, string n, int r, IShellItem? c) => 0;
    public int PreNewItem(uint f, IShellItem d, string n) => Abort;
    public int PostNewItem(uint f, IShellItem d, string n, string t, uint a, int r, IShellItem? c) => 0;
    public int UpdateProgress(uint t, uint c) => 0;
    public int ResetTimer() => 0;
    public int PauseTimer() => 0;
    public int ResumeTimer() => 0;
}
