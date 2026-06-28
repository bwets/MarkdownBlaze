using Markdig.Parsers;
using Markdig.Syntax;

namespace bwets.Markdig.Extensions.Admonitions;

/// <summary>
/// Parses MkDocs-style admonitions: <c>!!! type "title"</c> (and collapsible <c>??? type</c> /
/// <c>???+ type</c>) followed by a four-space indented body.
/// </summary>
public sealed class MkDocsAdmonitionParser : BlockParser
{
    public MkDocsAdmonitionParser()
    {
        OpeningCharacters = ['!', '?'];
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsCodeIndent)
        {
            return BlockState.None;
        }

        var line = processor.Line; // struct copy
        var marker = line.CurrentChar;
        var count = 0;
        while (line.CurrentChar == marker)
        {
            count++;
            line.NextChar();
        }

        if (count != 3)
        {
            return BlockState.None;
        }

        var collapsible = marker == '?';
        var open = !collapsible;
        if (collapsible && line.CurrentChar == '+')
        {
            open = true;
            line.NextChar();
        }

        // A space must separate the marker from the type.
        if (line.CurrentChar != ' ' && line.CurrentChar != '\t')
        {
            return BlockState.None;
        }

        line.TrimStart();
        var info = line.ToString().Trim();
        if (!AdmonitionInfo.TryParseMkDocs(info, out var kind, out var title))
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
            Collapsible = collapsible,
            Open = open,
        });

        return BlockState.ContinueDiscard;
    }

    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        if (processor.IsBlankLine)
        {
            // Keep the block open across blank lines; a following non-indented line closes it.
            return BlockState.Continue;
        }

        if (processor.IsCodeIndent)
        {
            processor.GoToCodeIndent();
            return BlockState.Continue;
        }

        return BlockState.None;
    }
}
