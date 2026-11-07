namespace MarkdownBlaze.Services;

/// <summary>
/// What the page may fetch through <c>mdasset://</c> right now: the folder a document's relative image
/// paths are resolved against, and the token that goes with this render of it.
/// </summary>
/// <param name="Token">
/// Random, made afresh for every render. It is in the address of every image of that render and nowhere
/// else, so an address that has outlived its document — or one an author guessed — matches nothing.
/// </param>
/// <param name="BaseFolder">The document's folder; empty for text with no folder (dropped onto the window), where only rooted paths resolve.</param>
internal sealed record AssetGrant(string Token, string BaseFolder);
