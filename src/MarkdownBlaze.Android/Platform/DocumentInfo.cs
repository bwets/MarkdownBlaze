using MarkdownBlaze.Services.Storage;

namespace MarkdownBlaze.Platform;

/// <summary>What the host learned about a document it was handed.</summary>
/// <param name="Uri">The document's address.</param>
/// <param name="Name">Its name as the provider gives it; untrusted.</param>
/// <param name="Location">Where it is on the shared storage, or null when its provider does not say (Drive, mail, an opaque id).</param>
/// <param name="Size">Its size in bytes, or 0 when not given.</param>
/// <param name="ModifiedMs">When it was last changed, in milliseconds since 1970, or 0 when not given.</param>
internal sealed record DocumentInfo(Android.Net.Uri Uri, string? Name, StorageLocation? Location, long Size, long ModifiedMs);
