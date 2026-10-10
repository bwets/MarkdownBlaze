namespace MarkdownBlaze.Components.Viewer;

/// <summary>
/// The popup menu (More button, view and theme choices, the row context menu): its entries and where it
/// opens. <paramref name="Right"/> anchors it under the right edge of the toolbar, like the More button
/// that opens it; otherwise it opens at <paramref name="X"/> and, when <paramref name="Y"/> is given, at
/// that height — without one it hangs from the bottom of the toolbar, where the stylesheet puts it.
/// </summary>
internal sealed record ContextMenuModel(string Label, IReadOnlyList<MenuItem> Items, bool Right, double X = 0, double? Y = null);
