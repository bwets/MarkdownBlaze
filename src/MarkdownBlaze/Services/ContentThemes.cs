    namespace MarkdownBlaze.Services;

/// <summary>
/// The look of the document itself — paper colour, typeface and spacing. Never the app chrome: the
/// toolbar, sidebar and dialogs keep following the app theme. <paramref name="Id"/> matches a
/// <c>.ct-&lt;id&gt;</c> block in wwwroot/lib/content-themes.css; <paramref name="IsDark"/> tells the
/// app whether code blocks and diagrams should be drawn dark.
/// </summary>
public sealed record ContentTheme(string Id, string Label, bool IsDark, string Description);

/// <summary>The content themes offered in Settings and in the print bar.</summary>
public static class ContentThemes
{
    /// <summary>The document follows the app theme — the app's own look, and the default.</summary>
    public const string Auto = "auto";

    public static readonly IReadOnlyList<ContentTheme> All =
    [
        new(Auto, "Follow app theme", false, "The app's own look — light or dark with the appearance above."),
        new("book", "Book", false, "Large serif with generous spacing, for reading end to end."),
        new("classic", "Classic", false, "Compact serif — the plain printed page."),
        new("elegant", "Elegant", false, "Airy serif with wide margins, for something you hand over."),
        new("newspaper", "Newspaper", false, "Dense serif on newsprint — fits the most on a sheet."),
        new("academic", "Academic", false, "The look of a typeset paper, in the LaTeX typeface."),
        new("report", "Report", false, "Clean sans with ruled headings."),
        new("modern", "Modern", false, "Roomy sans, the look of a web page."),
        new("simple", "Simple", false, "Compact modern sans."),
        new("writer", "Writer", false, "Soft grey writing paper, easy on the eyes."),
        new("typewriter", "Typewriter", false, "Monospaced draft on off-white paper, roomy for notes."),
        new("night", "Night", true, "Light text on a dark page, for reading after dark."),
    ];

    /// <summary>Looks up a theme by id, falling back to <see cref="Auto"/> for unknown/missing ids.</summary>
    public static ContentTheme Resolve(string? id) =>
        All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
