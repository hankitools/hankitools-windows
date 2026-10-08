using IgezziGuard;

// Review drawer model: plain-text confirmations become structured reviews without losing anything, and Recovery reads plainly.
internal static class ReviewChecks
{
    private static void Check(bool ok, string text) => DiagnosticChecks.Check(ok, text);
    internal static void Run() { Parse(); Recovery(); }

    private static string AllText(ReviewRequest r) => string.Join(" | ", new[] { r.Title, r.Intro ?? "" }.Concat(r.Rows.Select(x => x.Label + ": " + x.Value)).Concat(r.Notes.Select(n => n.Text)));

    private static void Parse()
    {
        const string dns = "Change IPv4 DNS for WLAN?\nBefore: Automatic\nAfter: 1.1.1.1,1.0.0.1\n\nMay interrupt name resolution or break private/corporate names. Public DNS receives future queries. IPv6 DNS is not changed. Policy/VPN settings can take precedence. Recovery stores the original configuration. Administrator rights may be required.";
        var change = ReviewText.Parse(dns);
        Check(change.Title == "Change IPv4 DNS for WLAN?" && change.ConfirmLabel == "Apply change" && !change.Danger, "review: a setting change gets its title and an Apply change button");
        Check(change.Rows.Count == 2 && change.Rows[0] is { Label: "Before", Value: "Automatic" } && change.Rows[1] is { Label: "After", Value: "1.1.1.1,1.0.0.1" }, "review: Before and After become rows");
        Check(change.Notes.Count == 6 && change.Notes.Any(n => n.Kind == ReviewNoteKind.Admin && n.Text.StartsWith("Administrator", StringComparison.Ordinal))
            && change.Notes.Any(n => n.Kind == ReviewNoteKind.Undo && n.Text.StartsWith("Recovery", StringComparison.Ordinal)) && change.Notes.Any(n => n.Kind == ReviewNoteKind.Warning && n.Text.StartsWith("May interrupt", StringComparison.Ordinal)),
            "review: every sentence is kept and marked as a warning, undo or administrator note");

        var removal = ReviewText.Parse("Remove this session from Performance history? Any settings it changed stay as they are; undo them from Recovery.");
        Check(removal.Title == "Remove this session from Performance history?" && removal.Intro == "Any settings it changed stay as they are; undo them from Recovery." && removal.ConfirmLabel == "Remove" && removal.Danger,
            "review: a one-paragraph question splits into a title and an introduction; removals are marked as dangerous");

        var restore = ReviewText.Parse("Restore Power plan: Active?\nCurrent expected: B\nRestore: A\n\nExternal changes will block undo. Restoring DNS can briefly affect name resolution; a restored startup file may run on next sign-in.");
        Check(restore.ConfirmLabel == "Restore" && restore.Rows.Select(r => r.Label).SequenceEqual(["Current expected", "Restore"]), "review: a restore keeps its current and restore values");

        var vague = ReviewText.Parse(new string('x', 200) + " without any question");
        Check(vague.Title == "Review this action" && vague.Intro is { Length: > 100 } || vague.Notes.Count > 0, "review: an unstructured long text still gets a title and keeps its content");

        var probes = ReviewText.Parse("Include network probes? Gateway ICMP contacts your local network. A and B. Installed KMS clients may also query your organization DNS. These endpoints can see your source IP.");
        Check(probes.Title == "Include network probes?" && probes.ConfirmLabel == "Include" && AllText(probes).Contains("source IP", StringComparison.Ordinal), "review: network probe disclosure keeps its text");
        foreach (var text in new[] { dns, "Delete your preset “Fast”? Your NVIDIA settings aren't changed.", "Start Foo and measure it?\r\n\nHanki waits for the game's window. Then it closes." }) {
            var parsed = ReviewText.Parse(text);
            var original = text.Replace("\r", " ").Replace("\n", " ").Split(new[] { ' ', ':', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var kept = AllText(parsed);
            Check(original.All(w => kept.Contains(w, StringComparison.Ordinal)), "review: no word of the original text is lost (" + text[..Math.Min(24, text.Length)] + "…)");
        }
    }

    private static void Recovery()
    {
        var power = new SettingChange(Guid.NewGuid(), DateTimeOffset.Now, "Power plan", "Active", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "381b4222-f694-41f0-9685-ff5bb260df2e", "Applied");
        var dns = new SettingChange(Guid.NewGuid(), DateTimeOffset.Now, "IPv4 DNS", "guid", "", "1.1.1.1,1.0.0.1", "Applied");
        var undone = power with { Status = "Undone" };
        Check(RecoveryText.Value("Power plan", power.Before) == "High performance" && RecoveryText.Value("Power plan", power.After) == "Balanced" && RecoveryText.Value("Power plan", "other") == "other", "recovery: power plans are named, not shown as GUIDs");
        Check(RecoveryText.Value("IPv4 DNS", "") == "Automatic (from your router)" && RecoveryText.Value("IPv4 DNS", "1.1.1.1,1.0.0.1") == "1.1.1.1, 1.0.0.1", "recovery: empty DNS reads as Automatic");
        Check(RecoveryText.CanUndo(power) && !RecoveryText.CanUndo(undone) && !RecoveryText.CanUndo(power with { Status = ChangeJournal.NotApplied }) && RecoveryText.CanUndo(power with { Status = "Pending / inspect" }), "recovery: only applied or pending changes can be undone");
        var review = RecoveryText.UndoReview(dns);
        Check(review.Title == "Put back IPv4 DNS servers?" && review.Rows.Count == 2 && review.Rows[1].Value == "Automatic (from your router)" && review.Notes.Any(n => n.Kind == ReviewNoteKind.Warning) && review.ConfirmLabel == "Put back", "recovery: the undo review names the setting and shows what comes back");
        Check(RecoveryText.StatusLabel(power) == "Applied" && RecoveryText.StatusLabel(undone) == "Undone" && RecoveryText.StatusLabel(power with { Status = "Pending / inspect" }) == "Needs a look", "recovery: status words are plain");
    }
}
