using Markdig.Parsers;
using Markdig.Syntax;

namespace bwets.Markdig.Extensions.Admonitions;

/// <summary>
/// A parsed admonition / callout, produced from either Docusaurus (<c>:::type[title] ... :::</c>)
/// or MkDocs (<c>!!! type "title"</c> / <c>??? type "title"</c> + indented body) syntax.
/// </summary>
public sealed class AdmonitionBlock : ContainerBlock
{
    public AdmonitionBlock(BlockParser parser) : base(parser)
    {
    }

    public string Kind { get; set; } = "note";

    /// <summary>null = default title (the kind), "" = no title, otherwise a custom title.</summary>
    public string? Title { get; set; }

    public bool Collapsible { get; set; }

    public bool Open { get; set; } = true;

    /// <summary>Number of opening fence characters (Docusaurus only).</summary>
    public int FenceLength { get; set; }
}
