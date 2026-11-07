using MarkdownBlaze.Services;

namespace MarkdownBlaze.Platform;

/// <summary>
/// The addresses of the Android window. <c>BlazorWebView</c> serves the UI from <c>https://0.0.0.1/</c> and the
/// WebView has no way to register a scheme of its own, so the pictures of the open document are served from a
/// path of the same origin, <c>https://0.0.0.1/_mdasset/&lt;token&gt;/…</c>, which <see cref="WebViewPolicy"/>
/// answers from <c>AssetHandler</c>.
/// </summary>
internal static class AndroidAddresses
{
    /// <summary>The origin the Blazor UI is served from (the one <c>BlazorWebView</c> uses).</summary>
    public static readonly Uri AppBase = new("https://0.0.0.1/");

    /// <summary>The addresses to give the services.</summary>
    public static readonly HostAddresses Addresses = new(AppBase, "https://0.0.0.1/_mdasset/");
}
