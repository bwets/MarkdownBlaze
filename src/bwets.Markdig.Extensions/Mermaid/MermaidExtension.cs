using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace bwets.Markdig.Extensions.Mermaid;

/// <summary>Markdig extension enabling mermaid diagram rendering.</summary>
public sealed class MermaidExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is not HtmlRenderer html)
        {
            return;
        }

        for (var i = 0; i < html.ObjectRenderers.Count; i++)
        {
            if (html.ObjectRenderers[i] is CodeBlockRenderer and not MermaidCodeBlockRenderer)
            {
                html.ObjectRenderers[i] = new MermaidCodeBlockRenderer();
                return;
            }
        }

        html.ObjectRenderers.Add(new MermaidCodeBlockRenderer());
    }
}
