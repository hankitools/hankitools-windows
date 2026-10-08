using System.Text.RegularExpressions;

namespace IgezziGuard;

public static class ByteSize
{
    public static string Text(long bytes)
    {
        var size = (double)bytes; string[] units = ["B", "KiB", "MiB", "GiB", "TiB"]; int i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return size.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + " " + units[i];
    }
}

public static class FileCategories
{
    public static readonly (string Name, string[] Extensions)[] All = [
        ("Videos", [".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".flv", ".3gp"]),
        ("Pictures", [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".heic", ".webp", ".tif", ".tiff", ".raw", ".cr2", ".nef", ".arw", ".dng", ".psd"]),
        ("Music & audio", [".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".wma", ".opus"]),
        ("Installers & archives", [".msi", ".exe", ".zip", ".7z", ".rar", ".iso", ".cab", ".msix", ".appx", ".tar", ".gz"]),
        ("Documents", [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".odt", ".rtf", ".csv", ".epub"]),
        ("Disk images & backups", [".vhd", ".vhdx", ".vmdk", ".bak", ".img", ".wim", ".esd"])
    ];
    public static string Of(string extension) => All.FirstOrDefault(c => c.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)).Name ?? "Other";
    /// <summary>Categories by total size, largest first.</summary>
    public static IReadOnlyList<(string Name, int Files, long Bytes)> Totals(IEnumerable<InventoryFile> files) =>
        files.GroupBy(f => Of(f.Extension)).Select(g => (g.Key, g.Count(), g.Sum(f => f.Bytes))).OrderByDescending(t => t.Item3).ToArray();
}

/// <summary>What a startup entry probably is, from its name and command. A hint to research, not a verdict.</summary>
public static class StartupAdvice
{
    private static readonly (string Kind, string Advice, string Pattern)[] Rules = [
        ("Security", "Keep enabled — it protects this PC.",
            @"securityhealth|windows ?defender|msmpeng|\bavast|\bavg\b|norton|mcafee|bitdefender|\beset\b|\bekrn|kaspersky|\bavp\.exe|malwarebytes|\bmbam|sophos|trend ?micro|webroot|f-secure"),
        ("Accessibility", "Keep enabled if you use it.", @"narrator|magnify|\bosk\.exe|atbroker"),
        ("Cloud sync", "Disabling pauses syncing until you open the app.", @"onedrive|dropbox|google ?drive|googledrivefs|icloud|\bbox\b.*sync|megasync|pcloud|nextcloud"),
        // Apps people open themselves come before updaters: "Discord\Update.exe" starts Discord.
        ("Convenience", "Safe to disable; open it yourself when needed.",
            @"teams|discord|slack|zoom|skype|spotify|steam|epicgames|epic games|battle\.net|\borigin\b|eadesktop|ubisoft|\bgog|whatsapp|telegram|signal|ccleaner|itunes|opera|chrome|msedge|brave|firefox|vivaldi|overwolf|curseforge"),
        ("Updater", "Usually safe to disable; it still updates when opened.", @"updat|googleupdate|edgeupdate|adobearm|jusched|softwareupdate"),
        ("Device helper", "Usually keep; may run audio, touchpad or keys.",
            @"realtek|rtkaud|nahimic|synaptics|\bsyntp|\belan\b|etdctrl|igfx|intel|nvidia|nvcontainer|\bamd\b|radeon|logitech|lghub|corsair|icue|razer|steelseries|\basus|armoury|\bmsi\b|\bdell\b|\bhp\b|lenovo|wacom|bluetooth|dolby|waves"),
    ];
    public static (string Kind, string Advice) Describe(string name, string command)
    {
        var text = name + " " + command;
        foreach (var rule in Rules)
            if (Regex.IsMatch(text, rule.Pattern, RegexOptions.IgnoreCase)) return (rule.Kind, rule.Advice);
        return ("Unknown", "Look up the name before disabling it.");
    }
}

public static class DuplicateInsights
{
    public static Diagnosis Summarize(DuplicateResult result, string report)
    {
        var groups = result.Groups;
        long reclaim = groups.Sum(g => (long)(g.Files.Count - 1) * g.Files[0].Bytes);
        var cards = new List<ResultCard>();
        if (groups.Count == 0) {
            cards.Add(new("What was found", "No files with identical contents were found in this folder.", CardStatus.Good));
            cards.Add(new("Coverage", result.Coverage));
            return Diagnosis.From(report, cards, "No duplicate files found");
        }
        cards.Add(new("What was found", $"{groups.Count} set(s) of identical files, {groups.Sum(g => g.Files.Count)} files in total.\nKeeping one copy of each would free {ByteSize.Text(reclaim)}.",
            reclaim >= 1L << 30 ? CardStatus.Review : CardStatus.Info));
        cards.Add(new("Biggest duplicates", string.Join("\n", groups.OrderByDescending(g => (long)(g.Files.Count - 1) * g.Files[0].Bytes).Take(6)
            .Select(g => $"{Path.GetFileName(g.Files[0].FullPath)} — {g.Files.Count} copies × {ByteSize.Text(g.Files[0].Bytes)}"))));
        cards.Add(new("What to do next", "Pick a set and a copy in the lists above, then Review / recycle one copy. Hanki re-checks the contents first and always keeps another copy. Recycled files can be restored from the Recycle Bin. Be careful with files inside program or cloud-sync folders."));
        cards.Add(new("Coverage", result.Coverage));
        return Diagnosis.From(report, cards, $"{ByteSize.Text(reclaim)} could be freed by removing duplicate copies");
    }
}
