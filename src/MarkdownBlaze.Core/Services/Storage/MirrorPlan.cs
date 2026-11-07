namespace MarkdownBlaze.Services.Storage;

/// <summary>What a sync has to do.</summary>
/// <param name="Copy">The files to copy, the one asked for first, then nearest the top of the folder first.</param>
/// <param name="Delete">The mirrored files that are not in the folder any more.</param>
internal sealed record MirrorPlan(IReadOnlyList<RemoteEntry> Copy, IReadOnlyList<string> Delete);
