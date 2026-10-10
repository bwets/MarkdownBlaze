using MarkdownBlaze.Security;

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
internal static class UriScheme
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
    /// <para>
    /// Each segment of the path is percent-escaped, so a <c>#</c> or <c>%</c> in a file name survives the
    /// round trip through <see cref="TryGetFilePath"/> instead of being read as the start of a
    /// fragment or as an escape. The separators and the drive's colon are left as they are, which is
    /// what keeps the footer readable.
    /// </para>
    /// </summary>
    public static string FragmentForFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "#";

        var segments = path.Replace('\\', '/')
            .Split('/')
            .Select(segment => Uri.EscapeDataString(segment).Replace("%3A", ":", StringComparison.Ordinal));
        return "#" + string.Join('/', segments);
    }

    /// <summary>
    /// The file path inside a <c>markdown://</c> URL, or null when the argument is not one — or when it
    /// names a UNC or device path, which a link from somewhere else has no business making this
    /// application open (it would start a network session as the reader). Accepts both the app's own
    /// form and the shorthand another application is likely to produce, where the path lands in the
    /// host (<c>markdown://C:/notes/Dune.md</c>).
    /// </summary>
    public static string? TryGetFilePath(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument)) return null;
        if (!Uri.TryCreate(argument.Trim().Trim('"'), UriKind.Absolute, out var uri)) return null;
        if (!string.Equals(uri.Scheme, Name, StringComparison.OrdinalIgnoreCase)) return null;

        // markdown://document/<path>, or the window's own form where the path rides in the fragment.
        string raw;
        if (!string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
            raw = uri.Host + uri.AbsolutePath; // markdown://C:/notes/Dune.md — the drive parsed as the host
        else if (uri.Fragment.Length > 1)
            raw = uri.Fragment[1..];
        else
            raw = uri.AbsolutePath;

        raw = Uri.UnescapeDataString(raw);
        // One leading slash is the URL's own ("/C:/notes"); a second means a UNC path written with
        // forward slashes, and trimming it away would quietly turn it into a relative one.
        if (LocalPath.IsUncOrDevice(raw) || LocalPath.IsUncOrDevice(raw.TrimStart('/'))) return null;

        raw = raw.TrimStart('/');
        return raw.Length == 0 ? null : raw.Replace('/', Path.DirectorySeparatorChar);
    }
}
