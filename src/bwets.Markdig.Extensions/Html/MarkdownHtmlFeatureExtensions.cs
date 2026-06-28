namespace bwets.Markdig.Extensions.Html;

/// <summary>Activation methods for the bundled HTML features.</summary>
public static class MarkdownHtmlFeatureExtensions
{
    public static MarkdownHtmlFeatures UseAdmonitions(this MarkdownHtmlFeatures features) =>
        features.Add(new AdmonitionFeature());

    public static MarkdownHtmlFeatures UseMermaid(this MarkdownHtmlFeatures features) =>
        features.Add(new MermaidFeature());

    /// <param name="extendedLanguages">
    /// false bundles the common ~38 languages (small); true bundles all ~190 languages plus Razor.
    /// </param>
    public static MarkdownHtmlFeatures UseSyntaxHighlighting(this MarkdownHtmlFeatures features, bool extendedLanguages = false) =>
        features.Add(new SyntaxHighlightingFeature(extendedLanguages));
}
