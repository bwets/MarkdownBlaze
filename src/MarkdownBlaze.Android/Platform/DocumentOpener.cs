using System.Globalization;
using Android.Content;
using Android.Provider;
using MarkdownBlaze.Services;
using MarkdownBlaze.Services.Storage;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Platform;

/// <summary>
/// Turns what Android hands the app — one <c>content://</c> or <c>file://</c> address from a file manager, a mail
/// attachment, the share sheet or the document chooser — into a path the viewer can open, and puts it in the
/// <see cref="HostInbox"/>.
/// <para>
/// A document that lies inside a folder the reader has granted is opened at its place in that folder's mirror, so the tree,
/// the links and the pictures work. Any other is copied, alone, into the single-file area and opened there: no tree, and links to
/// other files lead nowhere. When the document's folder can be named (<see cref="StorageUris"/>) the viewer is also given the
/// offer to grant it, which is what the grant bar asks. Where it cannot — Drive, mail, an opaque id — there is no offer,
/// because there is no folder to ask for.
/// </para>
/// </summary>
internal sealed partial class DocumentOpener
{
    private static readonly ILogger Logger = Log.For(nameof(DocumentOpener));
    private readonly Context _context;
    private readonly ContentResolver _resolver;
    private readonly AndroidStorage _storage;
    private readonly HostInbox _inbox;

    /// <summary>Creates the opener.</summary>
    /// <param name="context">The application context.</param>
    /// <param name="storage">The granted folders.</param>
    /// <param name="inbox">Where the document goes to reach the viewer.</param>
    public DocumentOpener(Context context, AndroidStorage storage, HostInbox inbox)
    {
        _context = context;
        _resolver = context.ContentResolver ?? throw new InvalidOperationException("The app has no content resolver.");
        _storage = storage;
        _inbox = inbox;
    }

    /// <summary>Opens a document the app was handed. The work happens off the calling thread; the viewer is told when the document is ready, or that it could not be opened.</summary>
    /// <param name="uri">The document's address.</param>
    public async Task OpenAsync(Android.Net.Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        try
        {
            await Task.Run(() => OpenNow(uri)).ConfigureAwait(false);
        }
        catch (Exception ex) when (Providers.IsFailure(ex) || ex is InvalidOperationException or ArgumentException)
        {
            LogOpenFailed(Logger, ex);
            _inbox.Notify("Could not open that file.");
        }
    }

    private void OpenNow(Android.Net.Uri uri)
    {
        var info = Describe(uri);

        TreeGrant? tree = null;
        if (info.Location is { } location)
        {
            tree = _storage.FindTreeContaining(location);
            if (tree is not null && _storage.MirrorDocument(tree, info) is { } mirrored)
            {
                _inbox.Post(new IncomingDocument { Path = mirrored, Root = tree.Root });
                // The rest of the folder, so that a link to another page of it works; the tree is refreshed when it is done.
                _storage.SyncInBackground(tree);
                return;
            }
        }

        var single = _storage.CopySingle(info);
        if (single is null)
        {
            _inbox.Notify("Could not open that file. It may be unreadable or larger than 32 MB.");
            return;
        }

        // A folder that is already granted, but could not give the document, is not asked for again.
        var offer = tree is null && info.Location is { } where ? FolderOffer.For(where, restrictedRoots: OperatingSystem.IsAndroidVersionAtLeast(30)) : null;
        if (offer is not null) _storage.Remember(single, new PendingOffer(info, offer));

        _inbox.Post(new IncomingDocument { Path = single, FolderLabel = offer?.Label });
    }

