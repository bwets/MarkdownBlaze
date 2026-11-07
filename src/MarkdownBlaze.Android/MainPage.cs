using MarkdownBlaze.Platform;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace MarkdownBlaze;

/// <summary>
/// The page: a <c>BlazorWebView</c> showing <see cref="Components.App"/>, with the WebView held to its
/// <see cref="WebViewPolicy"/>. The page's host page is <c>wwwroot/index.html</c>, the counterpart of the desktop
/// host's.
/// </summary>
internal sealed class MainPage : ContentPage
{
    /// <summary>Creates the page.</summary>
    /// <param name="policy">What the WebView may do.</param>
    /// <param name="printer">Told which WebView this is, to print it.</param>
    public MainPage(WebViewPolicy policy, AndroidPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(printer);

        var view = new BlazorWebView { HostPage = "wwwroot/index.html" };
        view.RootComponents.Add(new RootComponent { Selector = "app", ComponentType = typeof(Components.App) });

        view.BlazorWebViewInitialized += (_, e) =>
        {
            WebViewPolicy.Lock(e.WebView);
            printer.Attach(e.WebView);
            UseSplashColour(e.WebView);
        };
        view.UrlLoading += (_, e) => policy.OnUrlLoading(e);
        view.WebResourceRequested += (_, e) => policy.OnResourceRequested(e);

        Content = view;
    }

    /// <summary>
    /// Paints the WebView in the splash screen's colour (white, or the app's dark grey in a dark system) until the page paints its own, so
    /// the cold start does not flash white between the splash and the first frame of a dark app.
    /// </summary>
    internal static void UseSplashColour(Android.Webkit.WebView webView)
    {
        var context = webView.Context;
        var id = context?.Resources?.GetIdentifier("maui_splash_color", "color", context.PackageName) ?? 0;
        if (id != 0) webView.SetBackgroundColor(new Android.Graphics.Color(AndroidX.Core.Content.ContextCompat.GetColor(context, id)));
    }
}
