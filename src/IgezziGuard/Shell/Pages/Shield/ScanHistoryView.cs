using System.Windows;
using System.Windows.Controls;

namespace IgezziGuard.Shell;

/// <summary>File scan history: the summary of every scan Hanki's own file scanner has run on this PC.</summary>
internal sealed class ScanHistoryView : Grid
{
    private readonly DataList<HistoryEntry> list;
    private readonly TextBlock message = UiKit.Text("", 14, UiKit.Res("TextMuted"), wrap: true);
    internal int RowCount { get; private set; }

    internal ScanHistoryView()
    {
        Margin = new Thickness(24, 0, 24, 20);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition());
        list = new([new("When", 170, e => e.FinishedAt.ToLocalTime().ToString("g")), new("Scanned", 360, e => e.Target), new("Files", 80, e => e.FilesScanned.ToString("N0")),
            new("Findings", 85, e => e.DetectionCount.ToString()), new("Skipped", 80, e => e.Skipped.ToString()), new("Errors", 70, e => e.Errors.ToString())], false, "File scan history");
        message.Margin = new Thickness(0, 0, 0, 10); SetRow(message, 0); Children.Add(message);
        SetRow(list.View, 1); Children.Add(list.View);
    }

    internal void Reload()
    {
        try {
            var entries = new HistoryStore().GetEntries();
            RowCount = entries.Count; list.SetItems(entries.OrderByDescending(e => e.FinishedAt).ToArray());
            message.Text = entries.Count == 0 ? "No scans recorded yet. Run a file or folder scan in the File scanner tab to create a summary." : $"{entries.Count} scan{(entries.Count == 1 ? "" : "s")} recorded on this PC.";
        } catch (IOException ex) { message.Text = ex.Message; }
    }
}
