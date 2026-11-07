using System.Text;
using AngleSharp.Dom;

namespace MarkdownBlaze.Security;

/// <summary>
/// Keeps the HTML of a document from being nested deeper than a browser would show it.
/// <para>
/// A document can open fifty thousand <c>&lt;div&gt;</c>s in 300 KB. AngleSharp builds that tree without
/// trouble — its parser keeps its own stack of open elements instead of recursing — but nearly everything
/// else that walks a tree does recurse, once per level: <c>Node.Normalize</c> inside the sanitizer, the
/// serializer, <c>TextContent</c>. A tree that deep overflows the stack, and a stack overflow cannot be
/// caught: the process is gone. So the tree is cut down to a depth that is safe to recurse through
/// <i>before</i> anything recurses through it, and everything here is written with a stack of its own for
/// the same reason.
/// </para>
/// <para>
/// <see cref="MaxDepth"/> is Chromium's own limit (<c>kMaximumHTMLParserDOMTreeDepth</c>): the engine in
/// the window stops nesting at 512 and puts what follows in the element it has reached, so a document
/// nested deeper than that is already shown flattened. Nothing a reader could have seen is lost here.
/// </para>
/// </summary>
internal static class NestingLimit
{
    /// <summary>The deepest an element may sit: the children of the root are at depth 1.</summary>
    public const int MaxDepth = 512;

    /// <summary>
    /// Replaces every element deeper than <see cref="MaxDepth"/> with its text. The text is collected
    /// without recursion, and the elements themselves are not asked for it (<c>TextContent</c> and
    /// <c>ToHtml</c> recurse); what an element held besides text — more elements, attributes — goes.
    /// </summary>
    /// <param name="root">The node whose descendants are limited (a document's <c>body</c>).</param>
    /// <returns>How many elements were replaced, each standing for the whole subtree below it.</returns>
    public static int Apply(INode root)
    {
        var flattened = 0;
        var pending = new Stack<(INode Parent, int Depth)>();
        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            var (parent, depth) = pending.Pop();

            // A copy: the children are replaced while they are being looked at.
            foreach (var child in parent.ChildNodes.OfType<IElement>().ToArray())
            {
                if (depth + 1 <= MaxDepth)
                {
                    pending.Push((child, depth + 1));
                    continue;
                }

                var text = TextOf(child);
                if (text.Length == 0)
                    _ = parent.RemoveChild(child);
                else
                    _ = parent.ReplaceChild(parent.Owner!.CreateTextNode(text), child);

                flattened++;
            }
        }

        return flattened;
    }

    /// <summary>The depth of the deepest element below <paramref name="root"/> (0 when it has none), counted without recursion.</summary>
    /// <param name="root">The node to measure.</param>
    public static int DepthOf(INode root)
    {
        var deepest = 0;
        var pending = new Stack<(INode Node, int Depth)>();
        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            var (node, depth) = pending.Pop();
            deepest = Math.Max(deepest, depth);
            foreach (var child in node.ChildNodes.OfType<IElement>())
                pending.Push((child, depth + 1));
        }

        return deepest;
    }

    /// <summary>
    /// The text of everything below <paramref name="element"/>, in document order. Comments are not text;
    /// the content of a <c>&lt;script&gt;</c> is, and is only ever written out as text (escaped), because
    /// the element that held it is gone.
    /// </summary>
    private static string TextOf(IElement element)
    {
        var text = new StringBuilder();
        var pending = new Stack<INode>();
        pending.Push(element);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is IText content)
            {
                _ = text.Append(content.Data);
                continue;
            }

            // Pushed last to first, so that they come off first to last.
            var children = node.ChildNodes;
            for (var i = children.Length - 1; i >= 0; i--)
                pending.Push(children[i]);
        }

        return text.ToString();
    }
}
