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

    /// <summary>The document nests Markdown more deeply than the renderer follows (see <c>MarkdownService.MaxMarkdownNesting</c>).</summary>
    TooDeep = 4,

    /// <summary>The render took longer than the deadline (see <c>NavigationService.RenderDeadline</c>) and was given up on.</summary>
    TimedOut = 5,

    /// <summary>Not rendered: renders that were given up on earlier are still running (see <c>NavigationService.MaxAbandonedRenders</c>).</summary>
    Busy = 6,
}
