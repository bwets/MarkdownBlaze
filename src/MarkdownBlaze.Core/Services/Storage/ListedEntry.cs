namespace MarkdownBlaze.Services.Storage;

/// <summary>An entry of a folder as a provider lists it.</summary>
/// <param name="Name">The provider's display name; untrusted until <see cref="MirrorNames"/> has passed it.</param>
/// <param name="IsFolder">Whether it is a folder.</param>
/// <param name="Size">Its size in bytes; zero or less when not given.</param>
/// <param name="LastModifiedMs">Its last-modified time in milliseconds since 1970; zero or less when not given.</param>
/// <param name="Id">The provider's id for it: what lists its children, or reads it.</param>
internal sealed record ListedEntry(string Name, bool IsFolder, long Size, long LastModifiedMs, string Id);
