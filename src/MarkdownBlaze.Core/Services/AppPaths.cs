namespace MarkdownBlaze.Services;

/// <summary>
/// Where MarkdownBlaze keeps its own files. The preferences, the history and the log all live under
/// one folder; this is the only place that works out which. By default that is a folder in the user's
/// local app data; a host that has a place of its own for an app's files (Android's
/// <c>FileSystem.AppDataDirectory</c>) names it once, at start-up, with <see cref="Configure"/>.
/// <para>
/// The folder is a static setting, not a service: the stores read it where they are made and the log
/// provider before any container exists, so passing it through the container would reach every
/// constructor for a value that is fixed for the life of the process.
/// </para>
/// </summary>
internal static class AppPaths
{
    /// <summary><c>%LOCALAPPDATA%\MarkdownBlaze</c> (the platform's equivalent elsewhere), unless a host has named another.</summary>
    public static string DataFolder { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MarkdownBlaze");

    /// <summary>The folder the rolling log files are written to.</summary>
    public static string LogFolder => Path.Combine(DataFolder, "logs");

    /// <summary>
    /// The folder the bundled documents (<c>UserManual.md</c>, <c>WhatsNew.md</c>, <c>THIRD-PARTY-NOTICES.md</c>) are read
    /// from: next to the executable, where the desktop build and its installers put them, unless a host that
    /// cannot ship loose files has named another with <see cref="ConfigureBundledFolder"/>.
    /// </summary>
    public static string BundledFolder { get; private set; } = AppContext.BaseDirectory;

    /// <summary>
    /// Names the folder the app keeps its files in. Call it first thing in the host, before anything reads
    /// <see cref="DataFolder"/>: a store that has been made keeps the folder it was made with.
    /// </summary>
    /// <param name="dataFolder">An absolute path; the folder is created by whoever writes to it.</param>
    /// <exception cref="ArgumentException">The path is empty or not absolute.</exception>
    public static void Configure(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        if (!Path.IsPathFullyQualified(dataFolder)) throw new ArgumentException("The data folder must be an absolute path.", nameof(dataFolder));

        DataFolder = dataFolder;
    }

    /// <summary>
    /// Names the folder the bundled documents are in, for a host whose package keeps them where a path cannot reach
    /// (an Android APK holds them as assets, which the host copies to a folder of its own at start-up).
    /// </summary>
    /// <param name="bundledFolder">An absolute path to a folder that holds the documents.</param>
    /// <exception cref="ArgumentException">The path is empty or not absolute.</exception>
    public static void ConfigureBundledFolder(string bundledFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundledFolder);
        if (!Path.IsPathFullyQualified(bundledFolder))
            throw new ArgumentException("The folder of the bundled documents must be an absolute path.", nameof(bundledFolder));

        BundledFolder = bundledFolder;
    }
}
