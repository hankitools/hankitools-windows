namespace IgezziGuard;

/// <summary>How Recovery describes a recorded change: a plain name, readable before/after values, and whether it can be undone.</summary>
public static class RecoveryText
{
    private static readonly Dictionary<string, string> PowerPlans = new(StringComparer.OrdinalIgnoreCase) {
        ["381b4222-f694-41f0-9685-ff5bb260df2e"] = "Balanced",
        ["8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"] = "High performance",
        ["a1841308-3541-4fab-bc81-f71556f20b4a"] = "Power saver",
        ["e9a42b02-d5df-448d-aa00-03f14749eb61"] = "Ultimate Performance",
    };

    /// <summary>The setting in plain words ("Power plan", "Game Mode", "Startup file").</summary>
    public static string Title(SettingChange change) => change.Kind switch {
        "Power plan" => "Power plan", "IPv4 DNS" => "IPv4 DNS servers", "Startup file" => "Startup file", _ => HomeActivity.ChangeLabel(change)
    };

    /// <summary>What the change applied to, when that says more than the title (a file name); otherwise empty.</summary>
    public static string Subject(SettingChange change) => change.Kind switch {
        "Startup file" => Path.GetFileName(change.Target.Replace('\\', '/')),
        "GPU preference" or "NVIDIA setting" => Path.GetFileName(change.Target.Split('|')[0].Replace('\\', '/')),
        _ => ""
    };

    /// <summary>A saved value as a person would read it: plan names instead of GUIDs, "Automatic" for empty DNS.</summary>
    public static string Value(string kind, string value) => kind switch {
        "Power plan" => PowerPlans.TryGetValue(value.Trim(), out var name) ? name : value,
        "IPv4 DNS" => value.Length == 0 ? "Automatic (from your router)" : value.Replace(",", ", "),
        "Startup file" => value.StartsWith("Enabled:", StringComparison.Ordinal) ? "In the Startup folder" : value.StartsWith("Disabled:", StringComparison.Ordinal) ? "Moved to Hanki's backup" : value,
        _ => value.Length == 0 ? "Windows default" : value
    };

    /// <summary>Whether Undo can still run: the change was applied (or left pending for inspection) and has not been undone.</summary>
    public static bool CanUndo(SettingChange change) => change.Status == "Applied" || change.Status.StartsWith("Pending", StringComparison.Ordinal);

    /// <summary>A short status word for a list row.</summary>
    public static string StatusLabel(SettingChange change) => change.Status switch {
        "Applied" => "Applied", "Undone" => "Undone", ChangeJournal.NotApplied => "Not applied", var s when s.StartsWith("Pending", StringComparison.Ordinal) => "Needs a look", var s => s
    };

    /// <summary>The review to show before undoing a change.</summary>
    public static ReviewRequest UndoReview(SettingChange change)
    {
        string name = Title(change), subject = Subject(change);
        return new ReviewRequest($"Put back {name}" + (subject.Length > 0 ? $" for {subject}" : "") + "?", "Hanki changed this earlier and saved what it was.") {
            Rows = [new ReviewRow("Now", Value(change.Kind, change.After)), new ReviewRow("Put back", Value(change.Kind, change.Before))],
            Notes = [
                new(ReviewNoteKind.Undo, "If something else changed this setting since, undo stops instead of overwriting it."),
                .. change.Kind == "IPv4 DNS" ? new[] { new ReviewNote(ReviewNoteKind.Warning, "Restoring DNS can briefly affect name resolution.") } : [],
                .. change.Kind == "Startup file" ? new[] { new ReviewNote(ReviewNoteKind.Warning, "A restored startup file may run again at your next sign-in.") } : [],
            ],
            ConfirmLabel = "Put back",
        };
    }
}
