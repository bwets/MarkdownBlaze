namespace MarkdownBlaze.Services.Storage;

/// <summary>
/// Works out where a document is from what Android hands the app: the document id of a documents provider, or
/// the columns MediaStore keeps for a file. Everything here is a pure function of text, so the host only has to
/// read the strings; a provider that is not recognised gives null, which means "the folder is not known" and
/// leaves the document a single file.
/// <para>
/// Only providers whose ids or columns say where the file is on the shared storage are understood: the
/// external-storage provider (<c>primary:Documents/wiki/index.md</c>), the downloads provider when its id is a raw
/// path (<c>raw:/storage/emulated/0/Download/x.md</c>) and MediaStore (relative path, name and volume). Drive, mail
/// attachments and ids that are only numbers say nothing about a folder.
/// </para>
/// </summary>
internal static class StorageUris
{
    /// <summary>The authority of the external-storage provider, the one behind the Files app's "Internal storage".</summary>
    public const string ExternalStorageAuthority = "com.android.externalstorage.documents";

    /// <summary>The authority of the downloads provider.</summary>
    public const string DownloadsAuthority = "com.android.providers.downloads.documents";

    /// <summary>The volume name of the built-in storage.</summary>
    public const string PrimaryVolume = "primary";

    /// <summary>The folder the Files app calls Documents; its "home" root is this folder.</summary>
    private const string DocumentsFolder = "Documents";

    /// <summary>The prefix of a downloads-provider id that carries a file path.</summary>
    private const string RawPrefix = "raw:";

    /// <summary>
    /// The location an external-storage document or tree id names: <c>primary:Documents/wiki</c>, or
    /// <c>1234-ABCD:notes</c> on a card. The id <c>home:</c> is the Documents folder of the built-in storage.
    /// </summary>
    public static StorageLocation? FromExternalStorageId(string? documentId)
    {
        if (string.IsNullOrEmpty(documentId)) return null;

        var colon = documentId.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0) return null;

        var volume = documentId[..colon];
        var path = documentId[(colon + 1)..];
        return string.Equals(volume, "home", StringComparison.OrdinalIgnoreCase)
            ? StorageLocation.Create(PrimaryVolume, DocumentsFolder + "/" + path)
            : StorageLocation.Create(volume, path);
    }

    /// <summary>The location a downloads-provider id names, when it is a raw path (<c>raw:/storage/emulated/0/Download/x.md</c>).</summary>
    public static StorageLocation? FromDownloadsId(string? documentId)
    {
        if (documentId?.StartsWith(RawPrefix, StringComparison.Ordinal) != true) return null;
        return FromFilePath(documentId[RawPrefix.Length..]);
    }

    /// <summary>
    /// The location of a path of the shared storage: <c>/storage/emulated/0/…</c> (any user), <c>/sdcard/…</c>
    /// and <c>/storage/self/primary/…</c> are the built-in storage; <c>/storage/&lt;id&gt;/…</c> is a card. Any other
    /// path (the app's own, the system's) is not shared storage and gives null.
    /// </summary>
    public static StorageLocation? FromFilePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/') return null;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        if (string.Equals(segments[0], "sdcard", StringComparison.Ordinal)) return StorageLocation.Create(PrimaryVolume, string.Join('/', segments[1..]));
        if (!string.Equals(segments[0], "storage", StringComparison.Ordinal) || segments.Length < 2) return null;

        if (string.Equals(segments[1], "emulated", StringComparison.Ordinal))
            return segments.Length >= 3 ? StorageLocation.Create(PrimaryVolume, string.Join('/', segments[3..])) : null;

        if (string.Equals(segments[1], "self", StringComparison.Ordinal))
        {
            return segments.Length >= 3 && string.Equals(segments[2], PrimaryVolume, StringComparison.Ordinal)
                ? StorageLocation.Create(PrimaryVolume, string.Join('/', segments[3..]))
                : null;
        }

        return StorageLocation.Create(segments[1], string.Join('/', segments[2..]));
    }

    /// <summary>
    /// The location of a file MediaStore describes. <paramref name="relativePath"/> is its folder below the
    /// volume (<c>Documents/wiki/</c>), <paramref name="displayName"/> its name, <paramref name="volumeName"/> the
    /// volume (<c>external_primary</c>, or a card's lower-case id). Null when a column is missing, or the volume
    /// is the merged <c>external</c> one, which does not say which storage the file is on.
    /// </summary>
    public static StorageLocation? FromMediaStore(string? volumeName, string? relativePath, string? displayName)
    {
        if (string.IsNullOrEmpty(volumeName) || string.IsNullOrEmpty(displayName)) return null;
        if (displayName.Contains('/', StringComparison.Ordinal) || displayName.Contains('\\', StringComparison.Ordinal)) return null;

        string volume;
        if (string.Equals(volumeName, "external_primary", StringComparison.Ordinal)) volume = PrimaryVolume;
        else if (string.Equals(volumeName, "external", StringComparison.Ordinal)) return null;
        else volume = volumeName;

        return StorageLocation.Create(volume, (relativePath ?? "") + "/" + displayName);
    }
}
