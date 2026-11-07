namespace MarkdownBlaze.Services.Storage;

/// <summary>A file the mirror already holds.</summary>
/// <param name="Path">Its path below the mirror of the granted folder, <c>/</c>-separated.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="LastModifiedMs">Its last-write time in milliseconds since 1970; the copy sets it to the provider's, so that the two can be compared.</param>
internal sealed record LocalEntry(string Path, long Size, long LastModifiedMs);
