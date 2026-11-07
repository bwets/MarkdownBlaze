namespace MarkdownBlaze.Platform;

/// <summary>
/// The "open a file" and "open a folder" choosers of the host. The desktop shows the operating system's own
/// dialogs; another host answers with whatever its platform has. Both answer a path the rest of the app can
/// open, or null when the reader chose nothing (or the host has no chooser yet).
/// </summary>
internal interface IFilePicker
{
    /// <summary>Asks the reader for a Markdown file (any file, if they choose to see all of them).</summary>
    /// <param name="startDir">The folder to start in, if there is a sensible one.</param>
    Task<string?> PickFileAsync(string? startDir);

    /// <summary>Asks the reader for a folder.</summary>
    /// <param name="startDir">The folder to start in, if there is a sensible one.</param>
    Task<string?> PickFolderAsync(string? startDir);
}
