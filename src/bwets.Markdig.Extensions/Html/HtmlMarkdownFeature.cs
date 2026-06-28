using Markdig;

namespace bwets.Markdig.Extensions.Html;

/// <summary>
/// A self-contained Markdown HTML feature: optional Markdig pipeline configuration plus the
/// client-side assets (CSS/JS) and HTML fragments needed to present it in an HTML host.
/// </summary>
public abstract class HtmlMarkdownFeature
{
    /// <summary>Configures the Markdig pipeline for this feature (parsers/renderers).</summary>
    public virtual void Configure(MarkdownPipelineBuilder pipeline)
    {
    }

    /// <summary>Stylesheet/script assets that may be inlined or written next to the document.</summary>
    public virtual IEnumerable<MarkdownAsset> Assets => [];

    /// <summary>Extra markup for the document head (placed after this feature's style assets).</summary>
    public virtual string Head => string.Empty;

    /// <summary>Extra markup for the end of the body (placed after this feature's script assets), e.g. init code.</summary>
    public virtual string BodyEnd => string.Empty;
}
