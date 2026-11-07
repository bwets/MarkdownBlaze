using MarkdownBlaze.Models;

namespace MarkdownBlaze.Services;

/// <summary>The content themes offered in Settings and in the print bar.</summary>
internal static class ContentThemes
{
    /// <summary>The document follows the app theme — the app's own look, and the default.</summary>
    public const string Auto = "auto";

    public static readonly IReadOnlyList<ContentTheme> All =
    [
        new(Auto, "Follow app theme", IsDark: false, "The app's own look — light or dark with the appearance above."),
        new("book", "Book", IsDark: false, "Large serif with generous spacing, for reading end to end."),
        new("classic", "Classic", IsDark: false, "Compact serif — the plain printed page."),
        new("elegant", "Elegant", IsDark: false, "Airy serif with wide margins, for something you hand over."),
        new("newspaper", "Newspaper", IsDark: false, "Dense serif on newsprint — fits the most on a sheet."),
        new("academic", "Academic", IsDark: false, "The look of a typeset paper, in the LaTeX typeface."),
        new("report", "Report", IsDark: false, "Clean sans with ruled headings."),
        new("modern", "Modern", IsDark: false, "Roomy sans, the look of a web page."),
        new("simple", "Simple", IsDark: false, "Compact modern sans."),
        new("writer", "Writer", IsDark: false, "Soft grey writing paper, easy on the eyes."),
        new("typewriter", "Typewriter", IsDark: false, "Monospaced draft on off-white paper, roomy for notes."),
        new("night", "Night", IsDark: true, "Light text on a dark page, for reading after dark."),
    ];

    /// <summary>The theme that follows the app, found by its id rather than by its place in the list.</summary>
    private static readonly ContentTheme FollowApp = All.First(t => string.Equals(t.Id, Auto, StringComparison.Ordinal));

    /// <summary>Looks up a theme by id, falling back to <see cref="Auto"/> for unknown/missing ids.</summary>
    public static ContentTheme Resolve(string? id)
    {
        var index = IndexOf(id);
        return index < 0 ? FollowApp : All[index];
    }

    /// <summary>The place of the theme with this id in <see cref="All"/>, or -1 — a loop, because the list is short and F9 walks it.</summary>
    public static int IndexOf(string? id)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i].Id, id, StringComparison.Ordinal)) return i;
        }

        return -1;
    }
}
