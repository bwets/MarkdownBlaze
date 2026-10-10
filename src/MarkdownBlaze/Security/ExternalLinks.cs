using System.Collections.Frozen;

namespace MarkdownBlaze.Security;

/// <summary>
/// What may be handed to the operating system when a link in a document is clicked.
/// <para>
/// A document is not trusted: it can be a download, a colleague's wiki page, anything. Handing its
/// links to <c>ShellExecute</c> unfiltered means <c>file:///C:/x.exe</c>, <c>\\attacker\share\x.lnk</c>,
/// <c>ms-msdt:</c> and <c>search-ms:</c> all run on a click. So only a short list of schemes whose
/// handlers are a browser, a mail client or a dialler gets through; everything else is dropped.
/// </para>
/// <para>
/// The page applies the same list before a link is even reported to .NET, but it does not keep a copy: it
/// is handed this one when it starts (<c>ViewerConfig.LinkSchemes</c>), so the two cannot drift apart.
/// </para>
/// <para>
/// This class only decides and parses; it starts nothing. What the shell is then asked to do is
/// <see cref="Platform.IShellLauncher"/>'s business, and the launcher asks this class again before it does it.
/// </para>
/// </summary>
internal static class ExternalLinks
{
    /// <summary>
    /// The schemes a link may open. <c>ftp</c> is here because the bundled link-test page expects it.
    /// </summary>
    public static readonly FrozenSet<string> AllowedSchemes =
        new[] { "http", "https", "mailto", "tel", "ftp" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The schemes whose address is a host to connect to, so that one without a host is nonsense.</summary>
    private static readonly FrozenSet<string> WebSchemes =
        new[] { "http", "https" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses <paramref name="url"/> and answers whether it may be opened externally. On success
    /// <paramref name="uri"/> is the normalised, escaped form — that, not the raw text, is what should be
    /// handed to the shell.
    /// </summary>
    public static bool TryGetOpenable(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url)) return false;

        // The scheme has to be spelled out in the text. On Windows "C:/x.exe", "//server/share/x" and
        // "\\server\share\x" are all *parsed* as absolute file: URIs, so the parsed scheme alone is not
        // a statement of what the author wrote — and a protocol-relative "//host" is not an http link
        // here either (the page's script rewrites it to https before it ever gets this far).
        var text = url.Trim();
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0) return false;
        var written = text[..colon];
        if (!AllowedSchemes.Contains(written)) return false;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed)) return false;
        if (!AllowedSchemes.Contains(parsed.Scheme)) return false;

        // A web link without a host would be handed to the browser as nonsense.
        if (parsed.Host.Length == 0 && WebSchemes.Contains(parsed.Scheme))
            return false;

        uri = parsed;
        return true;
    }

    public static bool IsAllowed(string? url) => TryGetOpenable(url, out _);
}
