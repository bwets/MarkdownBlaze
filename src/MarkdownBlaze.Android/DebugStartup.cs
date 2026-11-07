namespace MarkdownBlaze;

/// <summary>
/// What a debug build can be told to open when it is started (the <c>mb_open</c> extra of the launch intent, see
/// <see cref="MainActivity"/>). A release build has no such door: the launcher starts it with no arguments and
/// it shows the manual.
/// </summary>
internal static class DebugStartup
{
    /// <summary>True in a debug build.</summary>
    public static bool IsDebugBuild =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>The path a debug build was asked to open; always null in a release build.</summary>
    public static string? Open { get; set; }

    /// <summary>The command line the services see: the path, if there is one.</summary>
    public static string[] Arguments => string.IsNullOrEmpty(Open) ? [] : [Open];
}
