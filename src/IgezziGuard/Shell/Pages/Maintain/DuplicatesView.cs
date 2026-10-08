using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace IgezziGuard.Shell;

/// <summary>Duplicates: find identical files in a folder (size, then SHA-256), then recycle one copy after a review that keeps a matching copy.</summary>
internal sealed class DuplicatesView : Grid
{
    private readonly IShellServices shell;
    private readonly Button find = Buttons.Primary("Find duplicates in folder…"), cancel = Buttons.Secondary("Cancel"), recycle = Buttons.Secondary("Review / recycle one copy");
    private readonly TextBlock intro = UiKit.Text("Find duplicate contents using size and SHA-256. Nothing is selected for deletion automatically. Recycling requires another matching copy and the existing personal-folder cleanup policy. Restore recycled files in Windows Recycle Bin.", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly TextBlock status = UiKit.Text("", 13.5, UiKit.Res("TextPrimary"), wrap: true);
    private readonly DiagnosisView summary = new();
    private readonly DataList<DuplicateGroup> groups;
    private readonly DataList<InventoryFile> files;
    private DuplicateResult? result;
    private CancellationTokenSource? running;

    internal int GroupCount => result?.Groups.Count ?? 0;

    internal DuplicatesView(IShellServices shell)
    {
        this.shell = shell;
        Margin = new Thickness(24, 0, 24, 20);
        for (int i = 0; i < 5; i++) RowDefinitions.Add(new RowDefinition { Height = i is 2 or 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        groups = new([new("Copies", 80, g => g.Files.Count.ToString()), new("Size each", 110, g => ByteSize.Text(g.Files[0].Bytes)), new("Space you could free", 160, g => ByteSize.Text(g.Files[0].Bytes * (g.Files.Count - 1))), new("First copy", 500, g => g.Files[0].FullPath)], false, "Duplicate groups");
        files = new([new("Copy in this group", 760, f => f.FullPath), new("Modified", 160, f => f.LastWriteUtc.ToLocalTime().ToString("g"))], false, "Files in the selected group");

        var top = new StackPanel(); intro.Margin = new Thickness(0, 0, 0, 12); top.Children.Add(intro);
        var bar = new WrapPanel(); bar.Children.Add(find); bar.Children.Add(cancel); bar.Children.Add(recycle); top.Children.Add(bar); top.Children.Add(status); top.Children.Add(summary);
        SetRow(top, 0); Children.Add(top);
        groups.View.Margin = new Thickness(0, 8, 0, 8); SetRow(groups.View, 2); Children.Add(groups.View);
        files.View.Margin = new Thickness(0, 0, 0, 0); SetRow(files.View, 3); Children.Add(files.View);
        cancel.IsEnabled = false; recycle.IsEnabled = false;

        find.Click += async (_, _) => await Find();
        cancel.Click += (_, _) => running?.Cancel();
        groups.SelectionChanged += () => { files.SetItems(groups.Selected is [var g] ? g.Files : []); UpdateActions(); };
        files.SelectionChanged += UpdateActions;
        recycle.Click += async (_, _) => await Recycle();
    }

    private void UpdateActions() => recycle.IsEnabled = running is null && groups.Selected is [var g] && files.Selected is [var f] && g.Files.Count > 1 && g.Files.Contains(f);

    /// <summary>For the UI check: shows a prepared result as if a scan had finished.</summary>
    internal void Load(DuplicateResult prepared) { result = prepared; groups.SetItems(prepared.Groups); files.SetItems([]); summary.Show(DuplicateInsights.Summarize(prepared, prepared.Coverage)); }

    private async Task Find()
    {
        if (running is not null) return;
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to search for duplicates" };
        if (picker.ShowDialog() != true) return;
        var folder = picker.FolderName;
        using var cts = new CancellationTokenSource(); running = cts; find.IsEnabled = false; cancel.IsEnabled = true; recycle.IsEnabled = false;
        using var ticket = shell.Tasks.Begin("Duplicates", cts.Cancel);
        status.Text = "Comparing files in " + folder + "…"; summary.Clear(); groups.SetItems([]); files.SetItems([]); result = null;
        try {
            var collected = await Task.Run(() => DuplicateFinder.Scan(folder, cts.Token), cts.Token);
            var report = collected.Coverage + "\r\n\r\n" + string.Join("\r\n\r\n", collected.Groups.Select(g => $"{g.Files.Count} copies × {g.Files[0].Bytes:N0} bytes\r\nSHA-256 {g.Hash}\r\n" + string.Join("\r\n", g.Files.Select(f => f.FullPath))));
            result = collected; groups.SetItems(collected.Groups); summary.Show(DuplicateInsights.Summarize(collected, report)); status.Text = "";
        }
        catch (OperationCanceledException) { status.Text = "Cancelled. Nothing on your PC was changed; incomplete results were discarded."; }
        catch (Exception ex) { status.Text = "Operation stopped: " + ex.Message; }
        finally { running = null; find.IsEnabled = true; cancel.IsEnabled = false; UpdateActions(); }
    }

    private async Task Recycle()
    {
        if (result is null || groups.Selected is not [var group] || files.Selected is not [var target]) return;
        var keeper = group.Files.First(f => f.FullPath != target.FullPath);
        if (!ReviewPresenter.Ask($"Recycle only this file?\n{target.FullPath}\n\nKeep this matching copy:\n{keeper.FullPath}\n\nBoth will be rechecked byte-for-byte. Only eligible personal files can be recycled. This may affect cloud-synced copies. Nothing is permanently deleted by Hanki.")) return;
        var owner = shell.DialogOwner.Handle;
        using var cts = new CancellationTokenSource(); running = cts; find.IsEnabled = false; cancel.IsEnabled = true; recycle.IsEnabled = false;
        using var ticket = shell.Tasks.Begin("Duplicates", cts.Cancel);
        status.Text = "Rechecking both copies…";
        try { status.Text = await RecycleCopy(target, keeper, group.Hash, owner, cts.Token); groups.SetItems([]); files.SetItems([]); summary.Clear(); result = null; }
        catch (OperationCanceledException) { status.Text = "Cancelled. The file was kept."; }
        catch (Exception ex) { status.Text = "Operation stopped: " + ex.Message; }
        finally { running = null; find.IsEnabled = true; cancel.IsEnabled = false; UpdateActions(); }
    }

    private static Task<string> RecycleCopy(InventoryFile target, InventoryFile keeper, string hash, IntPtr owner, CancellationToken token)
    {
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
