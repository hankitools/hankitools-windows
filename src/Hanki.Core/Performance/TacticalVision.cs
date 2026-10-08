using System.Text.Json;
namespace IgezziGuard;

internal sealed record VibranceRange(int Current, int Minimum, int Maximum, int Default)
{
    internal void Validate()
    {
        if (Minimum >= Maximum || Current < Minimum || Current > Maximum || Default < Minimum || Default >= Maximum)
            throw new IOException("The NVIDIA driver reported an unsupported Digital Vibrance range.");
    }
    internal int Boost(int percent)
    {
        Validate();
        if (!TacticalVisionSession.ValidStrength(percent) || percent == 0) throw new IOException("Choose a Tactical Vision strength from 51 to 100%.");
        // 50% is the driver's neutral value, 100% its maximum. Never reduce an existing boost.
        return Math.Max(Current, Default + (int)Math.Round((Maximum - (double)Default) * (percent - 50) / 50));
    }
}

internal sealed record VibranceRestore(string Display, int Before, int Applied);

/// <summary>One foreground display at a time. Persist restoration before touching the driver.</summary>
internal sealed class TacticalVisionSession(string path, Func<string, VibranceRange> read, Action<string, int> write)
{
    private VibranceRestore? active;
    private int activeStrength;
    internal static bool ValidStrength(int percent) => percent == 0 || percent is >= 51 and <= 100;
    internal static GameEntry? Match(IEnumerable<GameEntry> games, string executable) => games.FirstOrDefault(g =>
        !g.Hidden && g.TacticalVision > 0 && ValidStrength(g.TacticalVision) &&
        string.Equals(g.Executable, executable, StringComparison.OrdinalIgnoreCase));

    internal void Recover()
    {
        if (!File.Exists(path)) return;
        active = JsonSerializer.Deserialize<VibranceRestore>(File.ReadAllText(path)) ?? throw new IOException("Tactical Vision recovery data is empty.");
        Restore();
    }

    internal void Apply(string display, int percent)
    {
        if (active?.Display == display && activeStrength == percent) return;
        Restore();
        var range = read(display);
        int target = range.Boost(percent);
        if (target == range.Current) return;
        var pending = new VibranceRestore(display, range.Current, target);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(pending));
        File.Move(path + ".tmp", path, true);
        active = pending;
        activeStrength = percent;
        write(display, target);
        if (read(display).Current != target) throw new IOException("The NVIDIA driver did not apply Tactical Vision.");
    }

    internal void Restore()
    {
        if (active is not { } saved) return;
        // A manual change made in NVIDIA Control Panel takes precedence.
        if (read(saved.Display).Current == saved.Applied) {
            write(saved.Display, saved.Before);
            if (read(saved.Display).Current != saved.Before) throw new IOException("NVIDIA did not restore the previous color setting. Retry by reopening Hanki.");
        }
        File.Delete(path);
        active = null;
    }
}
