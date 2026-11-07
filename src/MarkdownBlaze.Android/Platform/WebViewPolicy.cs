using Android.Webkit;
using MarkdownBlaze.Security;
using MarkdownBlaze.Services;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.Logging;
using AndroidWebView = Android.Webkit.WebView;

namespace MarkdownBlaze.Platform;

/// <summary>
/// What the Android WebView inside the window is, and is not, allowed to do: the counterpart of the desktop
/// host's <c>ConfigureWebView</c>. The page shows documents nobody vetted, so the engine is kept to the one thing
/// it is here for — the app's own page — and the things the page needs from outside it are answered here:
/// <list type="bullet">
/// <item><description>a picture request under <c>https://0.0.0.1/_mdasset/</c> goes to <see cref="AssetHandler"/>
/// (<see cref="OnResourceRequested"/>);</description></item>
/// <item><description>a navigation away from the app is cancelled, and a web address that
/// <see cref="ExternalLinks"/> allows goes to the browser instead (<see cref="OnUrlLoading"/>);</description></item>
/// <item><description>the engine's settings and its permission prompts are fixed (<see cref="Lock"/>).</description></item>
/// </list>
/// </summary>
internal sealed partial class WebViewPolicy
{
    private const int StatusOk = 200;
    private const int StatusNotFound = 404;

    private static readonly ILogger Logger = Log.For(nameof(WebViewPolicy));

    /// <summary>One object for every WebView: the app has one, and the engine keeps what it is handed.</summary>
    private static readonly LockedChromeClient ChromeClient = new();
    private readonly HostAddresses _addresses;
    private readonly AssetHandler _assets;
    private readonly IShellLauncher _shell;

    /// <summary>Creates the policy.</summary>
    /// <param name="addresses">The app's origin and the prefix its pictures are served under.</param>
    /// <param name="assets">Decides which picture requests are answered, and with what.</param>
    /// <param name="shell">Opens the web addresses that leave the app.</param>
    public WebViewPolicy(HostAddresses addresses, AssetHandler assets, IShellLauncher shell)
    {
        _addresses = addresses;
        _assets = assets;
        _shell = shell;
    }

    /// <summary>
    /// Answers a request the page makes for one of the open document's pictures. Every other request is left to
    /// <c>BlazorWebView</c> (its own page, scripts and styles) or to the network (a picture from the web).
    /// What <see cref="AssetHandler"/> refuses — a token that is not the render on the page, a path outside the
    /// document's folder, a file that is not a picture — is a 404 with no body, as it is on the desktop.
    /// </summary>
    /// <param name="e">The request; the answer is set on it.</param>
    public void OnResourceRequested(WebViewWebResourceRequestedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!_addresses.IsAssetAddress(e.Uri.AbsoluteUri)) return;

        // Anything under the prefix is the app's, whatever the method or the address looks like: it never goes on to the network.
        e.Handled = true;
        string? contentType = null;
        var served = string.Equals(e.Method, "GET", StringComparison.OrdinalIgnoreCase)
            ? _assets.Serve(e.Uri.GetLeftPart(UriPartial.Path), out contentType)
            : null;
        if (served is null)
        {
            e.SetResponse(StatusNotFound, "Not Found", Headers("text/plain"), new MemoryStream());
            return;
        }

