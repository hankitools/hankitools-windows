using System.Text.RegularExpressions;
namespace IgezziGuard;

public enum ReviewNoteKind { Info, Undo, Warning, Admin }
/// <summary>One line of explanation under a review's title, with what kind of line it is (so the drawer can mark it).</summary>
public sealed record ReviewNote(ReviewNoteKind Kind, string Text);
/// <summary>A labelled value such as Before / After.</summary>
public sealed record ReviewRow(string Label, string Value);
/// <summary>One of several mutually exclusive actions (for example Recycle Bin or permanent delete).</summary>
public sealed record ReviewChoice(string Id, string Title, string Description, string ConfirmLabel, bool Danger = false, string? Acknowledge = null);
/// <summary>A checkable card (for example one proposed repair). Nothing is ticked in advance; a blocked item cannot be ticked.</summary>
public sealed record ReviewItem(string Id, string Title, IReadOnlyList<string> Lines, string? Blocked = null)
{
    /// <summary>Ticked when the review opens (nothing is ticked in advance unless a caller says so, for example for non-optional changes).</summary>
    public bool Ticked { get; init; }
    /// <summary>A step only the person can do: shown as a card without a checkbox, and never counted as chosen.</summary>
    public bool Informational { get; init; }
    /// <summary>An optional button on the card (for example "Open settings").</summary>
    public string? LinkLabel { get; init; }
    public Action? Link { get; init; }
}
/// <summary>A separate checkbox with an explanation (for example allowing network use).</summary>
public sealed record ReviewOption(string Id, string Title, string Description);
/// <summary>What the person has chosen so far in a review.</summary>
public sealed record ReviewState(string? ChoiceId, IReadOnlySet<string> Items, IReadOnlySet<string> Options, bool Acknowledged);
public sealed record ReviewResult(bool Confirmed, ReviewState State);

/// <summary>
/// Everything a confirmation needs to show: what is about to happen, what changes, how to undo it and what the choices are.
/// The drawer renders it; callers read the <see cref="ReviewResult"/>. It has no UI types so it can be checked without a window.
/// </summary>
public sealed record ReviewRequest(string Title, string? Intro = null)
{
    public IReadOnlyList<ReviewRow> Rows { get; init; } = [];
    public IReadOnlyList<ReviewNote> Notes { get; init; } = [];
    /// <summary>Lines shown in a scrollable box (for example the files to delete), under <see cref="ListCaption"/>.</summary>
    public IReadOnlyList<string> ListLines { get; init; } = [];
    public string? ListCaption { get; init; }
    public IReadOnlyList<ReviewChoice> Choices { get; init; } = [];
    public IReadOnlyList<ReviewItem> Items { get; init; } = [];
    public IReadOnlyList<ReviewOption> Options { get; init; } = [];
    public string ConfirmLabel { get; init; } = "Continue";
    public bool Danger { get; init; }
    /// <summary>Returns why the current selection cannot be confirmed, or null when it can.</summary>
    public Func<ReviewState, string?>? Validate { get; init; }
}

/// <summary>Turns the plain-text confirmations the pages already write into a structured <see cref="ReviewRequest"/>.</summary>
public static partial class ReviewText
{
    private static readonly string[] RowLabels = ["Before", "After", "Current expected", "Restore"];

    /// <summary>
    /// "Title?\nBefore: x\nAfter: y\n\nSentence. Sentence." becomes a title, Before/After rows, an introduction and marked notes.
    /// A single-paragraph text splits at its first question mark. Nothing is dropped: every sentence ends up somewhere.
    /// </summary>
    public static ReviewRequest Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string title; var rest = new List<string>();
        var first = lines.FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        int at = Array.IndexOf(lines, first);
        var after = lines.Skip(at + 1);
        int question = first.IndexOf("? ", StringComparison.Ordinal);
        if (question is >= 0 and <= 140) { title = first[..(question + 1)].Trim(); rest.Add(first[(question + 2)..].Trim()); }
        else if (first.Length <= 140) title = first.Trim();
        else { title = "Review this action"; rest.Add(first.Trim()); }
        rest.AddRange(after);

        var rows = new List<ReviewRow>(); var paragraphs = new List<string>(); var current = new List<string>();
        void Flush() { if (current.Count > 0) { paragraphs.Add(string.Join(" ", current)); current.Clear(); } }
        foreach (var raw in rest) {
            var line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }
            var row = RowLabels.FirstOrDefault(l => line.StartsWith(l + ": ", StringComparison.Ordinal));
            if (row is not null) { Flush(); rows.Add(new ReviewRow(row, line[(row.Length + 2)..])); }
            else current.Add(line);
        }
        Flush();

        string? intro = paragraphs.Count > 0 ? paragraphs[0] : null;
        var notes = new List<ReviewNote>();
        // The first paragraph stays whole as the introduction when it is short; longer text is split into marked sentences.
        if (intro is not null && intro.Length > 220) { notes.AddRange(Sentences(intro).Select(Classify)); intro = null; }
        foreach (var paragraph in paragraphs.Skip(1)) notes.AddRange(Sentences(paragraph).Select(Classify));
        return new ReviewRequest(title, intro) { Rows = rows, Notes = notes, ConfirmLabel = ConfirmLabelFor(title), Danger = IsDanger(title) };
    }

    private static IEnumerable<string> Sentences(string paragraph) =>
        SentenceBreak().Split(paragraph).Select(s => s.Trim()).Where(s => s.Length > 0);

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z“""(])")]
    private static partial Regex SentenceBreak();

    internal static ReviewNote Classify(string sentence)
    {
        if (Regex.IsMatch(sentence, @"\b(administrator|UAC|elevat)", RegexOptions.IgnoreCase)) return new(ReviewNoteKind.Admin, sentence);
        if (Regex.IsMatch(sentence, @"can't be undone|cannot be undone|permanently|can also affect|may (interrupt|break|affect|run)|remediate|quarantine|removed from your other devices", RegexOptions.IgnoreCase)) return new(ReviewNoteKind.Warning, sentence);
        if (Regex.IsMatch(sentence, @"\b(undo|undone|Recovery|restore|restored|put back|backup)\b", RegexOptions.IgnoreCase)) return new(ReviewNoteKind.Undo, sentence);
        return new(ReviewNoteKind.Info, sentence);
    }

    private static readonly Dictionary<string, string> Verbs = new(StringComparer.OrdinalIgnoreCase) {
        ["Change"] = "Apply change", ["Restore"] = "Restore", ["Put"] = "Put back", ["Remove"] = "Remove", ["Delete"] = "Delete", ["Clear"] = "Clear", ["Forget"] = "Forget",
        ["Recycle"] = "Recycle", ["Move"] = "Move", ["Start"] = "Start", ["Run"] = "Run", ["Send"] = "Send", ["Trace"] = "Trace", ["Compare"] = "Compare", ["Update"] = "Update",
        ["Create"] = "Create", ["Allow"] = "Allow", ["Include"] = "Include", ["Open"] = "Open", ["Map"] = "Map", ["Replace"] = "Replace", ["Uninstall"] = "Uninstall", ["Ask"] = "Continue"
    };
    internal static string ConfirmLabelFor(string title) => Verbs.TryGetValue(FirstWord(title), out var label) ? label : "Continue";
    internal static bool IsDanger(string title) => FirstWord(title) is "Remove" or "Delete" or "Clear" or "Forget" or "Recycle" or "Uninstall" or "Replace";
    private static string FirstWord(string title) => title.Split(' ', 2)[0].Trim('“', '"');
}
