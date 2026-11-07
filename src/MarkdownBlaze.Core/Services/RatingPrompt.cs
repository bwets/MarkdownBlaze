using MarkdownBlaze.Models;

namespace MarkdownBlaze.Services;

/// <summary>
/// The one request the app makes of its reader: a rating in the Microsoft Store. It is made once,
/// quietly, after the app has been of some use — and whatever the reader does with it, including
/// closing it unanswered, is the answer. It is not made again.
/// </summary>
internal static class RatingPrompt
{
    /// <summary>
    /// Which round of asking this is. A reader who has answered or dismissed round <c>n</c> is not
    /// asked again until this number is raised above <c>n</c> — raise it in a later version to ask
    /// everyone one more time, and at no other time.
    /// </summary>
    public const int Campaign = 1;

    /// <summary>How many documents have to have been opened before it is fair to ask.</summary>
    public const int AfterDocuments = 5;

    /// <summary>
    /// The Store's review page for this app, addressed by package family name: the Identity Name in
    /// packaging/windows/AppxManifest.xml (the part before the underscore) plus the Store's 13-character
    /// hash of its Publisher. A test reads the manifest and checks that the two agree, so renaming the
    /// package in one place and not the other fails the build instead of sending readers nowhere.
    /// <para>
    /// It is a fixed address the app chose itself, not one that came from a document, so it is opened
    /// through <see cref="Platform.IShellLauncher.OpenStoreReview"/> and not through the link allow-list
    /// (<c>ms-windows-store:</c> is not on it, on purpose).
    /// </para>
    /// </summary>
    public static readonly Uri StoreReviewUri =
        new("ms-windows-store://review/?PFN=28961GregoryKieffer.MarkdownBlaze_2emvhta21ts0y");

    /// <summary>Whether this round's request is still unanswered — the only time documents are counted.</summary>
    public static bool IsPending(Preferences prefs) =>
        OperatingSystem.IsWindows() && prefs.RatingAnswered < Campaign;

    /// <summary>Whether to show the request now.</summary>
    public static bool IsDue(Preferences prefs) =>
        IsPending(prefs) && prefs.DocumentsOpened >= AfterDocuments;
}
