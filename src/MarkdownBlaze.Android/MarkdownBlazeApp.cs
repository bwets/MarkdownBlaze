using MarkdownBlaze.Platform;

namespace MarkdownBlaze;

/// <summary>The MAUI application: one window with one page, <see cref="MainPage"/>.</summary>
internal sealed class MarkdownBlazeApp : Application
{
    private readonly WebViewPolicy _policy;
    private readonly AndroidPrinter _printer;

    /// <summary>Created by MAUI, which supplies the policy from the container.</summary>
    /// <param name="policy">What the page's WebView may do.</param>
    /// <param name="printer">Told which WebView the page is, to print it.</param>
    public MarkdownBlazeApp(WebViewPolicy policy, AndroidPrinter printer)
    {
        _policy = policy;
        _printer = printer;
    }

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState) => new(new MainPage(_policy, _printer)) { Title = "MarkdownBlaze" };
}
