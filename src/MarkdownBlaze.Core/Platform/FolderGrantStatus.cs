namespace MarkdownBlaze.Platform;

/// <summary>How asking for a folder ended.</summary>
internal enum FolderGrantStatus
{
    /// <summary>The reader backed out of the chooser; nothing changed.</summary>
    Cancelled = 0,

    /// <summary>The folder is granted and the document is inside it; <see cref="FolderGrantResult.DocumentPath"/> is its place in the mirror.</summary>
    Granted = 1,

    /// <summary>The folder is granted, but the document is not in it, so the document stays a single file.</summary>
    GrantedWithoutDocument = 2,

    /// <summary>Something went wrong (the log says what) and nothing was granted.</summary>
    Failed = 3,
}
