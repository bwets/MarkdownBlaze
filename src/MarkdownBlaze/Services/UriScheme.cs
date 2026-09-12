namespace MarkdownBlaze.Services;

/// <summary>
/// The <c>markdown://</c> URI scheme the app owns: it is both the origin the WebView serves the UI
/// from and the address other applications can use to open a document in MarkdownBlaze.
/// <para>
/// A URL is <c>markdown://document/&lt;path&gt;</c>. The <c>document</c> host looks redundant but is
/// not optional: everything before the first single slash is the WebView's origin, fixed for the
/// lifetime of the window, so only the path can follow the document being read.
/// </para>
/// </summary>
public static class UriScheme
{
    public const string Name = "markdown";
    private const string Host = "document";

    /// <summary>The origin the Blazor UI is served from.</summary>
    public static readonly Uri AppBase = new($"{Name}://{Host}/");

    /// <summary>
    /// The address bar fragment for a document, e.g. <c>#C:/notes/Dune.md</c>, which makes the window's
    /// URL <c>markdown://document/#C:/notes/Dune.md</c> — and that URL is what the browser prints in
    /// the page footer.
    /// <para>
    /// It goes in the fragment rather than the path for a concrete reason: PhotinoX serves the host
    /// page for an unknown URL only when it has no file extension, so a reload of
    /// <c>markdown://document/C:/notes/Dune.md</c> would 404 into a blank window. A fragment never
    /// reaches the resolver at all.
    /// </para>
    /// </summary>
    public static string FragmentForFile(string? path) =>
        string.IsNullOrEmpty(path) ? "#" : "#" + path.Replace('\\', '/');

    /// <summary>
    /// The file path inside a <c>markdown://</c> URL, or null when the argument is not one. Accepts
    /// both the app's own form and the shorthand another application is likely to produce, where the
    /// path lands in the host (<c>markdown://C:/notes/Dune.md</c>).
    /// </summary>
    public static string? TryGetFilePath(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument)) return null;
        if (!Uri.TryCreate(argument.Trim().Trim('"'), UriKind.Absolute, out var uri)) return null;
        if (!string.Equals(uri.Scheme, Name, StringComparison.OrdinalIgnoreCase)) return null;

        // markdown://document/<path>, or the window's own form where the path rides in the fragment.
        var raw = string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)
            ? (uri.Fragment.Length > 1 ? uri.Fragment[1..] : uri.AbsolutePath)
            : uri.Host + uri.AbsolutePath; // markdown://C:/notes/Dune.md — the drive parsed as the host

        raw = Uri.UnescapeDataString(raw).TrimStart('/');
        return raw.Length == 0 ? null : raw.Replace('/', Path.DirectorySeparatorChar);
    }
}
