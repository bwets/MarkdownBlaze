using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace bwets.Markdig.Extensions.Admonitions;

/// <summary>Markdig extension that enables Docusaurus and MkDocs admonitions.</summary>
public sealed class AdmonitionExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<DocusaurusAdmonitionParser>())
        {
            pipeline.BlockParsers.Insert(0, new DocusaurusAdmonitionParser());
        }

        if (!pipeline.BlockParsers.Contains<MkDocsAdmonitionParser>())
        {
            pipeline.BlockParsers.Insert(0, new MkDocsAdmonitionParser());
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html && !html.ObjectRenderers.Contains<AdmonitionRenderer>())
        {
            html.ObjectRenderers.Insert(0, new AdmonitionRenderer());
        }
    }
}
