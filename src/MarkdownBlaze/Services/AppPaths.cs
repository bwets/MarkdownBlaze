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
}
