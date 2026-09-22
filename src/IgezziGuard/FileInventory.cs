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
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            // Deliberately narrow first release: no arbitrary system/application cleanup.
            var allowed = new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Path.Combine(profile, "Downloads")
            };
            if (!allowed.Any(p => !string.IsNullOrWhiteSpace(p) && IsChild(path, p)))
                return "Cleanup is limited to personal Desktop, Documents, Downloads, Pictures, Music and Videos folders.";
            var blocked = new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppContext.BaseDirectory };
            if (blocked.Any(p => !string.IsNullOrWhiteSpace(p) && IsChild(path, p))) return "System/application folders are protected.";
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
