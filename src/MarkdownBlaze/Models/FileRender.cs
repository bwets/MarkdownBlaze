namespace MarkdownBlaze.Models;

/// <summary>The outcome of rendering a file: the document, or the reason there is none.</summary>
/// <param name="Status">What became of the attempt.</param>
/// <param name="Result">The document, when <paramref name="Status"/> is <see cref="RenderStatus.Rendered"/>.</param>
/// <param name="Length">The file's size in bytes, when it is <see cref="RenderStatus.TooLarge"/>.</param>
internal sealed record FileRender(RenderStatus Status, RenderResult? Result = null, long Length = 0);
