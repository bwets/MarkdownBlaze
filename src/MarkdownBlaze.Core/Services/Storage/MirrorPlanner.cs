namespace MarkdownBlaze.Services.Storage;

/// <summary>
/// Works out what a sync of a mirror has to do, from the provider's listing and what the mirror holds. A file is
/// copied when it is new, or its size or last-modified time differs from the copy's (the copy is given the
/// provider's time, so an unchanged file matches exactly); a time the provider does not give counts as changed,
/// because a file whose age is unknown cannot be shown to be the same. Files the listing no longer has are deleted —
/// but only when the listing was complete, since a walk that stopped early (a folder that would not list, a cap)
/// says nothing about the files it did not reach.
/// </summary>
internal static class MirrorPlanner
{
    /// <summary>Compares the listing with the mirror.</summary>
    /// <param name="remote">What the provider lists (see <see cref="MirrorWalker"/>).</param>
    /// <param name="local">What the mirror holds.</param>
    /// <param name="complete">Whether the listing covers the whole folder; only then may files be deleted.</param>
    /// <param name="first">The path to copy before any other (the document the reader is opening), if it needs copying.</param>
    public static MirrorPlan Plan(IEnumerable<RemoteEntry> remote, IEnumerable<LocalEntry> local, bool complete, string? first = null)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(local);

        var held = new Dictionary<string, LocalEntry>(StringComparer.Ordinal);
        foreach (var entry in local) held[entry.Path] = entry;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var copy = new List<RemoteEntry>();
        foreach (var entry in remote)
        {
            // A provider may list two files under one name; the mirror can hold only one of them.
            if (!seen.Add(entry.Path)) continue;
            if (!held.TryGetValue(entry.Path, out var have) || IsChanged(entry, have)) copy.Add(entry);
        }

        copy.Sort((a, b) => Compare(a, b, first));

        var delete = new List<string>();
        if (complete)
        {
            foreach (var path in held.Keys)
            {
                if (!seen.Contains(path)) delete.Add(path);
            }

            delete.Sort(StringComparer.Ordinal);
        }

        return new MirrorPlan(copy, delete);
    }

    private static bool IsChanged(RemoteEntry remote, LocalEntry local) =>
        remote.LastModifiedMs <= 0 || remote.Size != local.Size || remote.LastModifiedMs != local.LastModifiedMs;

    private static int Compare(RemoteEntry a, RemoteEntry b, string? first)
    {
        var aFirst = string.Equals(a.Path, first, StringComparison.Ordinal);
        var bFirst = string.Equals(b.Path, first, StringComparison.Ordinal);
        if (aFirst != bFirst) return aFirst ? -1 : 1;

        var depth = Depth(a.Path).CompareTo(Depth(b.Path));
        return depth != 0 ? depth : string.CompareOrdinal(a.Path, b.Path);
    }

    private static int Depth(string path) => path.Count(c => c == '/');
}
