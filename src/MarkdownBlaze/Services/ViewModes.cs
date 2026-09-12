namespace MarkdownBlaze.Services;

/// <summary>
/// How the document is laid out on screen.
/// <para>
/// <b>Continuous</b> is the reading view: one column that scrolls without end, which is what a
/// document on a screen wants to be. <b>Page</b> lays the same document onto sheets of paper, with
/// the margins and breaks it will have when printed — the view to check a document in before sending
/// it, and the one people expect from a PDF reader.
/// </para>
/// </summary>
public static class ViewModes
{
    public const string Continuous = "continuous";
    public const string Page = "page";

    /// <summary>Sheet as wide as the pane — reading size.</summary>
    public const string FitWidth = "width";

    /// <summary>Whole sheet visible at once — the layout at a glance.</summary>
    public const string FitPage = "page";

    public static string ResolveMode(string? value) =>
        string.Equals(value, Page, StringComparison.OrdinalIgnoreCase) ? Page : Continuous;

    public static string ResolveFit(string? value) =>
        string.Equals(value, FitPage, StringComparison.OrdinalIgnoreCase) ? FitPage : FitWidth;
}
