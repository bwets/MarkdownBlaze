using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace bwets.Markdig.Extensions.Mermaid;

/// <summary>
/// Renders fenced <c>```mermaid</c> code blocks as <c>&lt;pre class="mermaid"&gt;</c> so a
/// client-side mermaid library turns them into diagrams. All other code blocks fall back to the
/// default rendering.
/// </summary>
public sealed class MermaidCodeBlockRenderer : CodeBlockRenderer
{
    protected override void Write(HtmlRenderer renderer, CodeBlock obj)
    {
        if (obj is FencedCodeBlock fenced
            && fenced.Info is { } info
            && info.Trim().Equals("mermaid", StringComparison.OrdinalIgnoreCase))
        {
            renderer.EnsureLine();
            renderer.Write("<pre class=\"mermaid\">");
            renderer.WriteLeafRawLines(obj, true, true);
            renderer.Write("</pre>");
            renderer.EnsureLine();
            return;
        }

        base.Write(renderer, obj);
    }
}
