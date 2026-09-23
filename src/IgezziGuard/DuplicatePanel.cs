namespace IgezziGuard;

public sealed class DuplicatePanel : ToolPage
{
    private readonly ComboBox groups = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox files = new() { Width = 620, DropDownStyle = ComboBoxStyle.DropDownList };
    private DuplicateResult? result;
    public DuplicatePanel() : base("Find duplicate contents using size and SHA-256. Nothing is selected for deletion automatically. Recycling requires another matching copy and the existing personal-folder cleanup policy. Restore recycled files in Windows Recycle Bin; cloud deletions can propagate.") {
        Button("Find duplicates in folder…", async () => {
            using var picker = new FolderBrowserDialog(); if (picker.ShowDialog(this) != DialogResult.OK) return;
            var folder = picker.SelectedPath; DuplicateResult? collected = null;
            await Run(t => { collected = DuplicateFinder.Scan(folder, t); return Task.FromResult(DuplicateInsights.Summarize(collected, collected.Coverage + "\r\n\r\n" + string.Join("\r\n\r\n", collected.Groups.Select(g => $"{g.Files.Count} copies × {g.Files[0].Bytes:N0} bytes\r\nSHA-256 {g.Hash}\r\n" + string.Join("\r\n", g.Files.Select(f => f.FullPath)))))); });
            result = collected; groups.Items.Clear(); files.Items.Clear();
            if (result is not null) foreach (var g in result.Groups) groups.Items.Add($"{g.Files.Count} copies × {MaintainPanel.SizeText(g.Files[0].Bytes)}");
        });
        Bar.Controls.Add(groups); Bar.Controls.Add(files);
        groups.SelectedIndexChanged += (_, _) => { files.Items.Clear(); if (result is not null && groups.SelectedIndex >= 0) foreach (var file in result.Groups[groups.SelectedIndex].Files) files.Items.Add(file.FullPath); };
        Button("Review / recycle one copy", async () => {
            if (result is null || groups.SelectedIndex < 0 || files.SelectedIndex < 0) return;
            var group = result.Groups[groups.SelectedIndex]; var target = group.Files[files.SelectedIndex];
            var keeper = group.Files.First(f => f.FullPath != target.FullPath);
            if (!Review($"Recycle only this file?\n{target.FullPath}\n\nKeep this matching copy:\n{keeper.FullPath}\n\nBoth will be rechecked byte-for-byte. Only eligible personal files can be recycled. This may affect cloud-synced copies. Nothing is permanently deleted by Hanki.")) return;
            var owner = Handle;
            await Run(t => Recycle(target, keeper, group.Hash, owner, t)); groups.Items.Clear(); files.Items.Clear(); result = null;
        });
    }
    private static Task<string> Recycle(InventoryFile target, InventoryFile keeper, string hash, IntPtr owner, CancellationToken token) {
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try {
            if (DuplicateFinder.Hash(target, token) != hash || DuplicateFinder.Hash(keeper, token) != hash) throw new IOException("Content changed. Rescan before cleanup.");
            using var preserved = new FileStream(keeper.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using (var removed = new FileStream(target.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read)) if (!DuplicateFinder.Equal(preserved, removed, token)) throw new IOException("Contents no longer match.");
            token.ThrowIfCancellationRequested(); RecycleService.Recycle(target, owner);
            done.SetResult("Selected duplicate recycled. A matching copy was kept locked during recycling. Restore through Windows Recycle Bin if needed. Rescan for updated groups.");
        } catch (OperationCanceledException) { done.SetCanceled(token); } catch (Exception ex) { done.SetException(ex); } });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start(); return done.Task;
    }
}
