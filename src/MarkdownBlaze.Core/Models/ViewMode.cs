namespace MarkdownBlaze.Models;

/// <summary>
/// How the document is laid out on screen.
/// <para>
/// <b>Continuous</b> is the reading view: one column that scrolls without end, which is what a
/// document on a screen wants to be. <b>Page</b> lays the same document onto sheets of paper, with
/// the margins and breaks it will have when printed — the view to check a document in before sending
/// it, and the one people expect from a PDF reader.
/// </para>
/// Kept in the preferences file, and handed to the page, as the lower-case name ("continuous", "page").
/// </summary>
internal enum ViewMode
{
    Continuous = 0,
    Page = 1,
}
