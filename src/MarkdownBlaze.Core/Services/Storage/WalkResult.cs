namespace MarkdownBlaze.Services.Storage;

/// <summary>The outcome of a walk of a granted folder.</summary>
/// <param name="Files">The files to be mirrored, nearest the top first.</param>
/// <param name="Cap">Why the walk stopped early, if it did.</param>
/// <param name="Complete">Whether the walk covered the whole folder: no cap, no folder that failed to list, no depth limit.</param>
internal sealed record WalkResult(IReadOnlyList<RemoteEntry> Files, MirrorCap Cap, bool Complete);
