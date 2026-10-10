namespace MarkdownBlaze.Models;

/// <summary>
/// One folder or document in the sidebar, drawn as a collapsible panel. An open folder carries what
/// is inside it in <paramref name="Children"/>; a document has none — when it is the one being read
/// and <paramref name="Expanded"/>, its panel shows the document's own headings instead.
/// </summary>
internal sealed record TreeNode(
    TreeNodeKind Kind,
    string Path,
    string Label,
    bool Expanded,
    bool Current,
    IReadOnlyList<TreeNode> Children);
