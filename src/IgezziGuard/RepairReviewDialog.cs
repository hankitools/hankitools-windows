namespace IgezziGuard;

/// <summary>
/// "Review repairs": one card per proposed repair saying why it was proposed, what it changes and how to undo it.
/// Nothing is ticked in advance, and nothing runs until the technician chooses Repair selected.
/// </summary>
internal static class RepairReviewDialog
{
    internal static RepairApproval? Show(IWin32Window owner, DiagnosticScan scan, IReadOnlyList<IRepairAction> actions, bool administrator)
    {
        var (dialog, approval) = Build(scan, actions, administrator);
        using (dialog) return dialog.ShowDialog(owner) == DialogResult.OK ? approval() : null;
    }

    /// <summary>The dialog and a function that reads the approval from it; separate from Show so it can be rendered for review.</summary>
    internal static (Form Dialog, Func<RepairApproval?> Approval) Build(DiagnosticScan scan, IReadOnlyList<IRepairAction> actions, bool administrator)
    {
        const int width = 560;
        var dialog = new Form { Text = "Review repairs", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20) };
        var bold = new Font(dialog.Font, FontStyle.Bold);
        dialog.Disposed += (_, _) => bold.Dispose();
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        Label Text(string text, string? tag = null, int top = 3) =>
            new() { Text = text, AutoSize = true, MaximumSize = new Size(width - 30, 0), Tag = tag, Margin = new Padding(0, top, 0, 0), UseMnemonic = false };
        layout.Controls.Add(new Label { Text = "Nothing changes until you choose Repair selected. Each repair checks the problem again just before it runs, and afterwards to see whether it worked.",
            AutoSize = true, MaximumSize = new Size(width, 0), Tag = "intro", Margin = new Padding(0, 0, 0, 4) });

        var choices = new List<(CheckBox Box, RepairDefinition Definition)>();
        foreach (var d in actions.Select(a => a.Definition)) {
            var card = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Tag = "card",
                MinimumSize = new Size(width, 0), Padding = new Padding(14, 10, 14, 12), Margin = new Padding(0, 12, 0, 0) };
            var box = new CheckBox { Text = d.Title, Font = bold, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
            card.Controls.Add(box);
            if (RepairGuidance.Reason(scan, d.Id) is { Length: > 0 } reason) card.Controls.Add(Text("Why: " + reason));
            card.Controls.Add(Text("What it does: " + d.ChangeDescription));
            card.Controls.Add(Text($"{d.Risk} risk · {RepairGuidance.Restart(d.Restart)} · Undo: {d.RollbackInformation}", "intro", 6));
            if (RepairGuidance.Blocker(d, administrator) is { } blocker) { box.Enabled = false; card.Controls.Add(Text(blocker, "status-review", 6)); }
            layout.Controls.Add(card); choices.Add((box, d));
        }

        CheckBox? network = null, restore = null;
        var networkUsers = choices.Select(c => c.Definition).Where(d => d.RequiresNetwork).ToList();
        if (networkUsers.Count > 0) {
            network = new CheckBox { Text = "Allow network use", AutoSize = true, Font = bold, Margin = new Padding(0, 16, 0, 0) };
            layout.Controls.Add(network);
            layout.Controls.Add(Text(string.Join(" ", networkUsers.Select(RepairGuidance.NetworkUse)), "intro"));
        }
        if (choices.Any(c => c.Definition.RecommendRestorePoint)) {
            restore = new CheckBox { Text = "Continue if a restore point can't be created", AutoSize = true, Font = bold, Margin = new Padding(0, 14, 0, 0) };
            layout.Controls.Add(restore);
            layout.Controls.Add(Text("Hanki creates a Windows restore point before repairing Windows files. If that fails, the repair stops unless this is ticked.", "intro"));
        }

        var problem = Text("", "status-review", 16);
        layout.Controls.Add(problem);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var approve = new HankiButton { Text = "Repair selected", Primary = true, AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new HankiButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([approve, cancel]); layout.Controls.Add(buttons);
        dialog.Controls.Add(layout); dialog.CancelButton = cancel;
        HankiTheme.Apply(dialog);

        IReadOnlyCollection<RepairDefinition> Selected() => choices.Where(c => c.Box.Checked).Select(c => c.Definition).ToList();
        void Refresh() {
            var reason = RepairGuidance.ApprovalProblem(Selected(), network?.Checked ?? false);
            approve.Enabled = reason is null; problem.Text = reason ?? "";
        }
        foreach (var (box, _) in choices) box.CheckedChanged += (_, _) => Refresh();
        if (network is not null) network.CheckedChanged += (_, _) => Refresh();
        Refresh();

        return (dialog, () => RepairGuidance.ApprovalProblem(Selected(), network?.Checked ?? false) is not null ? null
            : new(scan.Id, Selected().Select(d => d.Id).ToHashSet(StringComparer.Ordinal), restore?.Checked ?? false, network?.Checked ?? false));
    }
}
