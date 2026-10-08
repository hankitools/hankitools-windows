using IgezziGuard;
using System.Text.Json;

internal static class TacticalVisionChecks
{
    internal static void Run()
    {
        void Check(bool ok, string text) => DiagnosticChecks.Check(ok, "Tactical Vision: " + text);
        var folder = Path.Combine(Path.GetTempPath(), "hanki-vision-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "restore.json");
        int level = 55, writes = 0;
        var session = new TacticalVisionSession(path, _ => new(level, 0, 100, 50), (_, value) => {
            Check(File.Exists(path), "recovery is persisted before any driver write"); level = value; writes++;
        });
        try {
            session.Apply("display1", 70);
            Check(level == 70, "boost applies to driver range");
            session.Apply("display1", 70);
            Check(writes == 1, "stable foreground does not rewrite settings");
            session.Apply("display1", 80);
            Check(level == 80, "strength change restores baseline before applying");
            session.Restore();
            Check(level == 55 && !File.Exists(path), "focus loss restores exact original value");
            session.Apply("display1", 70);
            level = 65;
            session.Restore();
            Check(level == 65, "external user changes are preserved");
            session.Apply("display1", 80);
            new TacticalVisionSession(path, _ => new(level, 0, 100, 50), (_, value) => level = value).Recover();
            Check(level == 65 && !File.Exists(path), "next launch recovers interrupted session");
            session.Restore();
            var fail = new TacticalVisionSession(path, _ => new(level, 0, 100, 50), (_, _) => throw new IOException("driver failed"));
            try { fail.Apply("display1", 80); Check(false, "failed write must be reported"); } catch (IOException) { }
            Check(File.Exists(path), "failed driver write retains recovery record");
            fail.Restore();
            Check(!File.Exists(path) && level == 65, "unchanged driver needs no restoration write");
            bool rejectRestore = false;
            var retry = new TacticalVisionSession(path, _ => new(level, 0, 100, 50), (_, value) => {
                if (rejectRestore) throw new IOException("display disconnected"); level = value;
            });
            retry.Apply("display1", 80);
            rejectRestore = true;
            try { retry.Restore(); Check(false, "restore failure must be reported"); } catch (IOException) { }
            Check(level == 80 && File.Exists(path), "failed restore retains original value for retry");
            rejectRestore = false;
            retry.Restore();
            Check(level == 65 && !File.Exists(path), "reconnected display can be restored");
            var unavailableBackup = new TacticalVisionSession(Path.Combine(folder, "missing", "restore.json"), _ => new(level, 0, 100, 50), (_, _) => Check(false, "must not write without a backup"));
            try { unavailableBackup.Apply("display1", 80); Check(false, "backup failure must stop application"); } catch (IOException) { }
            int first = 50, second = 60;
            var monitors = new TacticalVisionSession(path, name => new(name == "first" ? first : second, 0, 100, 50), (name, value) => { if (name == "first") first = value; else second = value; });
            monitors.Apply("first", 70);
            monitors.Apply("second", 80);
            Check(first == 50 && second == 80, "moving displays restores the previous monitor");
            monitors.Restore();
            Check(second == 60, "each monitor keeps its own original value");
            Check(new VibranceRange(0, -100, 100, 0).Boost(70) == 40, "signed driver ranges supported");
            Check(new VibranceRange(90, 0, 100, 50).Boost(70) == 90, "existing stronger saturation preserved");
            Check(!TacticalVisionSession.ValidStrength(101) && !TacticalVisionSession.ValidStrength(50), "invalid strengths rejected");
            var old = JsonSerializer.Deserialize<GameEntry>("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Name\":\"Game\",\"Executable\":\"C:\\\\Games\\\\game.exe\",\"Source\":\"test\",\"Goal\":0}")!;
            Check(old.TacticalVision == 0, "existing libraries default to off");
            var game = old with { TacticalVision = 70 };
            Check(TacticalVisionSession.Match([game], game.Executable.ToUpperInvariant()) == game, "matches full executable path case insensitively");
            Check(TacticalVisionSession.Match([game], @"C:\Other\game.exe") is null && TacticalVisionSession.Match([game with { Hidden = true }], game.Executable) is null, "unrelated executables and removed games never activate");
            var library = Path.Combine(folder, "games.json");
            GameLibrary.Write(library, [game]);
            Check(GameLibrary.Read(library).Single().TacticalVision == 70, "per-game preference survives save and reload");
        } finally { Directory.Delete(folder, true); }
    }
}
