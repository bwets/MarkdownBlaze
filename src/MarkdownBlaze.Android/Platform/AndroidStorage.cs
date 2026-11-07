using System.Collections.Concurrent;
using System.Globalization;
using Android.Content;
using Android.Provider;
using MarkdownBlaze.Services;
using MarkdownBlaze.Services.Storage;
using Microsoft.Extensions.Logging;
using DocumentColumns = Android.Provider.DocumentsContract.Document;

namespace MarkdownBlaze.Platform;

/// <summary>
/// The granted folders of the Android host, and the mirror of each (see <see cref="IFolderAccess"/>). Android hands an
/// app a document, or a folder the reader chose once through the Storage Access Framework, never a path; everything else in
/// the app reads paths. So a granted folder is copied into <c>mirror/&lt;id&gt;/&lt;folder name&gt;</c> under the app's no-backup
/// storage (not the cache, which Android may empty while the history still points into it), and the viewer opens, watches
/// and lists those copies like any folder. The grants themselves are the ones Android persists
/// (<c>ContentResolver.PersistedUriPermissions</c>): this class keeps no list of its own, so a grant the reader takes away in the
/// system's settings is simply no longer there, and its mirror is dropped at the next start.
/// <para>
/// A mirror is brought up to date by <see cref="MirrorEngine"/>, one sync of a folder at a time. The folder a reader grants
/// is only ever read: the permission asked for and kept is read-only.
/// </para>
/// </summary>
internal sealed partial class AndroidStorage : IFolderAccess
{
    private static readonly ILogger Logger = Log.For(nameof(AndroidStorage));

    /// <summary>
    /// How many single-file offers are remembered. An offer is only needed until the reader answers the bar, which is shown
    /// for the document on screen, so a few are plenty; the limit stops a long session from collecting them.
    /// </summary>
    private const int MaxRememberedOffers = 32;

    private readonly ContentResolver _resolver;
    private readonly HostInbox _inbox;
    private readonly string _mirrors;
    private readonly string _singles;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingOffer> _offers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MirrorCap> _reportedCaps = new(StringComparer.Ordinal);

    /// <summary>Creates the storage for the app.</summary>
    /// <param name="context">The application context.</param>
    /// <param name="inbox">Where documents, notices and mirror changes go to reach the viewer.</param>
    public AndroidStorage(Context context, HostInbox inbox)
    {
        _resolver = context.ContentResolver ?? throw new InvalidOperationException("The app has no content resolver.");
        _inbox = inbox;

        var data = context.NoBackupFilesDir?.AbsolutePath ?? throw new InvalidOperationException("The app has no private storage.");
        _mirrors = Path.Combine(data, "mirror");
        _singles = Path.Combine(data, "single");
    }

