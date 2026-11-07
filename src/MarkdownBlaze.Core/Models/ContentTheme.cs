namespace MarkdownBlaze.Models;

/// <summary>
/// The look of the document itself — paper colour, typeface and spacing. Never the app chrome: the
/// toolbar, sidebar and dialogs keep following the app theme. <paramref name="Id"/> matches a
/// <c>.ct-&lt;id&gt;</c> block in wwwroot/css/content-themes.css; <paramref name="IsDark"/> tells the
/// app whether code blocks and diagrams should be drawn dark.
/// </summary>
internal sealed record ContentTheme(string Id, string Label, bool IsDark, string Description);
