namespace MarkdownBlaze.Components.Viewer;

/// <summary>What the grant bar asks: access to the folder a single-file document came from.</summary>
/// <param name="DocumentPath">The document the offer is for; it is withdrawn when another document is shown.</param>
/// <param name="Label">The folder as the bar names it (<c>Documents/wiki</c>).</param>
internal sealed record FolderBarOffer(string DocumentPath, string Label);
