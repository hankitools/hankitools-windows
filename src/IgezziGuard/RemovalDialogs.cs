namespace IgezziGuard;

/// <summary>Themed confirmation dialogs for removals: a title, what will happen, and one clearly labelled action.</summary>
internal static class RemovalDialogs
{
    private static (Form Dialog, FlowLayoutPanel Body, HankiButton Confirm) Frame(string title, string message, string confirm)
    {
        var dialog = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(26, 22, 26, 20), Font = new Font("Segoe UI", 10.5f) };
        var body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        body.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 15f), MaximumSize = new Size(600, 0), UseMnemonic = false, Margin = new Padding(0, 0, 0, 8) });
        body.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false, Tag = "intro", Margin = new Padding(0, 0, 0, 12) });
        var confirmButton = new HankiButton { Text = confirm, Primary = true, AutoSize = true, DialogResult = DialogResult.OK, Margin = new Padding(8, 0, 0, 0) };
        var cancel = new HankiButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, Margin = Padding.Empty };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0, 16, 0, 0) };
        buttons.Controls.AddRange([confirmButton, cancel]);
        body.Controls.Add(buttons);
        body.SizeChanged += (_, _) => buttons.MinimumSize = new Size(body.ClientSize.Width, 0);
        dialog.Controls.Add(body); dialog.AcceptButton = null; dialog.CancelButton = cancel;
        return (dialog, body, confirmButton);
    }

    /// <summary>A yes/no question with a named action; the action is never the default button.</summary>
    internal static bool Confirm(IWin32Window owner, string title, string message, string confirm)
    {
        var (dialog, _, _) = Frame(title, message, confirm);
        using (dialog) { HankiTheme.Apply(dialog); return dialog.ShowDialog(owner) == DialogResult.OK; }
    }

    /// <summary>
    /// Delete files: Recycle Bin (restorable, the default) or permanently (after "I understand"). Files that can't be
    /// deleted are listed with why and skipped. Returns true for permanent, false for Recycle Bin, null when cancelled.
    /// </summary>
    internal static bool? DeleteFiles(IWin32Window owner, IReadOnlyList<(InventoryFile File, string? Blocked)> files)
    {
        var eligible = files.Where(f => f.Blocked is null).Select(f => f.File).ToArray();
        int skipped = files.Count - eligible.Length;
        string Count(int n) => n == 1 ? "1 file" : $"{n:N0} files";
        string message = eligible.Length == 0 ? "None of the selected files can be deleted. The list below says why."
            : $"{Count(eligible.Length)} · {ByteSize.Text(eligible.Sum(f => f.Bytes))}" + (skipped > 0 ? $". {Count(skipped)} can't be deleted and will be skipped." : ".") +
              " Close programs that use these files first. Files in a synced folder (OneDrive and similar) are removed from your other devices too.";
        var (dialog, body, confirm) = Frame(eligible.Length == 1 ? "Delete this file?" : $"Delete {Count(eligible.Length)}?", message, "");
        using (dialog) {
            var list = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Width = 600, Height = 150, AccessibleName = "Files to delete",
                Text = string.Join("\r\n", files.Select(f => $"{ByteSize.Text(f.File.Bytes),10}   {f.File.FullPath}" + (f.Blocked is { } why ? $"\r\n{"",13}Skipped: {why}" : ""))),
                Margin = new Padding(0, 0, 0, 14) };
            NativeTheme.PadText(list);
            var choices = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Tag = "choices" };
            var recycle = new ChoiceTile("Move to Recycle Bin", "You can restore them. The space is freed when you empty the Recycle Bin.") { Value = false, Selected = true, Margin = new Padding(0, 0, 12, 0) };
            var permanent = new ChoiceTile("Delete permanently", "Frees the space now. This can't be undone.", HankiTheme.Critical) { Value = true };
            foreach (var tile in new[] { recycle, permanent }) { tile.Width = 294; tile.Height = tile.HeightFor(294); }
            choices.Controls.AddRange([recycle, permanent]);
            var understand = new CheckBox { Text = "I understand these files can't be recovered", AutoSize = true, Visible = false, Margin = new Padding(0, 12, 0, 0) };
            body.Controls.Add(list); body.Controls.Add(choices); body.Controls.Add(understand);
            body.Controls.SetChildIndex(list, 2); body.Controls.SetChildIndex(choices, 3); body.Controls.SetChildIndex(understand, 4);
            void Update() {
                bool forever = permanent.Selected;
                understand.Visible = forever;
                confirm.Text = forever ? $"Delete {Count(eligible.Length)} permanently" : $"Move {Count(eligible.Length)} to Recycle Bin";
                confirm.Enabled = eligible.Length > 0 && (!forever || understand.Checked);
            }
            recycle.Chosen += Update; permanent.Chosen += Update; understand.CheckedChanged += (_, _) => Update();
            Update();
            HankiTheme.Apply(dialog);
            return dialog.ShowDialog(owner) == DialogResult.OK ? permanent.Selected : null;
        }
    }
}
