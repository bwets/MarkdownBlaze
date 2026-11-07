namespace MarkdownBlaze.Services.Storage;

/// <summary>A file a provider lists inside a granted folder.</summary>
/// <param name="Path">Its path below the granted folder, <c>/</c>-separated, every segment a safe name.</param>
/// <param name="Size">Its size in bytes; zero or less when the provider does not say.</param>
/// <param name="LastModifiedMs">When it was last changed, in milliseconds since 1970; zero or less when the provider does not say.</param>
/// <param name="Id">The provider's own id for it, which the host needs to read it; the planner never looks at it.</param>
internal sealed record RemoteEntry(string Path, long Size, long LastModifiedMs, string Id = "");
