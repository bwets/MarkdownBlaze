namespace MarkdownBlaze.Platform;

/// <summary>
/// The few things the app asks the operating system's shell to do: open an address in whatever handles it,
/// start another window of the app, show a file in the file manager. They are all ways of starting a
/// process, so they are all behind one seam — the platform differences live in <see cref="ShellLauncher"/>,
/// and a test hands the code under test a fake that records what it was asked for instead of starting
/// anything.
/// <para>
/// The rule about <i>which</i> addresses may be opened is not in here: <see cref="Security.ExternalLinks"/>
/// decides that, and the callers ask it before they get here. The launcher asks it again — what a document
/// links to is untrusted, and this is the last step before the shell.
/// </para>
/// </summary>
internal interface IShellLauncher
{
    /// <summary>
    /// Opens <paramref name="uri"/> in the default handler for its scheme, if the scheme is on the
    /// <see cref="Security.ExternalLinks"/> allow-list. Returns whether anything was started.
    /// </summary>
    /// <param name="uri">An address from <see cref="Security.ExternalLinks.TryGetOpenable"/>.</param>
    bool OpenExternal(Uri uri);

    /// <summary>
    /// Starts another instance of the app showing <paramref name="path"/>. Every window is a process of
    /// its own.
    /// </summary>
    /// <param name="path">The document to open.</param>
    void OpenInNewWindow(string path);

    /// <summary>Shows <paramref name="path"/> in the file manager: selected, where the platform can do that, else its folder.</summary>
    /// <param name="path">An existing file.</param>
    void RevealInFileManager(string path);

    /// <summary>
    /// Opens the app's own review page in the Microsoft Store. It is an address the app chose itself, not
    /// one that came from a document, so it is not subject to the link allow-list — which is why this has
    /// its own method that opens <c>ms-windows-store:</c> addresses and nothing else.
    /// </summary>
    /// <param name="review">The review address (<see cref="Services.RatingPrompt.StoreReviewUri"/>).</param>
    bool OpenStoreReview(Uri review);
}
