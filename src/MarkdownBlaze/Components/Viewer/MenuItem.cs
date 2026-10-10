namespace MarkdownBlaze.Components.Viewer;

/// <summary>
/// One entry of the popup menu. <paramref name="Checked"/> is null for a plain command and true or false
/// for one of a set of choices (the view and the document theme), which is drawn with a tick and told to
/// assistive technology as a radio item.
/// </summary>
internal sealed record MenuItem(string Label, Func<Task> Run, bool? Checked = null);
