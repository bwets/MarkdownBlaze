namespace MarkdownBlaze.Models;

/// <summary>What became of an attempt to read and render a file.</summary>
internal enum RenderStatus
{
    /// <summary>Read and rendered.</summary>
    Rendered = 0,

    /// <summary>There is no such file (any more) — an editor that saves by replacing the file shows this for a moment.</summary>
    Missing = 1,

    /// <summary>The file exists but could not be read: locked, no permission, not really a file.</summary>
    Unreadable = 2,

    /// <summary>The file is larger than the viewer renders (see <c>MarkdownService.MaxFileBytes</c>).</summary>
    TooLarge = 3,
}
