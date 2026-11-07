namespace MarkdownBlaze.Platform;

/// <summary>The outcome of <see cref="IFolderAccess.GrantAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="DocumentPath">The document's path in the mirror, when <paramref name="Status"/> is <see cref="FolderGrantStatus.Granted"/>.</param>
/// <param name="Root">The root of the granted folder's mirror, for the tree.</param>
internal sealed record FolderGrantResult(FolderGrantStatus Status, string? DocumentPath = null, string? Root = null);
