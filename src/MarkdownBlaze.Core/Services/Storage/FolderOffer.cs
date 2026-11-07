namespace MarkdownBlaze.Services.Storage;

/// <summary>
/// The folder the reader is asked to grant for a document that opened as a single file: the folder the
/// document is in, and how to name it in the bar.
/// </summary>
/// <param name="Folder">The folder holding the document; the system's folder chooser is pointed at it.</param>
internal sealed record FolderOffer(StorageLocation Folder)
{
    /// <summary>The folder as the bar names it (<c>Documents/wiki</c>).</summary>
    public string Label => Folder.Label;

    /// <summary>
    /// The offer for a document at <paramref name="document"/>, or null when there is nothing the reader could
    /// grant. Android's folder chooser refuses the top of a volume, and from Android 11 (<paramref name="restrictedRoots"/>)
    /// also the Download folder itself, so asking for those would send the reader to a screen with a disabled
    /// button; a document directly in one of them stays a single file, and a folder inside Download is fine.
    /// </summary>
    /// <param name="document">Where the document is.</param>
    /// <param name="restrictedRoots">True where the folder chooser refuses the Download folder (Android 11 and later).</param>
    public static FolderOffer? For(StorageLocation document, bool restrictedRoots)
    {
        ArgumentNullException.ThrowIfNull(document);

        var folder = document.Parent;
        if (folder is null || folder.Path.Length == 0) return null;
        if (restrictedRoots && string.Equals(folder.Path, "Download", StringComparison.OrdinalIgnoreCase)) return null;
        return new FolderOffer(folder);
    }
}
