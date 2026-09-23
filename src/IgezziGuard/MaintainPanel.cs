namespace IgezziGuard;

public sealed class MaintainPanel : UserControl
{
    private readonly HankiButton browse = Button("Choose folder / drive");
    private readonly HankiButton stop = Button("Cancel");
    private readonly HankiButton apply = Button("Apply filters");
    private readonly HankiButton largest = Button("Largest 100");
    private readonly HankiButton showAll = Button("All files");
    private readonly HankiButton reveal = Button("Open location");
    private readonly HankiButton cleanup = Button("Preview cleanup…");
    private readonly FlowLayoutPanel selectionActions = new() { Dock = DockStyle.Top, AutoSize = true, Visible = false };
    private readonly Label selectionHint = new() { AutoSize = true, Margin = new Padding(8, 12, 12, 0) };
    private readonly ComboBox preset = new() { Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox query = new() { Width = 170, PlaceholderText = "Filename contains…" };
    private readonly TextBox extension = new() { Width = 100, PlaceholderText = ".zip / .mp4" };
    private readonly NumericUpDown minimum = new() { Width = 100, Maximum = 10_000_000, DecimalPlaces = 1 };
    private readonly Label summary = new() { Dock = DockStyle.Bottom, Height = 55, AutoEllipsis = true };
    private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = true, HideSelection = false, VirtualMode = true, BackColor = Color.FromArgb(24, 36, 52), ForeColor = Color.WhiteSmoke };
    private List<InventoryFile> inventory = new();
    private InventoryFile[] visible = [];
    private CancellationTokenSource? running;
    private int sortColumn = 1;
    private bool descending = true, topOnly;
    private string scanSummary = "Choose a folder or drive to see what's using space. Nothing is selected or deleted automatically.";
    // Space by file type after a scan; clicking a chip filters the list to that type.
    private readonly FlowLayoutPanel categories = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Visible = false, Padding = new Padding(0, 4, 0, 6) };
    private string? category;
    public bool IsBusy => running is not null;
    public event Action<bool>? BusyChanged;
    public void Cancel() => running?.Cancel();

    public MaintainPanel()
    {
        Dock = DockStyle.Fill;
        ForeColor = Color.WhiteSmoke;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        bar.Controls.AddRange([browse, stop, largest, showAll]);
        selectionActions.Controls.AddRange([selectionHint, reveal, cleanup]);
        var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        preset.Items.AddRange(["All file types / ages", "Not modified in 90+ days", "Installers / archives", "Large files (250+ MiB)"]); preset.SelectedIndex = 0;
        preset.SelectedIndexChanged += (_, _) => RefreshView();
        filters.Controls.AddRange([preset, query, extension, new Label { Text = "Min MiB:", AutoSize = true }, minimum, apply]);
        layout.Controls.Add(bar, 0, 0); layout.Controls.Add(filters, 0, 1); layout.Controls.Add(categories, 0, 2);
        var fileArea = new Panel { Dock = DockStyle.Fill }; fileArea.Controls.Add(list); fileArea.Controls.Add(selectionActions);
        layout.Controls.Add(fileArea, 0, 3); layout.Controls.Add(summary, 0, 4);
        Controls.Add(layout);
        list.Columns.Add("Name", 220); list.Columns.Add("Size ↓", 110); list.Columns.Add("Type", 75);
        list.Columns.Add("Modified", 160); list.Columns.Add("Full path", 550);
        list.RetrieveVirtualItem += (_, e) => {
            var f = visible[e.ItemIndex];
            e.Item = new ListViewItem([f.Name, SizeText(f.Bytes), f.Extension, f.LastWriteUtc.ToLocalTime().ToString("g"), f.FullPath]);
        };
        list.ColumnClick += (_, e) => {
            descending = sortColumn == e.Column ? !descending : e.Column == 1;
            sortColumn = e.Column; RefreshView();
        };
        list.SelectedIndexChanged += (_, _) => UpdateSummary();
        list.DoubleClick += (_, _) => Reveal();
        browse.Click += async (_, _) => await Scan();
        stop.Click += (_, _) => Cancel();
        apply.Click += (_, _) => RefreshView();
        largest.Click += (_, _) => { sortColumn = 1; descending = true; topOnly = true; RefreshView(); };
        showAll.Click += (_, _) => { topOnly = false; RefreshView(); };
        reveal.Click += (_, _) => Reveal();
        cleanup.Click += async (_, _) => await Cleanup();
        SetBusy(false);
        UpdateSummary();
    }

    private async Task Scan()
    {
        if (IsBusy) return;
        using var chooser = new FolderBrowserDialog { Description = "Choose a folder or drive to inspect", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (chooser.ShowDialog(this) != DialogResult.OK) return;
        using var cts = new CancellationTokenSource(); running = cts; SetBusy(true);
        inventory.Clear(); visible = []; list.VirtualListSize = 0;
        summary.Text = "Scanning " + chooser.SelectedPath;
        try
        {
            var progress = new Progress<InventoryProgress>(p => {
                if (running == cts) summary.Text = $"Scanning: {p.Files:N0} files · {SizeText(p.Bytes)} logical size · {p.Errors} errors · {p.Skipped} skipped";
            });
            var result = await Task.Run(() => FileInventory.Scan(chooser.SelectedPath, progress, cts.Token), cts.Token);
            inventory = result.Files;
            scanSummary = $"{chooser.SelectedPath}: {inventory.Count:N0} files · {SizeText(result.Bytes)} in total · {result.Errors} unreadable · {result.Skipped} skipped" +
                (result.Limited ? " · PARTIAL: stopped at 100,000 files; choose a smaller folder for a complete picture." : "");
            sortColumn = 1; descending = true; category = null;
            ShowCategories(); RefreshView();
        }
        catch (OperationCanceledException) { scanSummary = "Scan cancelled; incomplete inventory discarded. Choose a folder to scan again."; }
        catch (Exception ex) { scanSummary = "Scan failed: " + ex.Message; }
        finally { running = null; SetBusy(false); UpdateSummary(); }
    }

    private void RefreshView()
    {
        var text = query.Text.Trim();
        var ext = extension.Text.Trim();
        if (ext.Length > 0 && !ext.StartsWith('.')) ext = "." + ext;
        var minBytes = (long)(minimum.Value * 1024 * 1024);
        IEnumerable<InventoryFile> matches = inventory.Where(f => f.Bytes >= minBytes &&
            f.Name.Contains(text, StringComparison.OrdinalIgnoreCase) &&
            (ext.Length == 0 || f.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase)));
        if (category is not null) matches = matches.Where(f => FileCategories.Of(f.Extension) == category);
        matches = preset.SelectedIndex switch {
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
        var next = (topOnly ? matches.Take(100) : matches).ToArray();
        list.SelectedIndices.Clear(); list.VirtualListSize = 0;
        visible = next; list.VirtualListSize = visible.Length; list.Invalidate();
        string[] headers = ["Name", "Size", "Type", "Modified", "Full path"];
        for (var i = 0; i < headers.Length; i++) list.Columns[i].Text = headers[i] + (sortColumn == i ? descending ? " ↓" : " ↑" : "");
        UpdateSummary();
    }
    private void ShowCategories()
    {
        foreach (Control old in categories.Controls.Cast<Control>().ToArray()) old.Dispose();
        var totals = FileCategories.Totals(inventory);
        categories.Visible = totals.Count > 0;
        if (totals.Count == 0) return;
        categories.Controls.Add(new Label { Text = "Space by type:", AutoSize = true, Tag = "intro", Margin = new Padding(0, 10, 8, 0) });
        foreach (var (name, files, bytes) in totals.Take(7)) {
            var chip = new HankiButton { Text = $"{name}  {SizeText(bytes)}", AutoSize = true, Primary = category == name, Margin = new Padding(0, 3, 6, 3),
                AccessibleName = $"Show only {name}: {files:N0} files, {SizeText(bytes)}", AccessibleDescription = category == name ? "Filter active; click to clear" : "" };
            chip.Click += (_, _) => { category = category == name ? null : name; ShowCategories(); RefreshView(); };
            categories.Controls.Add(chip);
        }
        HankiTheme.Apply(categories);
    }
    private string TextKey(InventoryFile f) => sortColumn switch { 2 => f.Extension, 4 => f.FullPath, _ => f.Name };
    private InventoryFile[] Selected() => list.SelectedIndices.Cast<int>().Where(i => i < visible.Length).Select(i => visible[i]).ToArray();
    private void UpdateSummary()
    {
        if (IsBusy) return;
        var selected = Selected();
        summary.Text = scanSummary + $"\nShowing {visible.Length:N0}" + (category is null ? "" : $" {category.ToLowerInvariant()}") + $" · selected {selected.Length:N0} ({SizeText(selected.Sum(f => f.Bytes))}). " +
            "Filters help you review; they don't prove a file is unneeded. Cleaned-up files go to the Recycle Bin, so space is freed when you empty it.";
        selectionActions.Visible = selected.Length > 0;
        selectionHint.Text = $"{selected.Length:N0} selected · {SizeText(selected.Sum(f => f.Bytes))}";
        reveal.Visible = selected.Length == 1;
        cleanup.Text = $"Preview cleanup ({selected.Length:N0})…";
        reveal.Enabled = selected.Length == 1;
        cleanup.Enabled = selected.Length > 0;
    }
    private void Reveal()
    {
        var selected = Selected();
        if (selected.Length == 1) DesktopShortcuts.Open(this, "explorer", selected[0].FullPath);
    }

    private async Task Cleanup()
    {
        if (IsBusy) return;
        var selected = Selected();
        if (selected.Length == 0) return;
        if (selected.Length > 100) { MessageBox.Show(this, "Select at most 100 files per cleanup batch.", "Hanki Maintain"); return; }
        var reasons = selected.Select(f => (File: f, Reason: CleanupPolicy.BlockReason(f))).ToArray();
        using var preview = new Form { Text = "Review cleanup — files will go to Recycle Bin", Size = new Size(850, 570),
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            Text = $"Selected: {selected.Length} files, {SizeText(selected.Sum(f => f.Bytes))}\r\n\r\n" +
                "Restore through Windows Recycle Bin. Recycling does not immediately reclaim disk space.\r\n" +
                "Cloud-synced deletions may propagate to other devices. Close programs using these files first.\r\n" +
                "This preview has not been runtime-tested on Windows. Test only on disposable copies first.\r\n\r\n" +
                string.Join("\r\n\r\n", reasons.Select(r => $"{r.File.FullPath}\r\n{SizeText(r.File.Bytes)} — {r.Reason ?? "Eligible for recycling"}")) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft };
        var back = new HankiButton { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var approve = new HankiButton { Text = "Move selected to Recycle Bin", DialogResult = DialogResult.OK, AutoSize = true,
            Enabled = reasons.All(r => r.Reason is null) };
        buttons.Controls.AddRange([back, approve]); preview.Controls.Add(details); preview.Controls.Add(buttons);
        preview.CancelButton = back; preview.AcceptButton = back;
        HankiTheme.Apply(preview);
        if (preview.ShowDialog(this) != DialogResult.OK) return;

        using var cts = new CancellationTokenSource(); running = cts; SetBusy(true);
        var recycled = new List<InventoryFile>();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = FindForm()!.Handle;
        var worker = new Thread(() => {
            try {
                foreach (var entry in selected) {
                    cts.Token.ThrowIfCancellationRequested();
                    RecycleService.Recycle(entry, owner);
                    recycled.Add(entry);
                }
                completion.SetResult(null);
            }
            catch (OperationCanceledException) { completion.SetResult("Cancelled. Remaining files were not processed."); }
            catch (Exception ex) { completion.SetResult("Stopped: " + ex.Message + "\nRemaining files were not processed. Rescan to refresh the inventory."); }
        }) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        summary.Text = "Recycling confirmed selection. Cancel stops before the next file; Windows may show its own dialog.";
        try {
            worker.Start();
            var error = await completion.Task;
            foreach (var entry in recycled) inventory.Remove(entry);
            scanSummary = "Cleanup finished; inventory may be stale. Rescan for current totals.";
            RefreshView();
            MessageBox.Show(this, $"Confirmed recycled: {recycled.Count}/{selected.Length} files.\nRestore them through Windows Recycle Bin.\n\n" +
                (error ?? "No permanent-delete fallback was used."), "Cleanup results");
        }
        finally { running = null; SetBusy(false); UpdateSummary(); }
    }

    private void SetBusy(bool value)
    {
        browse.Enabled = apply.Enabled = largest.Enabled = showAll.Enabled = query.Enabled = extension.Enabled = minimum.Enabled = list.Enabled = !value;
        cleanup.Enabled = reveal.Enabled = !value;
        selectionActions.Enabled = !value;
        stop.Enabled = value;
        BusyChanged?.Invoke(value);
    }
    private static HankiButton Button(string title) => new() { Text = title, AutoSize = true, Primary = title == "Choose folder / drive", Appearance = title == "Open location" ? HankiButtonStyle.Quiet : HankiButtonStyle.Secondary, ForeColor = Color.Black, Margin = new Padding(3, 5, 3, 5) };
    internal static string SizeText(long bytes) => ByteSize.Text(bytes);
}
