using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarkdownBlaze.Services.Storage;

/// <summary>
/// Keeps a folder on disk equal to a granted folder of a documents provider, as far as the app needs it: the
/// Markdown and the pictures (see <see cref="MirrorNames"/>), nothing hidden, within the caps of <see cref="MirrorWalker"/>.
/// The rest of the app reads that folder by ordinary paths, which is why the security code that reasons about paths
/// (<c>LocalPath</c>, <c>ReferenceResolver</c>, <c>FileTreeService</c>) did not have to change for Android.
/// <para>
/// A sync lists the provider (<see cref="MirrorWalker"/>), compares the listing with what is on disk
/// (<see cref="MirrorPlanner"/>), copies what changed and deletes what is gone. A file is copied to a temporary file
/// and moved into place, so that a reader of the mirror — the file watcher, a render — sees the old file or the new one,
/// never half of one, and is given the provider's last-modified time, which is what the next sync compares. Every
/// destination is resolved through <see cref="MirrorNames.Resolve"/>, so nothing a provider names can land outside
/// the mirror. Nothing here throws for what a provider or a disk can do wrong: it is logged, counted, and the file left
/// for the next sync.
/// </para>
/// <para>Not thread-safe: the host runs one sync of a mirror at a time.</para>
/// </summary>
internal sealed partial class MirrorEngine
{
    private readonly IFolderSource _source;
    private readonly string _root;
    private readonly string _temp;
    private readonly ILogger _log;

    /// <summary>Creates the engine for one granted folder.</summary>
    /// <param name="source">The provider.</param>
    /// <param name="root">The folder the granted folder is mirrored into; it is created if it is not there.</param>
    /// <param name="temp">A folder on the same volume for files being copied; it must not be inside <paramref name="root"/>.</param>
    /// <param name="log">Where problems are written.</param>
    public MirrorEngine(IFolderSource source, string root, string temp, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(temp);

        _source = source;
        _root = Path.GetFullPath(root);
        _temp = Path.GetFullPath(temp);
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>
    /// Brings the mirror up to date with the folder.
    /// </summary>
    /// <param name="rootId">The provider's id for the granted folder.</param>
    /// <param name="maxDepth">How deep to look (see <see cref="MirrorWalker.Walk"/>); a sync that does not look everywhere does not delete.</param>
    /// <param name="cancellationToken">Stops the sync between two files.</param>
    public SyncOutcome Sync(string rootId, int maxDepth, CancellationToken cancellationToken = default)
    {
        _ = Directory.CreateDirectory(_root);
        var walk = MirrorWalker.Walk(_source, rootId, maxDepth, cancellationToken);
        var plan = MirrorPlanner.Plan(walk.Files, ReadMirror(), walk.Complete);

        var copied = new List<string>();
        var skipped = 0;
        foreach (var entry in plan.Copy)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = CopyOne(entry);
            if (path is null) skipped++;
            else copied.Add(path);
        }

        var deleted = Delete(plan.Delete);
        return new SyncOutcome(copied, deleted, skipped, walk.Cap);
    }

    /// <summary>
    /// Copies one file into the mirror now, whether or not it changed: the document the reader is opening, which
    /// should be on screen before the rest of the folder has been looked at.
    /// </summary>
    /// <param name="entry">The file, as listed (or built by the host from what it knows of the document).</param>
    /// <returns>The path of the copy, or null when it was not copied (refused name, unreadable, too large).</returns>
    public string? CopyOne(RemoteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var target = MirrorNames.Resolve(_root, entry.Path);
        if (target is null)
        {
            LogRefusedName(_log, entry.Path.Length);
            return null;
        }

        try
        {
            _ = Directory.CreateDirectory(_temp);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var scratch = Path.Combine(_temp, Guid.NewGuid().ToString("N"));
            try
            {
                if (!WriteWithin(entry, scratch)) return null;

                if (entry.LastModifiedMs > 0) File.SetLastWriteTimeUtc(scratch, DateTimeOffset.FromUnixTimeMilliseconds(entry.LastModifiedMs).UtcDateTime);
                File.Move(scratch, target, overwrite: true);
                return target;
            }
            finally
            {
                if (File.Exists(scratch)) File.Delete(scratch);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCopyFailed(_log, ex);
            return null;
        }
    }

    /// <summary>Reads the provider's file into <paramref name="scratch"/>, giving up on a file over the document limit.</summary>
    private bool WriteWithin(RemoteEntry entry, string scratch)
    {
        using var input = _source.Open(entry.Id);
        if (input is null)
        {
            LogUnreadable(_log);
            return false;
        }

        using var output = new FileStream(scratch, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MarkdownService.MaxFileBytes)
            {
                LogTooLarge(_log);
                return false;
            }

            output.Write(buffer, 0, read);
        }

        return true;
    }

    /// <summary>What the mirror holds now: every file below the root, with its size and last-write time.</summary>
    private List<LocalEntry> ReadMirror()
    {
        var entries = new List<LocalEntry>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in new DirectoryInfo(_root).EnumerateFiles("*", options))
        {
            var relative = Path.GetRelativePath(_root, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
            entries.Add(new LocalEntry(relative, file.Length, new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds()));
        }

        return entries;
    }

    /// <summary>Removes the files the planner listed, and the folders that this leaves empty.</summary>
    private int Delete(IReadOnlyList<string> paths)
    {
        var deleted = 0;
        var folders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var relative in paths)
        {
            // The listing is of this folder on disk, so the path is one that exists; the check is for the day it is not.
            var full = Path.GetFullPath(Path.Combine(_root, relative));
            if (!MirrorNames.IsInside(_root, full)) continue;

            try
            {
                File.Delete(full);
                deleted++;
                for (var dir = Path.GetDirectoryName(full); dir is not null && MirrorNames.IsInside(_root, dir); dir = Path.GetDirectoryName(dir))
                    _ = folders.Add(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogDeleteFailed(_log, ex);
            }
        }

        // Deepest first, so a folder whose last subfolder has just gone is empty by the time it is looked at.
        foreach (var dir in folders.Reverse())
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogDeleteFailed(_log, ex);
            }
        }

        return deleted;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A name from the folder was refused ({Length} characters long); its file is not mirrored")]
    private static partial void LogRefusedName(ILogger log, int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A file of the folder could not be read; it is left for the next sync")]
    private static partial void LogUnreadable(ILogger log);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A file of the folder is over the size limit and is not mirrored")]
    private static partial void LogTooLarge(ILogger log);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A file could not be written into the mirror")]
    private static partial void LogCopyFailed(ILogger log, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A file or folder could not be removed from the mirror")]
    private static partial void LogDeleteFailed(ILogger log, Exception ex);
}
