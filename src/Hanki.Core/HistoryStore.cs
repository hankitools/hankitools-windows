using System.Text.Json;

namespace IgezziGuard;

public sealed class HistoryStore
{
    private const int MaximumEntries = 100;
    private static readonly object Gate = new();
    private readonly string path;
    public HistoryStore(string? path = null) => this.path = path ?? SecurityPaths.History;
    public IReadOnlyList<HistoryEntry> GetEntries()
    {
        lock (Gate) {
            if (!File.Exists(path)) return [];
            try {
                if (new FileInfo(path).Length > 2_000_000) throw new IOException("Scan history exceeds its size limit.");
                var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path));
                if (entries is null || entries.Count > MaximumEntries || entries.Any(e => e is null || string.IsNullOrWhiteSpace(e.Target) || e.FinishedAt < e.StartedAt || e.FilesScanned < 0 || e.BytesScanned < 0 || e.DetectionCount < 0 || e.Skipped < 0 || e.Errors < 0))
                    throw new IOException("Invalid scan-history records.");
                return entries.OrderByDescending(e => e.FinishedAt).ToArray();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
                throw new IOException("Scan history could not be read. The existing file has been preserved; no history was reset. " + ex.Message, ex);
            }
        }
    }
    public void Add(ScanSummary summary)
    {
        lock (Gate) {
            var entries = GetEntries().ToList();
            entries.Insert(0, new HistoryEntry(summary.Target, summary.StartedAt, summary.FinishedAt, summary.FilesScanned,
                summary.BytesScanned, summary.Findings.Count, summary.Skipped, summary.Errors));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    JsonSerializer.Serialize(stream, entries.Take(MaximumEntries)); stream.Flush(true);
                }
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
