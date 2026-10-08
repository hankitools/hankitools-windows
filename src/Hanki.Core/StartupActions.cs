using System.Text.Json;

namespace IgezziGuard;

public sealed record StartupValue(string Name, string Command, int Kind);
public sealed record StartupAction(Guid Id, DateTimeOffset At, StartupValue Original, string State);
public interface IStartupBackend
{
    StartupValue? Read(string name);
    void Remove(string name);
    void Restore(StartupValue value);
}

// Journal is persisted before touching the Run value. Pending entries remain recoverable after interruption.
public sealed class StartupActions(IStartupBackend backend, string path)
{
    public List<StartupAction> ReadHistory() => File.Exists(path)
        ? JsonSerializer.Deserialize<List<StartupAction>>(File.ReadAllText(path)) ?? throw new IOException("Invalid action history.") : [];
    private void Save(List<StartupAction> history)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(stream, history); stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Disable(StartupValue expected)
    {
        using var gate = Gate();
        var history = ReadHistory();
        if (expected.Kind is not (1 or 2)) throw new IOException("Only string Run values are supported.");
        if (backend.Read(expected.Name) != expected) throw new IOException("Entry changed. Refresh and review it again.");
        if (history.Any(a => a.Original.Name.Equals(expected.Name, StringComparison.OrdinalIgnoreCase) && a.State != "Restored"))
            throw new IOException("An unresolved action already exists for this entry. Resolve it in Action history first.");
        var action = new StartupAction(Guid.NewGuid(), DateTimeOffset.Now, expected, "Pending disable");
        history.Add(action); Save(history);
        // Recheck after journaling; refuse to delete a newer command.
        if (backend.Read(expected.Name) != expected) throw new IOException("Entry changed after backup. Nothing removed; review history.");
        backend.Remove(expected.Name);
        if (backend.Read(expected.Name) is not null) throw new IOException("Entry is present again; review history. Another app may have recreated it.");
        history[^1] = action with { State = "Disabled" }; Save(history);
    }
    public void Undo(Guid id)
    {
        using var gate = Gate();
        var history = ReadHistory(); var index = history.FindIndex(a => a.Id == id);
        if (index < 0) throw new IOException("Action no longer exists.");
        var action = history[index];
        if (action.State == "Restored") throw new IOException("Already restored.");
        if (action.Original.Kind is not (1 or 2)) throw new IOException("Unsupported backup type.");
        var current = backend.Read(action.Original.Name);
        if (current is not null && current != action.Original) throw new IOException("A different value now exists. Undo refused to avoid overwriting it.");
        // Idempotent if restoration succeeded but the final journal write was interrupted.
        if (current is null) backend.Restore(action.Original);
        if (backend.Read(action.Original.Name) != action.Original) throw new IOException("Restoration could not be verified. Backup retained.");
        history[index] = action with { State = "Restored" }; Save(history);
    }
    private FileStream Gate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}
