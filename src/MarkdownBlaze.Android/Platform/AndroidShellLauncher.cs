using MarkdownBlaze.Security;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Platform;

/// <summary>
/// <see cref="IShellLauncher"/> for Android: a link goes to whatever app handles its scheme (the browser, the mail
/// app, the dialler) through MAUI's <c>Launcher</c>, and only if <see cref="ExternalLinks"/> allows the address.
/// The app has one window and no file manager of its own to show a file in, so the rest of the interface does nothing.
/// </summary>
internal sealed partial class AndroidShellLauncher : IShellLauncher
{
    private static readonly ILogger Logger = Log.For(nameof(AndroidShellLauncher));

    /// <inheritdoc />
    public bool OpenExternal(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        // The callers have checked already; this is the last step before the system, so it is checked again.
        var address = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString;
        if (!ExternalLinks.TryGetOpenable(address, out var openable))
        {
            LogRefused(Logger, uri.IsAbsoluteUri ? uri.Scheme : "(relative)");
            return false;
        }

        // The page's thread is not waiting for the other app to start; a failure is logged, not thrown at it.
        _ = LaunchAsync(openable);
        return true;
    }

    /// <inheritdoc />
    public void OpenInNewWindow(string path)
    {
        // One window: the document is not opened anywhere else.
    }

    /// <inheritdoc />
    public void RevealInFileManager(string path)
    {
        // Android has no file manager that can be told to select a file.
    }

    /// <inheritdoc />
    public bool OpenStoreReview(Uri review) => false;

    /// <inheritdoc />
    public bool CanOpenInNewWindow => false;

    /// <inheritdoc />
    public bool CanRevealInFileManager => false;

    private static async Task LaunchAsync(Uri uri)
    {
        try
        {
            _ = await Launcher.Default.OpenAsync(uri).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException
            or Android.Content.ActivityNotFoundException
            or InvalidOperationException)
        {
            LogLaunchFailed(Logger, ex, uri.Scheme);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused to open a {Scheme} address")]
    private static partial void LogRefused(ILogger log, string scheme);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open a {Scheme} address")]
    private static partial void LogLaunchFailed(ILogger log, Exception ex, string scheme);
}
