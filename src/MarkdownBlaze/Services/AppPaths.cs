namespace MarkdownBlaze.Services;

/// <summary>
/// Where MarkdownBlaze keeps its own files. The preferences, the history and the log all live under
/// one folder in the user's local app data; this is the only place that works out which.
/// </summary>
internal static class AppPaths
{
    /// <summary><c>%LOCALAPPDATA%\MarkdownBlaze</c> (the platform's equivalent elsewhere).</summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MarkdownBlaze");

    /// <summary>The folder the rolling log files are written to.</summary>
    public static string LogFolder { get; } = Path.Combine(DataFolder, "logs");

    /// <summary>
    /// The web view's own user data folder (Windows / WebView2): what the browser engine inside the window
    /// keeps — its cache, cookies and local storage. PhotinoX would put it in <c>%LOCALAPPDATA%\Photino</c>,
    /// which every PhotinoX application on the machine shares, so a picture fetched from the web for one
    /// document would sit in a cache that other programs can read and this app cannot clear. Under the
    /// app's own folder it is deleted with it, like everything else the app stores.
    /// </summary>
    public static string WebViewFolder { get; } = Path.Combine(DataFolder, "webview");
}
