using System.Text.Json;
namespace IgezziGuard;

/// <summary>A file deletion, an uninstall or a removed leftover app entry, for System actions.</summary>
public sealed record RemovalRecord(DateTimeOffset At, string Title, string Detail);

/// <summary>Removals Hanki carried out, kept on this PC (newest 500).</summary>
public sealed class RemovalLog(string path)
{
    public static RemovalLog Default => new(Path.Combine(SecurityPaths.Root, "removals.json"));
    public IReadOnlyList<RemovalRecord> Read() => File.Exists(path) ? JsonSerializer.Deserialize<List<RemovalRecord>>(File.ReadAllText(path)) ?? [] : [];
    public void Add(RemovalRecord record)
    {
        var records = Read().Append(record).OrderByDescending(r => r.At).Take(500).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(records));
        File.Move(temp, path, overwrite: true);
    }
    /// <summary>Records a removal; a log that can't be written doesn't undo or block the removal.</summary>
    public static void TryAdd(RemovalRecord record)
    {
        try { Default.Add(record); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }
}
