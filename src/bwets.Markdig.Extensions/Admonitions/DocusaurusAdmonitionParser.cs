using Markdig.Parsers;
using Markdig.Syntax;

namespace bwets.Markdig.Extensions.Admonitions;

/// <summary>
/// Parses Docusaurus-style fenced admonitions: <c>:::warning[Optional title]</c> … <c>:::</c>.
/// </summary>
public sealed class DocusaurusAdmonitionParser : BlockParser
{
    public DocusaurusAdmonitionParser()
    {
        OpeningCharacters = [':'];
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsCodeIndent)
        {
            return BlockState.None;
        }

        var line = processor.Line; // struct copy for inspection
        var count = 0;
        while (line.CurrentChar == ':')
        {
            count++;
            line.NextChar();
        }

        if (count < 3)
        {
            return BlockState.None;
        }

        line.TrimStart();
        var info = line.ToString().Trim();
        if (!AdmonitionInfo.TryParseDocusaurus(info, out var kind, out var title))
        {
            return BlockState.None;
        }

        processor.NewBlocks.Push(new AdmonitionBlock(this)
        {
            Column = processor.Column,
            Line = processor.LineIndex,
            Span = new SourceSpan(processor.Start, processor.Line.End),
            Kind = kind,
            Title = title,
            Collapsible = false,
            FenceLength = count,
        });

        return BlockState.ContinueDiscard;
    }

    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        if (processor.IsBlankLine)
        {
            return BlockState.Continue;
        }

        var admonition = (AdmonitionBlock)block;
        var line = processor.Line; // struct copy
        var count = 0;
        while (line.CurrentChar == ':')
        {
            count++;
            line.NextChar();
        }

        if (count >= admonition.FenceLength)
        {
            line.TrimStart();
            if (line.IsEmpty)
            {
                block.UpdateSpanEnd(processor.Line.End);
                return BlockState.BreakDiscard;
            }
        }

        return BlockState.Continue;
    }
}
