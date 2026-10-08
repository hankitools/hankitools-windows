using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;

namespace IgezziGuard.Shell;

/// <summary>Files &amp; storage: pick a folder or drive, see what uses space by type, filter, and send files to the Recycle Bin or delete them after a review.</summary>
internal sealed class FilesView : Grid
{
    private readonly IShellServices shell;
    private readonly Button browse = Buttons.Primary("Choose folder / drive"), cancel = Buttons.Secondary("Cancel"), largest = Buttons.Secondary("Largest 100"), showAll = Buttons.Secondary("All files"), apply = Buttons.Secondary("Apply filters");
    private readonly Button reveal = Buttons.Quiet("Open location"), delete = Buttons.Secondary("Delete…");
    private readonly TextBox query = Buttons.Field("Filename contains…", 180), extension = Buttons.Field(".zip / .mp4", 110), minimum = Buttons.Field("Min MiB", 100);
    private readonly List<RadioButton> presets = [];
    private readonly WrapPanel categories = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 4) };
    private readonly StackPanel selectionBar = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Hidden, Margin = new Thickness(0, 0, 0, 8), MinHeight = 44 };
    private readonly TextBlock selectionText = UiKit.Text("", 14, UiKit.Res("TextMuted")), summary = UiKit.Text("", 13, UiKit.Res("TextMuted"), wrap: true);
    private readonly DataList<InventoryFile> list;
    private List<InventoryFile> inventory = [];
    private InventoryFile[] visible = [];
    private CancellationTokenSource? running;
    private int sortColumn = 1, preset;
    private bool descending = true, topOnly;
    private string? category;
    private string scanSummary = "Choose a folder or drive to see what's using space. Nothing is selected or deleted automatically.";

    internal bool IsBusy => running is not null;
    internal int VisibleCount => visible.Length;

    internal FilesView(IShellServices shell)
    {
        this.shell = shell;
        for (int i = 0; i < 5; i++) RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Margin = new Thickness(24, 0, 24, 20);
        list = new DataList<InventoryFile>([
            new("Name", 230, f => f.Name), new("Size", 100, f => ByteSize.Text(f.Bytes)), new("Type", 70, f => f.Extension),
            new("Modified", 150, f => f.LastWriteUtc.ToLocalTime().ToString("g")), new("Full path", 560, f => f.FullPath)], multiSelect: true, "Files");
        list.ShowSort(sortColumn, descending);

        var bar = new WrapPanel(); bar.Children.Add(browse); bar.Children.Add(cancel); bar.Children.Add(largest); bar.Children.Add(showAll);
        var filters = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        string[] names = ["All file types / ages", "Not modified in 90+ days", "Installers / archives", "Large files (250+ MiB)"];
        for (int i = 0; i < names.Length; i++) {
            int index = i;
            var chip = new RadioButton { Style = (Style)Application.Current.FindResource("SubTab"), GroupName = "filespreset", Content = names[i], IsChecked = i == 0, FontSize = 13 };
            chip.Click += (_, _) => { preset = index; RefreshView(); }; presets.Add(chip); filters.Children.Add(chip);
        }
        filters.Children.Add(query); filters.Children.Add(extension); filters.Children.Add(minimum); filters.Children.Add(apply);
        var top = new StackPanel(); top.Children.Add(bar); top.Children.Add(filters); top.Children.Add(categories);
        SetRow(top, 0); Children.Add(top);

        selectionBar.Children.Add(selectionText); selectionText.Margin = new Thickness(0, 0, 14, 0); selectionText.VerticalAlignment = VerticalAlignment.Center;
        reveal.Margin = new Thickness(0, 0, 8, 0); delete.Margin = new Thickness(0);
        selectionBar.Children.Add(reveal); selectionBar.Children.Add(delete);
        SetRow(selectionBar, 1); Children.Add(selectionBar);

        list.View.Margin = new Thickness(0, 0, 0, 10); SetRow(list.View, 3); Children.Add(list.View);
        summary.Margin = new Thickness(2, 0, 0, 0); SetRow(summary, 4); Children.Add(summary);

        browse.Click += async (_, _) => await Scan();
        cancel.Click += (_, _) => running?.Cancel();
        apply.Click += (_, _) => RefreshView();
        largest.Click += (_, _) => { sortColumn = 1; descending = true; topOnly = true; RefreshView(); };
        showAll.Click += (_, _) => { topOnly = false; RefreshView(); };
        reveal.Click += (_, _) => Reveal();
        delete.Click += async (_, _) => await Cleanup();
        list.SelectionChanged += UpdateSummary;
        list.Activated += _ => Reveal();
        list.HeaderClicked += column => { descending = sortColumn == column ? !descending : column == 1; sortColumn = column; RefreshView(); };
        list.View.KeyDown += async (_, e) => { if (e.Key == Key.Delete && list.Selected.Count > 0) { e.Handled = true; await Cleanup(); } };
        SetBusy(false); UpdateSummary();
    }

    private async Task Scan()
    {
        if (IsBusy) return;
        var chooser = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder or drive to inspect" };
        if (chooser.ShowDialog() != true) return;
        var path = chooser.FolderName;
        using var cts = new CancellationTokenSource(); running = cts; SetBusy(true);
        using var ticket = shell.Tasks.Begin("Files", cts.Cancel);
        inventory.Clear(); visible = []; list.SetItems(visible);
        summary.Text = "Scanning " + path;
        try {
            var progress = new Progress<InventoryProgress>(p => { if (running == cts) summary.Text = $"Scanning: {p.Files:N0} files · {ByteSize.Text(p.Bytes)} logical size · {p.Errors} errors · {p.Skipped} skipped"; });
            var result = await Task.Run(() => FileInventory.Scan(path, progress, cts.Token), cts.Token);
            inventory = result.Files;
            scanSummary = $"{path}: {inventory.Count:N0} files · {ByteSize.Text(result.Bytes)} in total · {result.Errors} unreadable · {result.Skipped} skipped" +
                (result.Limited ? " · PARTIAL: stopped at 100,000 files; choose a smaller folder for a complete picture." : "");
            sortColumn = 1; descending = true; category = null;
            ShowCategories(); RefreshView();
        }
        catch (OperationCanceledException) { scanSummary = "Scan cancelled; incomplete inventory discarded. Choose a folder to scan again."; }
        catch (Exception ex) { scanSummary = "Scan failed: " + ex.Message; }
        finally { running = null; SetBusy(false); UpdateSummary(); }
    }

    /// <summary>Filters, sorts and shows the inventory (also used by the UI check with a prepared inventory).</summary>
    internal void RefreshView()
    {
        var text = query.Text.Trim();
        var ext = extension.Text.Trim();
        if (ext.Length > 0 && !ext.StartsWith('.')) ext = "." + ext;
        long minBytes = double.TryParse(minimum.Text, out var mib) ? (long)(Math.Max(0, mib) * 1024 * 1024) : 0;
        IEnumerable<InventoryFile> matches = inventory.Where(f => f.Bytes >= minBytes && f.Name.Contains(text, StringComparison.OrdinalIgnoreCase) && (ext.Length == 0 || f.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase)));
        if (category is not null) matches = matches.Where(f => FileCategories.Of(f.Extension) == category);
        matches = preset switch {
            1 => matches.Where(f => f.LastWriteUtc < DateTime.UtcNow.AddDays(-90)),
            2 => matches.Where(f => new[] { ".msi", ".exe", ".zip", ".7z", ".rar", ".iso" }.Contains(f.Extension, StringComparer.OrdinalIgnoreCase)),
            3 => matches.Where(f => f.Bytes >= 250L * 1024 * 1024),
            _ => matches
        };
        matches = sortColumn switch {
            1 => descending ? matches.OrderByDescending(f => f.Bytes) : matches.OrderBy(f => f.Bytes),
            3 => descending ? matches.OrderByDescending(f => f.LastWriteUtc) : matches.OrderBy(f => f.LastWriteUtc),
            _ => descending ? matches.OrderByDescending(TextKey, StringComparer.OrdinalIgnoreCase) : matches.OrderBy(TextKey, StringComparer.OrdinalIgnoreCase)
        };
        visible = (topOnly ? matches.Take(100) : matches).ToArray();
        list.SetItems(visible); list.ShowSort(sortColumn, descending);
        UpdateSummary();
    }
    private string TextKey(InventoryFile f) => sortColumn switch { 2 => f.Extension, 4 => f.FullPath, _ => f.Name };

    /// <summary>For the UI check: loads a prepared inventory as if a scan had finished.</summary>
    internal void Load(IEnumerable<InventoryFile> files, string label)
    {
        inventory = files.ToList(); sortColumn = 1; descending = true; category = null; scanSummary = label; ShowCategories(); RefreshView();
    }

    private void ShowCategories()
    {
        categories.Children.Clear();
        var totals = FileCategories.Totals(inventory);
        categories.Visibility = totals.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (totals.Count == 0) return;
        var label = UiKit.Text("Space by type:", 13, UiKit.Res("TextMuted")); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 10, 0); categories.Children.Add(label);
        foreach (var (name, files, bytes) in totals.Take(7)) {
            bool on = category == name;
            var chip = new Button { Style = (Style)Application.Current.FindResource(on ? "PrimaryButton" : "SecondaryButton"), Content = $"{name}  {ByteSize.Text(bytes)}", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 3, 6, 3), FontSize = 13 };
            System.Windows.Automation.AutomationProperties.SetName(chip, $"Show only {name}: {files:N0} files, {ByteSize.Text(bytes)}");
            chip.Click += (_, _) => { category = category == name ? null : name; ShowCategories(); RefreshView(); };
            categories.Children.Add(chip);
        }
    }

    private void UpdateSummary()
    {
        if (IsBusy) return;
        var selected = list.Selected;
        summary.Text = scanSummary + $"\nShowing {visible.Length:N0}" + (category is null ? "" : $" {category.ToLowerInvariant()}") + $" · selected {selected.Count:N0} ({ByteSize.Text(selected.Sum(f => f.Bytes))}). " +
            "Filters help you review; they don't prove a file is unneeded. Select files (Ctrl or Shift for several), then Delete… to send them to the Recycle Bin or delete them permanently.";
        // Hidden, not collapsed: the list must not jump down when a selection appears.
        selectionBar.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Hidden;
        selectionText.Text = $"{selected.Count:N0} selected · {ByteSize.Text(selected.Sum(f => f.Bytes))}";
        reveal.Visibility = selected.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
        delete.Content = selected.Count == 1 ? "Delete…" : $"Delete {selected.Count:N0} files…";
    }

    private void Reveal() { if (list.Selected is [var one]) DesktopShortcuts.Open(shell.DialogOwner, "explorer", one.FullPath); }

    /// <summary>Selects rows by index for the UI check.</summary>
    internal void SelectRows(params int[] rows) { list.View.SelectedItems.Clear(); foreach (var row in rows) list.View.SelectedItems.Add(visible[row]); }

    private async Task Cleanup()
    {
        if (IsBusy) return;
        var selected = list.Selected;
        if (selected.Count == 0) return;
        if (selected.Count > 1000) { summary.Text = "Select at most 1,000 files at a time."; return; }
        var checkedFiles = selected.Select(f => (File: f, Blocked: CleanupPolicy.BlockReason(f))).ToArray();
        if (RemovalDialogs.DeleteFiles(shell.DialogOwner, checkedFiles) is not { } permanent) return;
        var eligible = checkedFiles.Where(f => f.Blocked is null).Select(f => f.File).ToArray();

        using var cts = new CancellationTokenSource(); running = cts; SetBusy(true);
        using var ticket = shell.Tasks.Begin("Files", cts.Cancel);
        var removed = new List<InventoryFile>(); var failures = new List<string>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = shell.DialogOwner.Handle;
        // Recycling needs an STA thread for the Windows shell; each file is checked again right before it goes.
        var worker = new Thread(() => {
            try {
                foreach (var entry in eligible) {
                    if (cts.Token.IsCancellationRequested) { failures.Add("Cancelled: the remaining files were kept."); break; }
                    try { if (permanent) RecycleService.DeletePermanently(entry); else RecycleService.Recycle(entry, owner); removed.Add(entry); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or COMException) { failures.Add($"{entry.Name}: {ex.Message}"); }
                }
            } finally { completion.SetResult(); }
        }) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        summary.Text = permanent ? "Deleting the confirmed files. Cancel stops before the next file." : "Moving the confirmed files to the Recycle Bin. Cancel stops before the next file; Windows may show its own dialog.";
        try {
            worker.Start(); await completion.Task;
            foreach (var entry in removed) inventory.Remove(entry);
            long bytes = removed.Sum(f => f.Bytes);
            string done = permanent ? $"Deleted {removed.Count:N0} of {eligible.Length:N0} files permanently ({ByteSize.Text(bytes)} freed)."
                : $"Moved {removed.Count:N0} of {eligible.Length:N0} files to the Recycle Bin ({ByteSize.Text(bytes)}). Restore them from the Recycle Bin; the space is freed when you empty it.";
            if (removed.Count > 0)
                RemovalLog.TryAdd(new(DateTimeOffset.Now, permanent ? "Files deleted permanently" : "Files moved to the Recycle Bin",
                    $"{removed.Count:N0} {(removed.Count == 1 ? "file" : "files")}, {ByteSize.Text(bytes)}: " + string.Join(", ", removed.Take(5).Select(f => f.FullPath)) + (removed.Count > 5 ? $" and {removed.Count - 5:N0} more" : "")));
            scanSummary = done + (failures.Count == 0 ? "" : "\nNot deleted: " + string.Join("; ", failures.Take(5)) + (failures.Count > 5 ? $"; and {failures.Count - 5} more" : "")) + "\nThe list may be out of date; rescan for current totals.";
            ShowCategories(); RefreshView();
        }
        finally { running = null; SetBusy(false); UpdateSummary(); }
    }

    private void SetBusy(bool busy)
    {
        browse.IsEnabled = apply.IsEnabled = largest.IsEnabled = showAll.IsEnabled = query.IsEnabled = extension.IsEnabled = minimum.IsEnabled = !busy;
        list.View.IsEnabled = !busy; selectionBar.IsEnabled = !busy; cancel.IsEnabled = busy; foreach (var p in presets) p.IsEnabled = !busy;
    }
}
