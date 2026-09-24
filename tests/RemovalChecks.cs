using IgezziGuard;

// HANKI-MAINT-120: deleting files and uninstalling apps, with the places and entries Hanki protects.
internal static class RemovalChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    internal static void Run() { Locations(); Commands(); Leftovers(); History(); }

    private static void Locations()
    {
        var folders = new CleanupPolicy.ProtectedFolders(@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData",
            @"C:\Users\Aino\AppData\Local", @"C:\Users\Aino\AppData\Roaming", @"C:\Users\Aino", @"D:\Tools\Hanki\");
        string? Block(string path) => CleanupPolicy.LocationBlock(path, folders);
        Check(new[] { @"C:\Users\Aino\Downloads\big.iso", @"C:\Users\Aino\Videos\trip.mp4", @"D:\Backups\old.zip", @"E:\Games\recordings\clip.mkv", @"C:\Data\archive.7z", @"C:\old.iso" }.All(p => Block(p) is null),
            "delete: personal folders and other data locations on local drives are allowed");
        Check(new[] { @"C:\Windows\System32\drivers\x.sys", @"C:\Program Files\App\app.exe", @"D:\Program Files\Game\game.exe", @"C:\ProgramData\x.dat", @"C:\pagefile.sys",
            @"D:\hiberfil.sys", @"C:\System Volume Information\x", @"C:\$Recycle.Bin\S-1\x", @"C:\Windows.old\x", @"E:\WindowsApps\pkg\a.dll", @"D:\Tools\Hanki\HankiTools.exe" }.All(p => Block(p) is not null),
            "delete: Windows, program, Store app and system files are protected on every drive, and so is Hanki itself");
        Check(Block(@"C:\Users\Aino\AppData\Local\Temp\x.tmp") is { } appData && appData.Contains("App data") && Block(@"C:\Users\Aino\AppData\LocalLow\x") is not null
            && Block(@"C:\Users\Veikko\Documents\x.docx") is { } other && other.Contains("Other people") && Block(@"C:\Users\Public\x") is not null,
            "delete: app data and other people's profiles are protected");
    }

    private static void Commands()
    {
        Check(AppRemoval.ParseCommand("\"C:\\Program Files\\7-Zip\\Uninstall.exe\"") == (@"C:\Program Files\7-Zip\Uninstall.exe", "")
            && AppRemoval.ParseCommand("\"C:\\Apps\\uninst.exe\" /S --keep") == (@"C:\Apps\uninst.exe", "/S --keep")
            && AppRemoval.ParseCommand(@"C:\Program Files\Some App\unins000.exe /SILENT") == (@"C:\Program Files\Some App\unins000.exe", "/SILENT")
            && AppRemoval.ParseCommand("MsiExec.exe /I{23170F69-40C1-2702-2301-000001000000}") == ("MsiExec.exe", "/I{23170F69-40C1-2702-2301-000001000000}")
            && AppRemoval.ParseCommand("  ") is null && AppRemoval.ParseCommand("\"") is null,
            "uninstall: registered commands split into the program and its arguments, quoted or not");
        InstalledApp App(string key = "Example", string? uninstall = "\"C:\\Apps\\uninst.exe\"", bool msi = false, bool noRemove = false) =>
            new("Example", "Publisher", "1.0", null, null, "LocalMachine / Registry64", "LocalMachine", "Registry64", key, uninstall, @"C:\Apps", msi, noRemove);
        const string Product = "{23170F69-40C1-2702-2301-000001000000}";
        Check(AppRemoval.UninstallCommand(App(Product, "MsiExec.exe /I" + Product, msi: true), out _) == ("msiexec.exe", "/x " + Product)
            && AppRemoval.UninstallCommand(App(), out _) == (@"C:\Apps\uninst.exe", "")
            && AppRemoval.UninstallCommand(App(noRemove: true), out var locked) is null && locked!.Contains("not removable")
            && AppRemoval.UninstallCommand(App(uninstall: null), out var none) is null && none!.Contains("Installed apps"),
            "uninstall: Windows Installer products use msiexec /x; apps without an uninstaller or marked not removable are sent to Windows Settings");
    }

    private static void Leftovers()
    {
        Check(AppRemoval.IsLeftover(true, -1, null, false, null, false) && !AppRemoval.IsLeftover(true, 5, @"C:\gone.exe", false, null, false),
            "leftover: Windows Installer products follow what Windows reports");
        Check(AppRemoval.IsLeftover(false, null, @"C:\Apps\uninst.exe", false, @"C:\Apps", false) && AppRemoval.IsLeftover(false, null, @"C:\Apps\uninst.exe", false, null, false)
            && !AppRemoval.IsLeftover(false, null, @"C:\Apps\uninst.exe", false, @"C:\Apps", true) && !AppRemoval.IsLeftover(false, null, @"C:\Apps\uninst.exe", true, @"C:\Apps", false)
            && !AppRemoval.IsLeftover(false, null, null, false, null, false),
            "leftover: only when the uninstaller and the install folder are both gone; unknown means installed");
        InstalledApp Entry(string hive, string view, string key) => new("Old App", "P", "1", null, null, "", hive, view, key);
        Check(AppRemoval.RegistryKey(Entry("LocalMachine", "Registry32", "OldApp")) == (@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OldApp", "/reg:32")
            && AppRemoval.RegistryKey(Entry("CurrentUser", "Registry64", "{GUID}"))?.Key.StartsWith(@"HKCU\", StringComparison.Ordinal) == true
            && new[] { @"..\Run", "a\"b", "", " spaced", "a/b" }.All(k => AppRemoval.RegistryKey(Entry("LocalMachine", "Registry64", k)) is null)
            && AppRemoval.RegistryKey(Entry("Users", "Registry64", "OldApp")) is null,
            "leftover: only a plain Uninstall entry under HKLM or HKCU can be removed");
        Check(AppRemoval.BackupName(Entry("LocalMachine", "Registry64", "x") with { Name = "Old: App / v2" }, new DateTime(2026, 9, 24, 14, 5, 0)) == "app-entry-Old--App---v2-20260924-140500.reg",
            "leftover: backups get a safe file name");
    }

    private static void History()
    {
        var at = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var timeline = SystemActions.Timeline([], [], [], [new(at, "App uninstalled", "Old App 1.0 (P)"), new(at.AddHours(1), "Files deleted permanently", "2 files, 3 GB")]);
        Check(timeline.Count == 2 && timeline[0] is { Kind: "Removal", Title: "Files deleted permanently" } && timeline[1].Detail == "Old App 1.0 (P)",
            "history: deletions and uninstalls appear in System actions, newest first");
        var path = Path.Combine(Path.GetTempPath(), "hanki-removals-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            var log = new RemovalLog(path);
            log.Add(new(at, "A", "1")); log.Add(new(at.AddMinutes(1), "B", "2"));
            Check(log.Read().Select(r => r.Title).SequenceEqual(["B", "A"]), "history: the removal log keeps the newest first");
        } finally { File.Delete(path); }
    }
}
