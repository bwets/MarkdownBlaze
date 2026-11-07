using MarkdownBlaze.Services;

namespace MarkdownBlaze.Models;

/// <summary>A document rendered for the page.</summary>
/// <param name="Path">The document's full path, or the name it was dropped under.</param>
/// <param name="File">The file name alone.</param>
/// <param name="Title">The document's first level-1 heading, else its first heading, else its file name.</param>
/// <param name="BodyHtml">The sanitized body HTML.</param>
/// <param name="Headings">Every heading of the document, in order.</param>
/// <param name="Language">
/// The language the document is written in, as a tag for its <c>lang</c> attribute — what the WebView
/// hyphenates by. Null when there is too little text to tell.
/// </param>
/// <param name="Assets">
/// What the page needs to be allowed to fetch the document's images (see <see cref="AssetScope"/>); null
/// when the document has none.
/// </param>
internal sealed record RenderResult(
    string Path,
    string File,
    string Title,
    string BodyHtml,
    IReadOnlyList<Heading> Headings,
    string? Language = null,
    AssetGrant? Assets = null);
