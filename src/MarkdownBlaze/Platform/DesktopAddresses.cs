using MarkdownBlaze.Services;

namespace MarkdownBlaze.Platform;

/// <summary>
/// The addresses of the desktop window: the UI is served from <c>markdown://document/</c> and the pictures
/// of the open document from <c>mdasset://doc/&lt;token&gt;/…</c>, two schemes of the app's own that the
/// window registers (see <c>Program</c>).
/// </summary>
internal static class DesktopAddresses
{
    /// <summary>The scheme the pictures are served under; also what the page's Content-Security-Policy names in <c>img-src</c>.</summary>
    public const string AssetScheme = "mdasset";

    /// <summary>The origin the Blazor UI is served from.</summary>
    public static readonly Uri AppBase = new($"{UriScheme.Name}://{UriScheme.Host}/");

    /// <summary>The addresses to give the services.</summary>
    public static readonly HostAddresses Addresses = new(AppBase, AssetScheme + "://doc/");
}
