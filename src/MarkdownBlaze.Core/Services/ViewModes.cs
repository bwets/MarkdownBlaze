using MarkdownBlaze.Models;

namespace MarkdownBlaze.Services;

/// <summary>
/// Turning the layout enums (<see cref="ViewMode"/>, <see cref="PageFit"/>) into the text the page knows
/// them by and back, with the fallback for a value nobody recognises.
/// </summary>
internal static class ViewModes
{
    /// <summary>The view mode a name stands for, in any capitalisation; continuous for one nobody knows.</summary>
    /// <param name="value">"continuous", "page", or whatever the file or the page held.</param>
    public static ViewMode ResolveMode(string? value) => EnumText.Parse(value, ViewMode.Continuous);

    /// <summary>The page fit a name stands for, in any capitalisation; width for one nobody knows.</summary>
    /// <param name="value">"width", "page", or whatever the file or the page held.</param>
    public static PageFit ResolveFit(string? value) => EnumText.Parse(value, PageFit.Width);

    /// <summary>The name the page's script and the preferences file use: "continuous" or "page".</summary>
    public static string Name(ViewMode mode) => EnumText.Name(mode, camelCase: true);

    /// <summary>The name the page's script and the preferences file use: "width" or "page".</summary>
    public static string Name(PageFit fit) => EnumText.Name(fit, camelCase: true);
}