        e.SetResponse(StatusOk, "OK", Headers(contentType ?? "application/octet-stream"), served);
    }

    /// <summary>
    /// Decides what a navigation of the WebView does. Nothing the page does may take the window anywhere but
    /// the app: a link a document slipped past the sanitizer, a script, a redirect. The page's own address
    /// stays in the WebView; everything else is cancelled, and an address that <see cref="ExternalLinks"/>
    /// allows (a web link, a mail address, a phone number) is handed to the browser — the same list, and the
    /// same launcher, as a click the page reports itself.
    /// </summary>
    /// <param name="e">The navigation; its strategy is set.</param>
    public void OnUrlLoading(UrlLoadingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (IsAppAddress(e.Url))
        {
            e.UrlLoadingStrategy = UrlLoadingStrategy.OpenInWebView;
            return;
        }

        e.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
        if (ExternalLinks.TryGetOpenable(e.Url.OriginalString, out var uri))
            _ = _shell.OpenExternal(uri);
        else
            LogNavigationRefused(Logger, e.Url.IsAbsoluteUri ? e.Url.Scheme : "(relative)");
    }

    /// <summary>
    /// Fixes the engine's settings, once it exists and before any component has been rendered. Page zoom,
    /// file and content access, geolocation and extra windows are off, and every request the page makes of the
    /// engine's permission system — camera, microphone, location, files — is refused.
    /// </summary>
    /// <param name="webView">The WebView <c>BlazorWebView</c> made.</param>
    public static void Lock(AndroidWebView webView)
    {
        ArgumentNullException.ThrowIfNull(webView);

        var settings = webView.Settings;

        // The page can reach nothing on the device's storage, by file:// or content://: the document's pictures
        // are served through the app's own origin, not read by the engine.
        settings.AllowFileAccess = false;
        settings.AllowContentAccess = false;

        // These two are deprecated from Android 11 on, where they are off for good; on the older releases the app
        // runs on (8 to 10) they are off unless an app turns them on, and it is said here that this one does not.
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            settings.AllowFileAccessFromFileURLs = false;
            settings.AllowUniversalAccessFromFileURLs = false;
        }

        settings.SetGeolocationEnabled(flag: false);
        settings.SetSupportMultipleWindows(support: false);
        settings.JavaScriptCanOpenWindowsAutomatically = false;

        // The WebView's own zoom scales the whole page — toolbar, sidebar and all — which is not what pinching
        // in a document viewer should do. The page zooms the document (js/zoom.js) instead.
        settings.SetSupportZoom(support: false);
        settings.BuiltInZoomControls = false;
        settings.DisplayZoomControls = false;

        // The page is https (BlazorWebView serves it from https://0.0.0.1/), and the desktop shows a picture from a plain
        // http address (its CSP allows img-src http:). CompatibilityMode is the mode that lets passive content such as an
        // image onto an https page while scripts, frames and other active content stay blocked (and the sanitizer and the
        // page's CSP removed those long before this setting could matter), so a document should look the same here as on
        // the desktop. Checked on the API 36 emulator (WebView 133): Chromium still refuses the image in this mode, and in
        // AlwaysAllow too, so a plain http picture does not show yet; the setting is the right one for when it does.
        settings.MixedContentMode = MixedContentHandling.CompatibilityMode;

        webView.SetWebChromeClient(ChromeClient);
    }

    private bool IsAppAddress(Uri address) =>
        address.IsAbsoluteUri
            && string.Equals(address.Scheme, _addresses.AppBase.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(address.Host, _addresses.AppBase.Host, StringComparison.OrdinalIgnoreCase)
            && !_addresses.IsAssetAddress(address.AbsoluteUri);

    private static Dictionary<string, string> Headers(string contentType) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Content-Type"] = contentType,
        ["Cache-Control"] = "no-store",
        ["X-Content-Type-Options"] = "nosniff",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancelled a navigation to a {Scheme} address")]
    private static partial void LogNavigationRefused(ILogger log, string scheme);

    /// <summary>
    /// The engine's host-side callbacks, with every one that would let the page ask the reader for something
    /// answered "no": a permission prompt, the location prompt, a new window, a file chooser and the script
    /// dialogs (a hostile script could stack <c>alert</c>s over the app). Nothing the viewer shows needs any of them.
    /// </summary>
    private sealed class LockedChromeClient : WebChromeClient
    {
        public override void OnPermissionRequest(PermissionRequest? request) => request?.Deny();

        public override void OnGeolocationPermissionsShowPrompt(string? origin, GeolocationPermissions.ICallback? callback) =>
            callback?.Invoke(origin, allow: false, retain: false);

        public override bool OnCreateWindow(AndroidWebView? view, bool isDialog, bool isUserGesture, Android.OS.Message? resultMsg) => false;

        public override bool OnShowFileChooser(AndroidWebView? webView, IValueCallback? filePathCallback, FileChooserParams? fileChooserParams)
        {
            filePathCallback?.OnReceiveValue(value: null);
            return true;
        }

        public override bool OnJsAlert(AndroidWebView? view, string? url, string? message, JsResult? result)
        {
            result?.Cancel();
            return true;
        }

        public override bool OnJsConfirm(AndroidWebView? view, string? url, string? message, JsResult? result)
        {
            result?.Cancel();
            return true;
        }

        public override bool OnJsPrompt(AndroidWebView? view, string? url, string? message, string? defaultValue, JsPromptResult? result)
        {
            result?.Cancel();
            return true;
        }
    }
}
