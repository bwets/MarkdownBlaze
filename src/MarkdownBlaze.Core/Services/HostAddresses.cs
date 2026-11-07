namespace MarkdownBlaze.Services;

/// <summary>
/// The two addresses that depend on the host the page is shown in: the origin the UI is served from, and
/// where the open document's pictures are fetched. The renderer writes the second into <c>&lt;img src&gt;</c>,
/// the image handler recognises it, and the sanitizer refuses an author's own address that would land on it,
/// so all three ask this one object instead of knowing a scheme.
/// <para>
/// The desktop window serves the UI from <c>markdown://document/</c> and the pictures from
/// <c>mdasset://doc/&lt;token&gt;/…</c>, both schemes of its own. A host that cannot register schemes
/// (Android's <c>BlazorWebView</c> serves from <c>https://0.0.0.1/</c>) serves the pictures from a path of
/// the same origin instead, <c>https://0.0.0.1/_mdasset/&lt;token&gt;/…</c>, and says so here.
/// </para>
/// </summary>
internal sealed class HostAddresses
{
    /// <summary>The origin the UI is served from; always ends in a slash.</summary>
    public Uri AppBase { get; }

    /// <summary>
    /// What every picture address of the open document starts with, ending in a slash:
    /// <c>mdasset://doc/</c> on the desktop. The render's token follows it.
    /// </summary>
    public string AssetPrefix { get; }

    /// <summary>Creates the addresses of a host.</summary>
    /// <param name="appBase">The origin the UI is served from.</param>
    /// <param name="assetPrefix">The absolute address, ending in <c>/</c>, that the pictures of a document are served under.</param>
    /// <exception cref="ArgumentException">An address is not absolute, or the prefix does not end in a slash.</exception>
    public HostAddresses(Uri appBase, string assetPrefix)
    {
        ArgumentNullException.ThrowIfNull(appBase);
        ArgumentException.ThrowIfNullOrEmpty(assetPrefix);
        if (!appBase.IsAbsoluteUri) throw new ArgumentException("The app's address must be absolute.", nameof(appBase));
        if (!Uri.TryCreate(assetPrefix, UriKind.Absolute, out _) || !assetPrefix.EndsWith('/'))
            throw new ArgumentException("The picture prefix must be an absolute address ending in a slash.", nameof(assetPrefix));

        AppBase = appBase;
        AssetPrefix = assetPrefix;
    }

    /// <summary>The address of an image of the render with <paramref name="token"/>, for the path the document wrote.</summary>
    /// <param name="token">The token of the render (see <see cref="AssetGrant"/>).</param>
    /// <param name="decodedPath">The image path as the document wrote it, percent-decoded.</param>
    public string AssetUrlFor(string token, string decodedPath) =>
        // One escaped segment, "/" included. A browser resolves "." and ".." segments in a URL before it
        // makes the request, so a path left as segments (../images/a.png) would arrive with the token
        // chopped off; written as one segment it arrives as written.
        AssetPrefix + token + "/" + Uri.EscapeDataString(decodedPath);

    /// <summary>
    /// Whether <paramref name="address"/>, as the page would resolve it against <see cref="AppBase"/>, lands
    /// under <see cref="AssetPrefix"/>. Written the way a browser reads it: a relative form
    /// (<c>/_mdasset/…</c>), a scheme-relative one, upper case and <c>a/../</c> segments all count, and so
    /// do the tabs and line breaks a browser deletes from an address before it reads it.
    /// </summary>
    /// <param name="address">The value of a <c>src</c> or <c>href</c> an author wrote.</param>
    public bool IsAssetAddress(string? address)
    {
        if (string.IsNullOrEmpty(address)) return false;

        var compact = string.Concat(address.Where(c => !char.IsControl(c) && c != ' '));
        return Uri.TryCreate(AppBase, compact, out var resolved)
            && resolved.AbsoluteUri.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
