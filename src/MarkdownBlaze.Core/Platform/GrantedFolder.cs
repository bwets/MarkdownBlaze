namespace MarkdownBlaze.Platform;

/// <summary>A folder the reader has granted, as Settings lists it.</summary>
/// <param name="Id">The host's name for it, which <see cref="IFolderAccess.ForgetAsync"/> takes back.</param>
/// <param name="Label">How the reader would know it (<c>Documents/wiki</c>).</param>
/// <param name="LocalPath">The folder on disk that the rest of the app reads it from, so that a tree rooted in it can be put away when it is forgotten.</param>
internal sealed record GrantedFolder(string Id, string Label, string LocalPath);
