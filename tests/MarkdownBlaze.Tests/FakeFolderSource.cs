using MarkdownBlaze.Services.Storage;

namespace MarkdownBlaze.Tests;

/// <summary>
/// A documents provider in memory: folders and files by id, as a granted folder would list them. The tests use it to
/// say what a provider answers, including what a hostile one would.
/// </summary>
internal sealed class FakeFolderSource : IFolderSource
{
    private readonly Dictionary<string, List<ListedEntry>> _folders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _content = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unlistable = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unreadable = new(StringComparer.Ordinal);

    /// <summary>The id of the granted folder.</summary>
    public const string Root = "root";

    /// <summary>How many times the provider was asked to list a folder.</summary>
    public int Listings { get; private set; }

    /// <summary>How many times the provider was asked for a file's content.</summary>
    public int Reads { get; private set; }

    /// <summary>Adds a file to a folder; the id of the file is <c>folder/name</c> unless one is given.</summary>
    public FakeFolderSource File(string folder, string name, string content, long modified = 1000, string? id = null)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return File(folder, name, bytes, modified, id);
    }

    /// <summary>Adds a file with bytes as its content.</summary>
    public FakeFolderSource File(string folder, string name, byte[] content, long modified = 1000, string? id = null)
    {
        var fileId = id ?? (folder + "/" + name);
        Add(folder, new ListedEntry(name, IsFolder: false, content.Length, modified, fileId));
        _content[fileId] = content;
        return this;
    }

    /// <summary>Adds a subfolder to a folder; its id is <c>folder/name</c> unless one is given.</summary>
    public FakeFolderSource Folder(string parent, string name, string? id = null)
    {
        var folderId = id ?? (parent + "/" + name);
        Add(parent, new ListedEntry(name, IsFolder: true, 0, 0, folderId));
        if (!_folders.ContainsKey(folderId)) _folders[folderId] = [];
        return this;
    }

    /// <summary>Changes a file's content (and so, if asked, its time), as an edit on the device would.</summary>
    public void Edit(string id, string content, long modified)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        _content[id] = bytes;
        foreach (var entries in _folders.Values)
        {
            var index = entries.FindIndex(e => string.Equals(e.Id, id, StringComparison.Ordinal));
            if (index >= 0) entries[index] = entries[index] with { Size = bytes.Length, LastModifiedMs = modified };
        }
    }

    /// <summary>Removes a file, as a delete on the device would.</summary>
    public void Remove(string id)
    {
        _ = _content.Remove(id);
        foreach (var entries in _folders.Values) _ = entries.RemoveAll(e => string.Equals(e.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Makes a file say a different size in the listing than it has, as a provider that guesses might.</summary>
    public void Misreport(string id, long size)
    {
        foreach (var entries in _folders.Values)
        {
            var index = entries.FindIndex(e => string.Equals(e.Id, id, StringComparison.Ordinal));
            if (index >= 0) entries[index] = entries[index] with { Size = size };
        }
    }

    /// <summary>Makes a file listed but impossible to read, as a document whose provider has died would be.</summary>
    public void FailToOpen(string id) => _ = _unreadable.Add(id);

    /// <summary>Makes listing a folder fail, as a revoked grant or a pulled card would.</summary>
    public void FailToList(string folderId) => _ = _unlistable.Add(folderId);

    /// <inheritdoc />
    public IReadOnlyList<ListedEntry>? List(string folderId)
    {
        Listings++;
        return (_unlistable.Contains(folderId) || !_folders.TryGetValue(folderId, out var entries)) ? null : [.. entries];
    }

    /// <inheritdoc />
    public Stream? Open(string fileId)
    {
        Reads++;
        return (!_unreadable.Contains(fileId) && _content.TryGetValue(fileId, out var bytes)) ? new MemoryStream(bytes) : null;
    }

    private void Add(string folder, ListedEntry entry)
    {
        if (!_folders.TryGetValue(folder, out var entries)) _folders[folder] = entries = [];
        entries.Add(entry);
    }
}
