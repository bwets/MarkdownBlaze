using System.Text.Json;

namespace MarkdownBlaze.Services;

/// <summary>
/// Enums that are kept as text: the preferences file and the page both know a view mode as "page", not as
/// a number, so a file written by an earlier version still reads and the page's script needs no table.
/// Reading is forgiving — any capitalisation, and a value nobody knows is the fallback rather than an
/// error — because a preference is not worth a reader's file being thrown away as damaged.
/// </summary>
internal static class EnumText
{
    /// <summary>The member named <paramref name="text"/> (in any capitalisation), or <paramref name="fallback"/>.</summary>
    /// <typeparam name="T">The enum.</typeparam>
    /// <param name="text">What was read; may be null.</param>
    /// <param name="fallback">What an unknown or missing value means.</param>
    public static T Parse<T>(string? text, T fallback)
        where T : struct, Enum
    {
        // Matched by name, not Enum.TryParse: that also takes a number ("1"), which no version ever wrote.
        foreach (var name in Enum.GetNames<T>())
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase)) return Enum.Parse<T>(name);
        }

        return fallback;
    }

    /// <summary>The member's name as it is written down: as declared, or with a lower-case first letter.</summary>
    /// <typeparam name="T">The enum.</typeparam>
    /// <param name="value">The member.</param>
    /// <param name="camelCase">Whether the first letter is lower case ("page" rather than "Page").</param>
    public static string Name<T>(T value, bool camelCase)
        where T : struct, Enum
    {
        var name = value.ToString();
        return camelCase ? JsonNamingPolicy.CamelCase.ConvertName(name) : name;
    }
}