    /// <inheritdoc />
    public IReadOnlyList<GrantedFolder> Folders =>
        [.. Trees().Select(tree => new GrantedFolder(tree.Id, tree.Label, tree.Folder)).OrderBy(folder => folder.Label, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The folders the reader has granted and Android still holds the permission for.</summary>
    public IReadOnlyList<TreeGrant> Trees()
    {
        var trees = new List<TreeGrant>();
        var permissions = _resolver.PersistedUriPermissions;
        if (permissions is null) return trees;

        foreach (var permission in permissions)
        {
            var uri = permission.Uri;
            if (uri is null || !permission.IsReadPermission || !DocumentsContract.IsTreeUri(uri)) continue;
            trees.Add(ToGrant(uri));
        }

        return trees;
    }

    /// <summary>
    /// The granted folder that holds a document, if there is one: the widest, when two grants overlap, because
    /// it reaches the most of what the document links to.
    /// </summary>
    /// <param name="document">Where the document is.</param>
    public TreeGrant? FindTreeContaining(StorageLocation document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Trees()
            .Where(tree => tree.Location?.Contains(document) == true)
            .MinBy(tree => tree.Location!.Path.Length);
    }

    /// <summary>The granted folder whose mirror holds <paramref name="path"/>, if there is one.</summary>
    /// <param name="path">A path in the mirror.</param>
    public TreeGrant? FindTreeOfMirroredPath(string path)
    {
        var full = Path.GetFullPath(path);
        return Trees().FirstOrDefault(tree => MirrorNames.IsInside(Path.GetFullPath(tree.Folder), full));
    }

    /// <summary>Remembers the folder offered for a single-file document, for when the reader says <em>Allow</em>.</summary>
    /// <param name="path">The path the document was opened at.</param>
    /// <param name="offer">What was offered.</param>
    public void Remember(string path, PendingOffer offer)
    {
        if (_offers.Count >= MaxRememberedOffers) _offers.Clear();
        _offers[Path.GetFullPath(path)] = offer;
    }

    /// <inheritdoc />
    public async Task<FolderGrantResult> GrantAsync(string documentPath)
    {
        if (!_offers.TryGetValue(Path.GetFullPath(documentPath), out var pending)) return new FolderGrantResult(FolderGrantStatus.Failed);

        try
        {
            // The chooser opens at the document's own folder (EXTRA_INITIAL_URI), so granting it is one tap.
            using var chooser = TreeChooser();
            var folder = DocumentsContract.BuildDocumentUri(StorageUris.ExternalStorageAuthority, pending.Offer.Folder.DocumentId);
            _ = chooser.PutExtra(DocumentsContract.ExtraInitialUri, folder);
            var uri = (await ActivityResults.LaunchAsync(chooser).ConfigureAwait(false))?.Data;
            if (uri is null) return new FolderGrantResult(FolderGrantStatus.Cancelled);

            var tree = Persist(uri);
            if (tree is null) return new FolderGrantResult(FolderGrantStatus.Failed);

            // The folder the reader chose may be a different one from the one offered. If it holds the document, the
            // document is copied first, so that it can be shown at once; if it does not, the grant is kept all the same.
            if (pending.Document.Location is { } location && tree.Location?.Contains(location) == true)
            {
                var path = await Task.Run(() => MirrorDocument(tree, pending.Document)).ConfigureAwait(false);
                if (path is not null)
                {
                    SyncInBackground(tree);
                    return new FolderGrantResult(FolderGrantStatus.Granted, path, tree.Root);
                }
            }

            SyncInBackground(tree);
            return new FolderGrantResult(FolderGrantStatus.GrantedWithoutDocument);
        }
        catch (Exception ex) when (Providers.IsFailure(ex) || ex is InvalidOperationException)
        {
            LogGrantFailed(Logger, ex);
            return new FolderGrantResult(FolderGrantStatus.Failed);
        }
    }

    /// <summary>
    /// Asks the reader for a folder to open (the toolbar's Open folder), keeps the grant and mirrors it. The files at the
    /// top of the folder are copied before this returns, so that the document the viewer opens first is there; the rest
    /// follows in the background, and the tree is refreshed when it is done.
    /// </summary>
    /// <returns>The root of the mirror, or null when the reader backed out or the folder could not be kept.</returns>
    public async Task<string?> PickFolderAsync()
    {
        try
        {
            using var chooser = TreeChooser();
            var uri = (await ActivityResults.LaunchAsync(chooser).ConfigureAwait(false))?.Data;
            if (uri is null) return null;

            var tree = Persist(uri);
            if (tree is null) return null;

            var outcome = await SyncTreeAsync(tree, maxDepth: 0).ConfigureAwait(false);
            ReportCap(tree, outcome);
            SyncInBackground(tree);
            return tree.Root;
        }
        catch (Exception ex) when (Providers.IsFailure(ex) || ex is InvalidOperationException)
        {
            LogGrantFailed(Logger, ex);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task ForgetAsync(string id)
    {
        var tree = Trees().FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
        if (tree is not null)
        {
            try
            {
                _resolver.ReleasePersistableUriPermission(tree.Uri, ActivityFlags.GrantReadUriPermission);
            }
            catch (Exception ex) when (Providers.IsFailure(ex))
            {
                LogForgetFailed(Logger, ex);
            }
        }

        // Wait for a sync that is copying into the mirror, then remove it: the id is a name made here, but a path from outside is never trusted.
        var gate = GateOf(id);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            DeleteMirror(id);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<FolderSyncResult> SyncAsync(string documentPath)
    {
        var tree = FindTreeOfMirroredPath(documentPath);
        if (tree is null) return FolderSyncResult.None;

        var outcome = await SyncTreeAsync(tree, maxDepth: -1).ConfigureAwait(false);
        ReportCap(tree, outcome);
        return new FolderSyncResult(outcome.Copied, outcome.Changed);
    }

    /// <summary>Copies one document of a granted folder into its mirror now, ahead of the rest, and returns where it went.</summary>
    /// <param name="tree">The granted folder that holds the document.</param>
    /// <param name="document">The document, whose location lies inside <paramref name="tree"/>.</param>
    /// <exception cref="ArgumentException">The document has no location.</exception>
    public string? MirrorDocument(TreeGrant tree, DocumentInfo document)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(document);

        var location = document.Location ?? throw new ArgumentException("The document has no location.", nameof(document));
        var relative = tree.Location?.RelativePathOf(location);
        if (relative is null) return null;

        EnsureMirror(tree);
        var entry = new RemoteEntry(relative, document.Size, document.ModifiedMs, location.DocumentId);

        // The address the document came with first (the app holds a grant for it); the tree's grant if that has lapsed.
        return new MirrorEngine(new SingleDocumentSource(_resolver, document.Uri), tree.Root, tree.Scratch, Log.For<MirrorEngine>()).CopyOne(entry)
            ?? new MirrorEngine(new ContentTreeSource(_resolver, tree.Uri), tree.Root, tree.Scratch, Log.For<MirrorEngine>()).CopyOne(entry);
    }

    /// <summary>
    /// Copies a document that is in no granted folder into a folder of its own under the single-file area, so that it
    /// can be opened by a path. The folder is named from the address, so that two documents of the same name stay apart and
    /// the same document is replaced by its newer copy.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The path of the copy, or null when it could not be copied (unreadable, or over the 32 MB a document may be).</returns>
    public string? CopySingle(DocumentInfo document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var name = MirrorNames.IsSafeName(document.Name) ? document.Name : "document.md";
        var folder = Path.Combine(_singles, MirrorNames.SingleFolderName(document.Uri.ToString() ?? name));
        var engine = new MirrorEngine(new SingleDocumentSource(_resolver, document.Uri), folder, Path.Combine(_singles, ".tmp"), Log.For<MirrorEngine>());
        return engine.CopyOne(new RemoteEntry(name, document.Size, document.ModifiedMs));
    }

    /// <summary>Brings a mirror up to date without anybody waiting for it, and tells the viewer when something changed.</summary>
    /// <param name="tree">The granted folder.</param>
    public void SyncInBackground(TreeGrant tree) => Background.Run(
        async () =>
        {
            var outcome = await SyncTreeAsync(tree, maxDepth: -1).ConfigureAwait(false);
            ReportCap(tree, outcome);
            if (outcome.Changed) _inbox.NotifyTreeChanged();
        },
        Logger);

    /// <summary>
    /// Start-up housekeeping: drops the mirrors whose grant no longer exists (the reader took it away in Android's
    /// settings, or the app was reinstalled over a restored backup), and the single-file copies that no history row points at.
    /// </summary>
    /// <param name="historyPaths">The paths of the history entries.</param>
    public void CleanUp(IEnumerable<string> historyPaths)
    {
        ArgumentNullException.ThrowIfNull(historyPaths);

        var granted = Trees().Select(tree => tree.Id).ToHashSet(StringComparer.Ordinal);
        if (Directory.Exists(_mirrors))
        {
            foreach (var directory in Directory.EnumerateDirectories(_mirrors))
            {
                var id = Path.GetFileName(directory);
                if (!granted.Contains(id)) DeleteMirror(id);
            }
        }

        if (!Directory.Exists(_singles)) return;

        var kept = historyPaths.Select(Path.GetFullPath).ToList();
        foreach (var directory in Directory.EnumerateDirectories(_singles))
        {
            var prefix = directory + Path.DirectorySeparatorChar;
            var scratch = string.Equals(Path.GetFileName(directory), ".tmp", StringComparison.Ordinal);
            if (!scratch && kept.Exists(path => path.StartsWith(prefix, StringComparison.Ordinal))) continue;

            TryDelete(directory);
        }
    }

    private static Intent TreeChooser()
    {
        // Read only, and kept: the folder is only ever read, and the permission has to outlive the app's process.
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        _ = intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);
        return intent;
    }

    /// <summary>Keeps the permission for a folder the reader chose and makes its mirror's folder; null if Android or the provider will not keep it.</summary>
    private TreeGrant? Persist(Android.Net.Uri uri)
    {
        try
        {
            _resolver.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
        }
        catch (Exception ex) when (Providers.IsFailure(ex))
        {
            LogPersistFailed(Logger, ex);
            _inbox.Notify("That folder cannot be kept for later.");
            return null;
        }

        var tree = ToGrant(uri);
        EnsureMirror(tree);
        return tree;
    }

    private TreeGrant ToGrant(Android.Net.Uri uri)
    {
        var address = uri.ToString() ?? throw new InvalidOperationException("A tree has no address.");
        var root = DocumentsContract.GetTreeDocumentId(uri) ?? throw new InvalidOperationException("A tree has no root document.");
        var location = string.Equals(uri.Authority, StorageUris.ExternalStorageAuthority, StringComparison.Ordinal)
            ? StorageUris.FromExternalStorageId(root)
            : null;
        var id = MirrorNames.MirrorId(address);
        return new TreeGrant(uri, id, root, location, Path.Combine(_mirrors, id));
    }

    /// <summary>Makes the mirror's folders, named after the granted folder (its provider's name for it when the storage path says none).</summary>
    private void EnsureMirror(TreeGrant tree)
    {
        _ = Directory.CreateDirectory(tree.Folder);
        if (Directory.EnumerateDirectories(tree.Folder).Any(d => !Path.GetFileName(d).StartsWith('.'))) return;

        _ = Directory.CreateDirectory(Path.Combine(tree.Folder, MirrorNames.RootFolderName(tree.Location?.Name ?? QueryName(tree))));
    }

    /// <summary>The provider's display name for the granted folder, for a provider whose ids say nothing.</summary>
    private string? QueryName(TreeGrant tree)
    {
        try
        {
            var document = DocumentsContract.BuildDocumentUriUsingTree(tree.Uri, tree.RootDocumentId);
            using var cursor = Providers.Query(_resolver, document, [DocumentColumns.ColumnDisplayName]);
            return cursor?.MoveToFirst() == true ? cursor.GetString(0) : null;
        }
        catch (Exception ex) when (Providers.IsFailure(ex))
        {
            LogNameFailed(Logger, ex);
            return null;
        }
    }

    /// <summary>One sync of one granted folder at a time: two copying into the same mirror would only repeat each other's work.</summary>
    private async Task<SyncOutcome> SyncTreeAsync(TreeGrant tree, int maxDepth)
    {
        var gate = GateOf(tree.Id);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                EnsureMirror(tree);
                var engine = new MirrorEngine(new ContentTreeSource(_resolver, tree.Uri), tree.Root, tree.Scratch, Log.For<MirrorEngine>());
                return engine.Sync(tree.RootDocumentId, maxDepth);
            })
                .ConfigureAwait(false);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <summary>Writes a capped sync to the log, and tells the reader once per folder and run of the app (a toast at every return would be a nag).</summary>
    private void ReportCap(TreeGrant tree, SyncOutcome outcome)
    {
        if (outcome.Cap == MirrorCap.None) return;

        LogCapped(Logger, tree.Label, outcome.Cap);
        if (_reportedCaps.TryAdd(tree.Id, outcome.Cap))
        {
            _inbox.Notify(outcome.Cap == MirrorCap.Entries
                ? string.Format(CultureInfo.CurrentCulture, "{0} is large: only the first {1:N0} items are shown.", tree.Label, MirrorWalker.MaxEntries)
                : string.Format(CultureInfo.CurrentCulture, "{0} is large: only the first {1:N0} MB of it is shown.", tree.Label, MirrorWalker.MaxBytes / (1024 * 1024)));
        }
    }

    private SemaphoreSlim GateOf(string id) => _gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

    private void DeleteMirror(string id)
    {
        // An id is a folder name made by MirrorNames.MirrorId; anything else is not one of ours and is left alone.
        if (id.Length == 0 || id.Contains('/', StringComparison.Ordinal) || id.Contains('\\', StringComparison.Ordinal) || id.StartsWith('.')) return;
        TryDelete(Path.Combine(_mirrors, id));
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogDeleteFailed(Logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Asking for a folder failed")]
    private static partial void LogGrantFailed(ILogger log, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A folder could not be kept for later")]
    private static partial void LogPersistFailed(ILogger log, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The permission for a folder could not be given back")]
    private static partial void LogForgetFailed(ILogger log, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The name of a granted folder could not be read")]
    private static partial void LogNameFailed(ILogger log, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirror of {Folder} was cut short ({Cap}); the rest of the folder is not shown")]
    private static partial void LogCapped(ILogger log, string folder, MirrorCap cap);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A mirror folder could not be removed")]
    private static partial void LogDeleteFailed(ILogger log, Exception ex);
}
