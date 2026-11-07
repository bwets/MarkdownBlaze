namespace MarkdownBlaze.Services;

/// <summary>
/// A document a host wants shown, which arrives from outside the window: an intent from the file manager, the
/// share sheet, or the host's own chooser.
/// </summary>
internal sealed record IncomingDocument
{
    /// <summary>The file to open, or null for <see cref="Text"/>.</summary>
    public string? Path { get; init; }

    /// <summary>The folder to show as the tree, when the document is in a granted folder; null for a single file, which has no tree.</summary>
    public string? Root { get; init; }

    /// <summary>Markdown that has no file (shared text), rendered the way text dropped on the desktop window is.</summary>
    public string? Text { get; init; }

    /// <summary>What the text is called, for its title.</summary>
    public string? Name { get; init; }

    /// <summary>When the document is a single file whose folder could be granted: how the folder is named in the bar that offers it.</summary>
    public string? FolderLabel { get; init; }
}