    /// <summary>What can be known of a document from its address and its provider.</summary>
    internal DocumentInfo Describe(Android.Net.Uri uri)
    {
        string? name = null;
        long size = 0;
        long modified = 0;

        if (string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
        {
            (name, size) = QueryNameAndSize(uri);
            if (DocumentsContract.IsDocumentUri(_context, uri)) modified = QueryModified(uri);
        }
        else if (string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileName(uri.Path);
        }

        return new DocumentInfo(uri, name, LocationOf(uri), size, modified);
    }

    /// <summary>
    /// Where the document is on the shared storage, from the id its provider uses or the columns MediaStore keeps; null when
    /// the provider does not say, which leaves the document a single file.
    /// </summary>
    private StorageLocation? LocationOf(Android.Net.Uri uri)
    {
        if (string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase)) return StorageUris.FromFilePath(uri.Path);
        if (!string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            var authority = uri.Authority;
            if (string.Equals(authority, StorageUris.ExternalStorageAuthority, StringComparison.Ordinal) && DocumentsContract.IsDocumentUri(_context, uri))
                return StorageUris.FromExternalStorageId(DocumentsContract.GetDocumentId(uri));

            if (string.Equals(authority, StorageUris.DownloadsAuthority, StringComparison.Ordinal) && DocumentsContract.IsDocumentUri(_context, uri))
                return StorageUris.FromDownloadsId(DocumentsContract.GetDocumentId(uri));

            if (string.Equals(authority, MediaStore.Authority, StringComparison.Ordinal)) return FromMediaStore(uri);

            // The Files app's "Documents" and "Recent" lists hand out ids of the media provider ("document:1234"),
            // which are MediaStore rows; the same row asked of MediaStore itself says where the file is.
            if (string.Equals(authority, "com.android.providers.media.documents", StringComparison.Ordinal) && DocumentsContract.IsDocumentUri(_context, uri))
            {
                var id = DocumentsContract.GetDocumentId(uri);
                var colon = id?.IndexOf(':', StringComparison.Ordinal) ?? -1;
                if (id is not null && colon >= 0 && long.TryParse(id.AsSpan(colon + 1), CultureInfo.InvariantCulture, out var row))
                    return FromMediaStore(MediaStore.Files.GetContentUri("external", row));
            }
        }
        catch (Exception ex) when (Providers.IsFailure(ex))
        {
            LogLocationFailed(Logger, ex);
        }

        return null;
    }

    private StorageLocation? FromMediaStore(Android.Net.Uri? uri)
    {
        // The columns that say where a file is were added in Android 10.
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return null;

        string[] columns = [MediaStore.IMediaColumns.VolumeName, MediaStore.IMediaColumns.RelativePath, MediaStore.IMediaColumns.DisplayName];
        using var cursor = Providers.Query(_resolver, uri, columns);
        return cursor?.MoveToFirst() == true
            ? StorageUris.FromMediaStore(cursor.GetString(0), cursor.GetString(1), cursor.GetString(2))
            : null;
    }

    private (string? Name, long Size) QueryNameAndSize(Android.Net.Uri uri)
    {
        try
        {
            using var cursor = Providers.Query(_resolver, uri, [IOpenableColumns.DisplayName, IOpenableColumns.Size]);
            if (cursor?.MoveToFirst() != true) return (null, 0);
            return (cursor.GetString(0), cursor.IsNull(1) ? 0 : cursor.GetLong(1));
        }
        catch (Exception ex) when (Providers.IsFailure(ex))
        {
            LogLocationFailed(Logger, ex);
            return (null, 0);
        }
    }

    private long QueryModified(Android.Net.Uri uri)
    {
        try
        {
            using var cursor = Providers.Query(_resolver, uri, [DocumentsContract.Document.ColumnLastModified]);
            return cursor?.MoveToFirst() == true && !cursor.IsNull(0) ? cursor.GetLong(0) : 0;
        }
        catch (Exception ex) when (Providers.IsFailure(ex))
        {
            LogLocationFailed(Logger, ex);
            return 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A document could not be opened")]
    private static partial void LogOpenFailed(ILogger log, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A document's provider would not say where it is")]
    private static partial void LogLocationFailed(ILogger log, Exception ex);
}
