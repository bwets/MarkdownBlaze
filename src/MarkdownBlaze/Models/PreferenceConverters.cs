using System.Text.Json;
using System.Text.Json.Serialization;
using MarkdownBlaze.Services;
using Microsoft.FluentUI.AspNetCore.Components;

namespace MarkdownBlaze.Models;

/// <summary>
/// How the enum preferences are written in <c>preferences.json</c>. They are the same strings the file
/// has always held — <c>"ThemeMode": "Dark"</c>, <c>"ViewMode": "page"</c>, <c>"PageFit": "width"</c> —
/// so a file written by an earlier version reads as it did, and one written now can be read by it. A
/// value that is not one of the names (an edited file, a newer version's) is the default of the setting,
/// not a damaged file.
/// </summary>
internal static class PreferenceConverters
{
    /// <summary>Reads and writes an enum as text, by the two functions it is given.</summary>
    /// <typeparam name="T">The enum.</typeparam>
    internal abstract class EnumTextConverter<T>(Func<string?, T> parse, Func<T, string> name) : JsonConverter<T>
        where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            reader.Skip(); // a number, an object, null: not a name, and the reader must still end up after it
            return parse(text);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(name(value));
    }

    /// <summary>"System", "Light", "Dark".</summary>
    internal sealed class ThemeModeConverter()
        : EnumTextConverter<ThemeMode>(text => EnumText.Parse(text, ThemeMode.System), mode => EnumText.Name(mode, camelCase: false));

    /// <summary>"continuous", "page".</summary>
    internal sealed class ViewModeConverter() : EnumTextConverter<ViewMode>(ViewModes.ResolveMode, ViewModes.Name);

    /// <summary>"width", "page".</summary>
    internal sealed class PageFitConverter() : EnumTextConverter<PageFit>(ViewModes.ResolveFit, ViewModes.Name);
}
