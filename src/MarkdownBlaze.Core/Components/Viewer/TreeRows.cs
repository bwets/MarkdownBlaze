using MarkdownBlaze.Models;

namespace MarkdownBlaze.Components.Viewer;

/// <summary>
/// What the sidebar's Contents tab shows, worked out once per change of folder, document or expansion
/// (reading the file tree has side effects — it opens the path to the document — so it is not done
/// while drawing). The recursive <c>TreePanel</c> component is handed a path and finds its own node here.
/// </summary>
internal sealed class TreeRows
{
    public static TreeRows Empty { get; } = new([], detached: null, topHeadingLevel: 1);

    private readonly Dictionary<string, TreeNode> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public TreeRows(IReadOnlyList<TreeNode> roots, TreeNode? detached, int topHeadingLevel)
    {
        Roots = roots;
        Detached = detached;
        TopHeadingLevel = topHeadingLevel;
        Add(roots);
        if (detached is not null) Add([detached]);
    }

    /// <summary>The panels directly inside the root folder, in display order.</summary>
    public IReadOnlyList<TreeNode> Roots { get; }

    /// <summary>The open document, when it is not inside the tree's folder; it gets a panel above the tree.</summary>
    public TreeNode? Detached { get; }

    /// <summary>
    /// The shallowest heading level of the open document. The outline is indented from it, so a document
    /// that starts at ## does not waste a level of the sidebar's width.
    /// </summary>
    public int TopHeadingLevel { get; }

    public TreeNode? Find(string path) => _byPath.GetValueOrDefault(path);

    private void Add(IEnumerable<TreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            _byPath[node.Path] = node;
            Add(node.Children);
        }
    }
}
