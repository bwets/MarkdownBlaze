namespace MarkdownBlaze.Services.Storage;

/// <summary>
/// Walks a granted folder, breadth first, and lists the files the mirror should hold: Markdown and the pictures a
/// document can show, outside hidden folders and <c>node_modules</c>, each under the per-file limit, with names
/// that passed <see cref="MirrorNames"/>. A folder is entered only if its own name passes, which is also what keeps
/// the walk out of anything the provider named oddly. The walk is capped: an enormous folder (a whole notes archive, a
/// code checkout) must not fill the phone or take minutes, and the cap is reported so the reader is told.
/// </summary>
internal static class MirrorWalker
{
    /// <summary>
    /// The most entries (files and folders, wanted or not) a walk looks at. A reader's notes folder is hundreds;
    /// 20,000 is a large wiki, and a provider answers one query per folder, so the walk's own cost is bounded too.
    /// </summary>
    public const int MaxEntries = 20_000;

    /// <summary>
    /// The most the mirror holds for one granted folder: the sum of the sizes of the files it copies. It lives in
    /// the app's private storage, which the reader cannot see and Android will not clean, so it is held to a
    /// size that a phone can spare.
    /// </summary>
    public const long MaxBytes = 500L * 1024 * 1024;

    /// <summary>Lists the files of the folder with id <paramref name="rootId"/>.</summary>
    /// <param name="source">The provider.</param>
    /// <param name="rootId">The provider's id for the granted folder.</param>
    /// <param name="maxDepth">How many levels below the root to enter; 0 lists the files of the root only, a negative number everything.</param>
    /// <param name="cancellationToken">Stops the walk between two folders.</param>
    public static WalkResult Walk(IFolderSource source, string rootId, int maxDepth, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var files = new List<RemoteEntry>();
        var complete = true;
        var entries = 0;
        long bytes = 0;
        var cap = MirrorCap.None;

        var queue = new Queue<(string Id, string Path, int Depth)>();
        queue.Enqueue((rootId, "", 0));
        while (queue.Count > 0 && cap == MirrorCap.None)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (id, path, depth) = queue.Dequeue();

            var listed = source.List(id);
            if (listed is null)
            {
                complete = false;
                continue;
            }

            foreach (var entry in listed)
            {
                if (++entries > MaxEntries)
                {
                    cap = MirrorCap.Entries;
                    break;
                }

                var childPath = path.Length == 0 ? entry.Name : path + "/" + entry.Name;
                if (entry.IsFolder)
                {
                    if (!MirrorNames.ShouldEnterFolder(entry.Name)) continue;
                    if (maxDepth >= 0 && depth >= maxDepth)
                    {
                        complete = false;
                        continue;
                    }

                    queue.Enqueue((entry.Id, childPath, depth + 1));
                }
                else if (MirrorNames.ShouldMirrorFile(entry.Name, entry.Size))
                {
                    var size = Math.Max(entry.Size, 0);
                    if (bytes + size > MaxBytes)
                    {
                        cap = MirrorCap.Size;
                        break;
                    }

                    bytes += size;
                    files.Add(new RemoteEntry(childPath, entry.Size, entry.LastModifiedMs, entry.Id));
                }
            }
        }

        return new WalkResult(files, cap, complete && cap == MirrorCap.None);
    }
}
