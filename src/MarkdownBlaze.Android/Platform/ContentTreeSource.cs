using Android.Content;
using Android.Provider;
using MarkdownBlaze.Services;
using MarkdownBlaze.Services.Storage;
using Microsoft.Extensions.Logging;
using DocumentColumns = Android.Provider.DocumentsContract.Document;

namespace MarkdownBlaze.Platform;

/// <summary>
/// A granted folder as <see cref="MirrorEngine"/> reads it: the children of a folder and the content of a file, asked of
/// the documents provider through the tree's address, which the reader's grant covers. The provider is another app and
/// may be gone, slow or hostile, so a failure to list or open is a null (and a log line), never an exception; the names it
/// returns are passed on as they are, and checked by the mirror before anything is written.
/// </summary>
internal sealed partial class ContentTreeSource : IFolderSource
{
    private static readonly ILogger Logger = Log.For(nameof(ContentTreeSource));

    /// <summary>The columns read for each child, in the order the cursor is read.</summary>
    private static readonly string[] Projection =
    [
        DocumentColumns.ColumnDocumentId,
        DocumentColumns.ColumnDisplayName,
        DocumentColumns.ColumnMimeType,
        DocumentColumns.ColumnSize,
        DocumentColumns.ColumnLastModified,
    ];

    private readonly ContentResolver _resolver;
    private readonly Android.Net.Uri _tree;

    /// <summary>Creates the source for one granted folder.</summary>
    /// <param name="resolver">What queries providers.</param>
    /// <param name="tree">The tree address the reader granted.</param>
    public ContentTreeSource(ContentResolver resolver, Android.Net.Uri tree)
    {
        _resolver = resolver;
        _tree = tree;
    }

    /// <inheritdoc />
    public IReadOnlyList<ListedEntry>? List(string folderId)
    {
        try
        {
            var children = DocumentsContract.BuildChildDocumentsUriUsingTree(_tree, folderId);
            using var cursor = Providers.Query(_resolver, children, Projection);
            if (cursor is null) return null;

            var entries = new List<ListedEntry>(cursor.Count);
            while (cursor.MoveToNext())
            {
                var id = cursor.GetString(0);
                var name = cursor.GetString(1);
                if (id is null || name is null) continue;

                var isFolder = string.Equals(cursor.GetString(2), DocumentColumns.MimeTypeDir, StringComparison.Ordinal);
                entries.Add(new ListedEntry(name, isFolder, cursor.GetLong(3), cursor.GetLong(4), id));
            }

            return entries;
        }
        catch (Exception ex) when (Providers.IsFailure(ex))
        {
            LogListFailed(Logger, ex);
            return null;
        }
    }

    /// <inheritdoc />
    public Stream? Open(string fileId)
    {
        try
        {
            var document = DocumentsContract.BuildDocumentUriUsingTree(_tree, fileId);
            return document is null ? null : _resolver.OpenInputStream(document);
        }
        catch (Exception ex) when (Providers.IsFailure(ex))
        {
            LogOpenFailed(Logger, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A folder of a granted tree could not be listed")]
    private static partial void LogListFailed(ILogger log, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A file of a granted tree could not be opened")]
    private static partial void LogOpenFailed(ILogger log, Exception ex);
}
