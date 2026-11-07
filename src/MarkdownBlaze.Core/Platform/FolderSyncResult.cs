namespace MarkdownBlaze.Platform;

/// <summary>The outcome of <see cref="IFolderAccess.SyncAsync"/>.</summary>
/// <param name="Changed">The paths of the files whose content was replaced, so that the one on screen can be read again.</param>
/// <param name="TreeChanged">Whether any file was added, replaced or removed, so that the tree should be read again.</param>
internal sealed record FolderSyncResult(IReadOnlyList<string> Changed, bool TreeChanged)
{
    /// <summary>Nothing changed.</summary>
    public static FolderSyncResult None { get; } = new([], TreeChanged: false);
}
