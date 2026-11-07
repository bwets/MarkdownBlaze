namespace MarkdownBlaze.Services.Storage;

/// <summary>What a sync of a mirror did.</summary>
/// <param name="Copied">The full paths of the files that were copied (new, or replaced with a changed one).</param>
/// <param name="Deleted">How many mirrored files were removed because the folder no longer has them.</param>
/// <param name="Skipped">How many files could not be copied: the provider would not give them, or they were over the size limit.</param>
/// <param name="Cap">Why the walk stopped before the end of the folder, if it did.</param>
internal sealed record SyncOutcome(IReadOnlyList<string> Copied, int Deleted, int Skipped, MirrorCap Cap)
{
    /// <summary>Whether the tree of the mirror differs from before: something was added, replaced or removed.</summary>
    public bool Changed => Copied.Count > 0 || Deleted > 0;
}
