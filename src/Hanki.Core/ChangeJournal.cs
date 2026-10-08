using System.Text.Json;

namespace IgezziGuard;

public sealed record SettingChange(Guid Id, DateTimeOffset At, string Kind, string Target, string Before, string After, string Status);
public interface ISettingBackend
{
    Task<string> Read(string kind, string target, CancellationToken token);
    Task Write(string kind, string target, string value, CancellationToken token);
}
public sealed class ChangeJournal(ISettingBackend backend, string path)
{
    public const string NotApplied = "Not applied (unchanged)";
    private static bool Closed(SettingChange e) => e.Status is "Undone" or NotApplied;
    public List<SettingChange> Read() => File.Exists(path) ? JsonSerializer.Deserialize<List<SettingChange>>(File.ReadAllText(path)) ?? throw new IOException("Invalid recovery journal.") : [];
    private void Save(List<SettingChange> entries) {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, entries); stream.Flush(true); } File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private FileStream Gate() { Directory.CreateDirectory(Path.GetDirectoryName(path)!); return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    public async Task Apply(string kind, string target, string expected, string after, CancellationToken token)
    {
        using var gate = Gate(); var entries = Read();
        if (entries.Any(e => e.Kind == kind && e.Target == target && !Closed(e))) throw new IOException("Undo or reconcile the previous change to this setting first.");
        if (await backend.Read(kind, target, token) != expected) throw new IOException("Setting changed after preview. Refresh before applying.");
        if (expected == after) throw new IOException("Already at the requested setting.");
        var entry = new SettingChange(Guid.NewGuid(), DateTimeOffset.Now, kind, target, expected, after, "Pending / inspect");
        entries.Add(entry); Save(entries);
        if (await backend.Read(kind, target, token) != expected) throw new IOException("Setting changed after backup; nothing intentionally applied. Inspect Recovery.");
        try { await backend.Write(kind, target, after, token); }
        catch (Exception) {
            // A write refused before changing anything (for example, access denied) must not stay pending undo.
            // Only a verified unchanged state is closed; anything else keeps the pending backup for inspection.
            bool unchanged;
            try { unchanged = await backend.Read(kind, target, CancellationToken.None) == expected; } catch (Exception) { unchanged = false; }
            if (unchanged) { entries[^1] = entry with { Status = NotApplied }; Save(entries); }
            throw;
        }
        if (await backend.Read(kind, target, token) != after) throw new IOException("Change could not be verified. Backup retained in Recovery.");
        entries[^1] = entry with { Status = "Applied" }; Save(entries);
    }
    public async Task Undo(Guid id, CancellationToken token)
    {
        using var gate = Gate(); var entries = Read(); int i = entries.FindIndex(e => e.Id == id);
        if (i < 0 || Closed(entries[i])) throw new IOException(i >= 0 && entries[i].Status == NotApplied ? "This change was never applied; nothing to undo." : "No active change selected.");
        var entry = entries[i]; var current = await backend.Read(entry.Kind, entry.Target, token);
        if (current != entry.Before && current != entry.After) throw new IOException("Current state differs from both saved states. Undo refused to avoid overwriting an external change.");
        if (current != entry.Before) await backend.Write(entry.Kind, entry.Target, entry.Before, token);
        if (await backend.Read(entry.Kind, entry.Target, token) != entry.Before) throw new IOException("Restore could not be verified. Backup retained.");
        entries[i] = entry with { Status = "Undone" }; Save(entries);
    }
}
