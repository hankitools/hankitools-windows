using System.Security.Cryptography;

namespace IgezziGuard;

public sealed record DuplicateGroup(string Hash, List<InventoryFile> Files);
public sealed record DuplicateResult(List<DuplicateGroup> Groups, int Skipped, string Coverage);
public static class DuplicateFinder
{
    public static DuplicateResult Scan(string folder, CancellationToken token)
    {
        var scan = FileInventory.Scan(folder, null, token); var groups = new List<DuplicateGroup>(); int skipped = 0; long read = 0;
        foreach (var candidates in scan.Files.Where(f => f.Bytes > 0).GroupBy(f => f.Bytes).Where(g => g.Count() > 1)) {
            var hashes = new Dictionary<string, List<InventoryFile>>();
            foreach (var file in candidates) {
                token.ThrowIfCancellationRequested();
                if (file.Bytes > 2L * 1024 * 1024 * 1024 || read + file.Bytes > 20L * 1024 * 1024 * 1024) { skipped++; continue; }
                try { read += file.Bytes; var hash = Hash(file, token); if (!hashes.TryGetValue(hash, out var list)) hashes[hash] = list = []; list.Add(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
            }
            groups.AddRange(hashes.Where(p => p.Value.Count > 1).Select(p => new DuplicateGroup(p.Key, p.Value)));
        }
        return new(groups.OrderByDescending(g => g.Files[0].Bytes * (g.Files.Count - 1)).ToList(), skipped,
            $"{scan.Files.Count} inventoried, {scan.Errors} inventory errors, {scan.Skipped} excluded entries. Inventory capped: {scan.Limited}. {skipped} hash skips/errors. File limit 2 GiB; total hash-read budget 20 GiB. Zero-byte files excluded. Hard links may appear as duplicates; sizes are logical, not guaranteed reclaimable disk space.");
    }
    public static void CheckUnchanged(InventoryFile file) {
        CleanupPolicy.RejectReparseAncestors(file.FullPath); var info = new FileInfo(file.FullPath);
        if (!info.Exists || info.Length != file.Bytes || info.LastWriteTimeUtc != file.LastWriteUtc || info.CreationTimeUtc != file.CreatedUtc) throw new IOException("File changed; rescan.");
    }
    public static string Hash(InventoryFile file, CancellationToken token) {
        CheckUnchanged(file); using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[128 * 1024]; int n;
        while ((n = stream.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, n); }
        CheckUnchanged(file); return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static bool Equal(Stream a, Stream b, CancellationToken token) {
        if (a.Length != b.Length) return false; a.Position = b.Position = 0;
        var x = new byte[128 * 1024]; var y = new byte[x.Length];
        while (a.Position < a.Length) { token.ThrowIfCancellationRequested(); int n = (int)Math.Min(x.Length, a.Length - a.Position); a.ReadExactly(x.AsSpan(0, n)); b.ReadExactly(y.AsSpan(0, n)); if (!x.AsSpan(0, n).SequenceEqual(y.AsSpan(0, n))) return false; }
        return true;
    }
}
