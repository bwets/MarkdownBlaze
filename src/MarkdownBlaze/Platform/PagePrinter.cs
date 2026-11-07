namespace MarkdownBlaze.Platform;

/// <summary>
/// <see cref="IPrinter"/> for a web view that prints its own page (WebView2, WebKitGTK, WKWebView): it
/// prints nothing itself and so leaves the page to call <c>window.print()</c>.
/// </summary>
internal sealed class PagePrinter : IPrinter
{
    /// <inheritdoc />
    public ValueTask<bool> PrintAsync() => ValueTask.FromResult(false);
}
