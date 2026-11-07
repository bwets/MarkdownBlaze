namespace MarkdownBlaze.Platform;

/// <summary>
/// A host whose files are behind a permission the reader grants folder by folder (Android's Storage Access
/// Framework). Such a host keeps a copy of each granted folder where the rest of the app can read it by an
/// ordinary path — the mirror — and this is how the viewer asks it to widen, refresh or narrow what it has. The
/// desktop host has none and does not register one: whatever uses this asks the container for it and shows nothing
/// when it is absent.
/// </summary>
internal interface IFolderAccess
{
    /// <summary>The folders the reader has granted.</summary>
    IReadOnlyList<GrantedFolder> Folders { get; }

    /// <summary>
    /// Asks the reader for the folder a single-file document was opened from, keeps the grant, mirrors the folder and
    /// says where the document is in it. The rest of the folder is copied in the background.
    /// </summary>
    /// <param name="documentPath">The path the single-file document was opened at.</param>
    Task<FolderGrantResult> GrantAsync(string documentPath);

    /// <summary>Gives the grant back and removes the mirror.</summary>
    /// <param name="id">The <see cref="GrantedFolder.Id"/> of the folder.</param>
    Task ForgetAsync(string id);

    /// <summary>
    /// Brings the mirror that holds <paramref name="documentPath"/> up to date with the folder, copying what changed.
    /// A document that is in no mirror gives <see cref="FolderSyncResult.None"/>.
    /// </summary>
    /// <param name="documentPath">The document on screen.</param>
    Task<FolderSyncResult> SyncAsync(string documentPath);
}
