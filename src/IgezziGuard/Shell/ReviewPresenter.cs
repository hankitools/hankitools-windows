namespace IgezziGuard.Shell;

/// <summary>
/// The one way to ask "are you sure?" about a change. When the WPF shell is running it shows the review drawer; otherwise
/// (the legacy window, tests) callers fall back to the dialogs they had, so nothing is lost.
/// </summary>
internal static class ReviewPresenter
{
    /// <summary>Shows a review and returns what was chosen. Set by the shell; null when there is no drawer.</summary>
    internal static Func<ReviewRequest, ReviewResult>? Provider { get; set; }
    internal static bool IsAvailable => Provider is not null;

    internal static ReviewResult Ask(ReviewRequest request) => Provider is { } show ? show(request) : throw new InvalidOperationException("No review drawer is available.");
    /// <summary>Asks about a plain-text confirmation; true when confirmed.</summary>
    internal static bool Ask(string text) => Ask(ReviewText.Parse(text)).Confirmed;
}
