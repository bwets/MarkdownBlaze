namespace MarkdownBlaze.Services.Storage;

/// <summary>
/// What a mirror is made from: the host's view of a documents provider, folder by folder. The provider is another
/// app, so what it says is data to check, not to trust (<see cref="MirrorNames"/>), and it can fail at any call (the grant
/// was taken back, the card was pulled out), which is why neither member throws for that.
/// </summary>
internal interface IFolderSource
{
    /// <summary>The entries of one folder, or null when it cannot be listed.</summary>
    /// <param name="folderId">The provider's id for the folder.</param>
    IReadOnlyList<ListedEntry>? List(string folderId);

    /// <summary>The content of one file, or null when it cannot be read. The caller disposes the stream.</summary>
    /// <param name="fileId">The provider's id for the file.</param>
    Stream? Open(string fileId);
}
