namespace MarkdownBlaze.Models;

/// <summary>
/// In page view, what a sheet is sized against. Kept in the preferences file, and handed to the page, as
/// the lower-case name ("width", "page").
/// </summary>
internal enum PageFit
{
    /// <summary>Sheet as wide as the pane — reading size.</summary>
    Width = 0,

    /// <summary>Whole sheet visible at once — the layout at a glance.</summary>
    Page = 1,
}
