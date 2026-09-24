using System.Diagnostics;

namespace IgezziGuard;

public sealed record InventoryFile(string FullPath, long Bytes, DateTime LastWriteUtc, DateTime CreatedUtc)
{
    public string Name => Path.GetFileName(FullPath);
    public string Extension => Path.GetExtension(FullPath);
}
public sealed record InventoryProgress(int Files, long Bytes, int Skipped, int Errors);
public sealed record InventoryResult(List<InventoryFile> Files, long Bytes, int Skipped, int Errors, bool Limited);

public static class FileInventory
{
    public const int Limit = 100_000;
    public static InventoryResult Scan(string root, IProgress<InventoryProgress>? progress, CancellationToken token)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        CleanupPolicy.RejectReparseAncestors(root);
        var pending = new Stack<string>(); pending.Push(root);
        var files = new List<InventoryFile>();
        long bytes = 0; int skipped = 0, errors = 0;
        var timer = Stopwatch.StartNew();
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                // Stream entries instead of allocating every child in a large directory at once.
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var attr = File.GetAttributes(path);
                        if ((attr & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        if ((attr & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                        var info = new FileInfo(path);
                        files.Add(new InventoryFile(info.FullName, info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc));
                        bytes += info.Length;
                        if (files.Count >= Limit) return new(files, bytes, skipped, errors, true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors++; }
                    if (timer.ElapsedMilliseconds >= 200)
                    {
                        progress?.Report(new(files.Count, bytes, skipped, errors)); timer.Restart();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors++; }
        }
        token.ThrowIfCancellationRequested();
        return new(files, bytes, skipped, errors, false);
    }
}

public static class CleanupPolicy
{
    public static string? BlockReason(InventoryFile entry)
    {
        try
        {
            var path = Path.GetFullPath(entry.FullPath);
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.Length < 3 || path[1] != ':' || path[2] != '\\' || path[3..].Contains(':'))
                return "Only ordinary local drive paths can be recycled.";
            if (new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed)
                return "Network and removable drives are scan-only.";
            if (LocationBlock(path, ProtectedFolders.Current()) is { } protectedReason) return protectedReason;
            RejectReparseAncestors(path);
            var info = new FileInfo(path);
            if (!info.Exists) return "File no longer exists.";
            if ((info.Attributes & (FileAttributes.Directory | FileAttributes.System | FileAttributes.ReadOnly | FileAttributes.Offline)) != 0)
                return "Directories, system, read-only and offline files are protected.";
            if (info.Length != entry.Bytes || info.LastWriteTimeUtc != entry.LastWriteUtc || info.CreationTimeUtc != entry.CreatedUtc)
                return "File changed after scanning. Rescan before cleanup.";
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return ex.Message; }
    }

    /// <summary>The folders the policy protects, as Windows reports them on this PC.</summary>
    public sealed record ProtectedFolders(string Windows, string ProgramFiles, string ProgramFilesX86, string ProgramData, string LocalAppData, string RoamingAppData, string Profile, string App)
    {
        public static ProtectedFolders Current() => new(Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppContext.BaseDirectory);
    }
    // Windows, program, recovery and boot folders at the root of any drive, and system files at a drive's root.
    private static readonly HashSet<string> ProtectedRootFolders = new(StringComparer.OrdinalIgnoreCase) {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "Recovery", "System Volume Information", "$Recycle.Bin", "$WinREAgent",
        "$Windows.~BT", "$Windows.~WS", "$SysReset", "Boot", "EFI", "Config.Msi", "Windows.old", "PerfLogs", "MSOCache", "OneDriveTemp" };
    private static readonly HashSet<string> ProtectedRootFiles = new(StringComparer.OrdinalIgnoreCase) {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp", "bootmgr", "BOOTNXT", "BOOTSECT.BAK" };

    /// <summary>
    /// Why a local path must not be deleted, or null. Anything on a local fixed drive is allowed except Windows,
    /// program and system folders (on any drive), Microsoft Store app files, app data, other people's profiles,
    /// drive-root system files and Hanki's own folder. Works on "C:\..." strings, independent of the host OS.
    /// </summary>
    public static string? LocationBlock(string path, ProtectedFolders folders)
    {
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return "Only files inside a folder or drive can be deleted.";
        if (parts.Length == 2 && ProtectedRootFiles.Contains(parts[1])) return "Windows system files are protected.";
        if (ProtectedRootFolders.Contains(parts[1])) return "Windows, program and system folders are protected.";
        if (parts.Any(p => p.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase))) return "Microsoft Store app files are protected. Uninstall the app instead.";
        bool Under(string folder) => folder.Length > 0 && path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        if (new[] { folders.Windows, folders.ProgramFiles, folders.ProgramFilesX86, folders.ProgramData, folders.App }.Any(Under))
            return "Windows, program and system folders are protected.";
        string profile = folders.Profile.TrimEnd('\\');
        if (Under(folders.LocalAppData) || Under(folders.RoamingAppData) || Under(profile + "\\AppData"))
            return "App data folders are protected: apps keep their settings and caches there.";
        int split = profile.LastIndexOf('\\');
        if (split > 2 && Under(profile[..split]) && !Under(profile)) return "Other people's files and shared profile folders are protected.";
        return null;
    }

    public static bool IsChild(string path, string parent) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void RejectReparseAncestors(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links, junctions and cloud placeholders are scan/cleanup exclusions.");
            current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current));
        }
    }
}
