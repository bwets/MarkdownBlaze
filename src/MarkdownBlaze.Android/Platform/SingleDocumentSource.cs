using Android.Content;
using MarkdownBlaze.Services;
using MarkdownBlaze.Services.Storage;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Platform;

/// <summary>
/// One document the host was handed (an intent's address, or the answer of the document chooser) as
/// <see cref="MirrorEngine"/> reads it, so that a single file is copied by the same code, with the same checks, as a granted
/// folder's. It has no folders; whatever id the engine asks for, the content is the document's.
/// </summary>
internal sealed partial class SingleDocumentSource : IFolderSource
{
    private static readonly ILogger Logger = Log.For(nameof(SingleDocumentSource));
    private readonly ContentResolver _resolver;
    private readonly Android.Net.Uri _document;

    /// <summary>Creates the source for one document.</summary>
    /// <param name="resolver">What opens the address.</param>
    /// <param name="document">The document's address, which the app holds a grant for.</param>
    public SingleDocumentSource(ContentResolver resolver, Android.Net.Uri document)
    {
        _resolver = resolver;
        _document = document;
    }

    /// <inheritdoc />
    public IReadOnlyList<ListedEntry>? List(string folderId) => null;

    /// <inheritdoc />
    public Stream? Open(string fileId)
    {
        try
        {
            return _resolver.OpenInputStream(_document);
        }
        catch (Exception ex) when (Providers.IsFailure(ex))
        {
            LogOpenFailed(Logger, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The document could not be opened")]
    private static partial void LogOpenFailed(ILogger log, Exception ex);
}
