using MarkdownBlaze.Security;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Interop;

/// <summary>
/// What the page is told when it starts (<c>init(dotNetRef, config)</c> in <c>wwwroot/js/main.js</c>).
/// <para>
/// Each of these used to be written twice, once in C# and once in JavaScript, with a comment asking
/// whoever edited one to edit the other and, for the link schemes, a test that compared the two. Now
/// they are written down here and the page has no copy. Serialised with the JS interop's camelCase
/// naming: <c>zoom.min</c>, <c>linkSchemes</c>, <c>page.widthMm</c>, ...
/// </para>
/// </summary>
internal sealed record ViewerConfig(
    ZoomLimits Zoom,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> LinkSchemes,
    SheetPage Page,
    IReadOnlyList<string> Shortcuts)
{
    /// <summary>
    /// The configuration for a page whose keyboard commands answer to <paramref name="shortcuts"/>: the key
    /// combinations the command registry knows, in the form the page spells them ("ctrl+shift+o").
    /// </summary>
    public static ViewerConfig Create(IEnumerable<string> shortcuts) => new(
        ZoomLimits.Default,
        MarkdownService.SupportedExtensions,
        [.. ExternalLinks.AllowedSchemes.Order(StringComparer.Ordinal)],
        SheetPage.A4,
        [.. shortcuts]);
}
