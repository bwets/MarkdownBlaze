namespace MarkdownBlaze.Services;

/// <summary>What a panel in the sidebar is.</summary>
public enum TreeNodeKind { Folder, File }

/// <summary>
/// One folder or document in the sidebar, drawn as a collapsible panel. An open folder carries what
/// is inside it in <paramref name="Children"/>; a document has none — when it is the one being read
/// and <paramref name="Expanded"/>, its panel shows the document's own headings instead.
/// </summary>
public sealed record TreeNode(
    TreeNodeKind Kind,
    string Path,
    string Label,
    bool Expanded,
    bool Current,
    IReadOnlyList<TreeNode> Children);

/// <summary>
/// The folder the reader is working in, and the tree of documents inside it.
/// <para>
/// Folders are read only when they are opened, so pointing the app at a large tree costs nothing
/// until it is explored. The chain of folders leading to the current document is opened
/// automatically, and the current document's panel is open on its own headings — everything else
/// stays shut until the reader opens it.
/// </para>
/// </summary>
public sealed class FileTreeService
{
    // Folders that are never worth showing in a document tree.
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".vscode", ".idea", "node_modules", "bin", "obj", "__pycache__",
    };

    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (List<string> Folders, List<string> Files)> _children =
        new(StringComparer.OrdinalIgnoreCase);

    // The document whose folders were last opened for it. Revealing happens once per document, not
    // on every render — otherwise a folder holding the open document could never be shut.
    private string? _revealed;

    // The document whose outline the reader has shut. Only the open document has one, so one is
    // enough; moving to another document forgets it.
    private string? _outlineShut;

    /// <summary>The folder at the top of the tree, or null before a document has been opened.</summary>
    public string? Root { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Points the tree at a folder. Re-reading is deliberate: <see cref="SetRoot"/> is called when the
    /// reader opens something, which is exactly when a stale listing would show.
    /// </summary>
    public void SetRoot(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        var full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        var rootChanged = !string.Equals(full, Root, StringComparison.OrdinalIgnoreCase);

        Root = full;
        _children.Clear();
        if (rootChanged) { _expanded.Clear(); _revealed = null; }
        _expanded.Add(full);
        Changed?.Invoke();
    }

    /// <summary>Sets the root from a document — the folder that document lives in.</summary>
    public void SetRootFromFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        SetRoot(Path.GetDirectoryName(Path.GetFullPath(filePath)));
    }

    /// <summary>Forgets cached listings so the next read picks up files added or removed meanwhile.</summary>
    public void Refresh()
    {
        _children.Clear();
        Changed?.Invoke();
    }

    public void Toggle(string folder)
    {
        if (!_expanded.Remove(folder)) _expanded.Add(folder);
        Changed?.Invoke();
    }

    /// <summary>Shuts or reopens the outline of the document being read.</summary>
    public void ToggleOutline(string file)
    {
        _outlineShut = string.Equals(_outlineShut, file, StringComparison.OrdinalIgnoreCase) ? null : file;
        Changed?.Invoke();
    }

    /// <summary>
    /// The panels inside the root, in display order: its folders, then its documents, each open
    /// folder carrying its own.
    /// </summary>
    public IReadOnlyList<TreeNode> Nodes(string? currentFile)
    {
        var current = Track(currentFile);
        if (Root is null) return [];

        if (!string.Equals(current, _revealed, StringComparison.OrdinalIgnoreCase))
        {
            _revealed = current;
            OpenPathTo(current);
        }
        return ChildNodes(Root, current);
    }

    /// <summary>
    /// The document being read when it lies outside the root — the bundled manual, or one reached by
    /// a link. It gets a panel of its own above the tree, so its outline is never out of reach.
    /// </summary>
    public TreeNode? Detached(string? currentFile)
    {
        var current = Track(currentFile);
        return current is null || Contains(current) ? null : FileNode(current, current);
    }

    /// <summary>Normalises the open document's path; a shut outline is forgotten once it changes.</summary>
    private string? Track(string? currentFile)
    {
        var current = string.IsNullOrEmpty(currentFile) ? null : Path.GetFullPath(currentFile);
        if (!string.Equals(current, _outlineShut, StringComparison.OrdinalIgnoreCase)) _outlineShut = null;
        return current;
    }

    /// <summary>True when the document sits inside the current root, so the tree can point at it.</summary>
    public bool Contains(string? filePath)
    {
        if (Root is null || string.IsNullOrWhiteSpace(filePath)) return false;
        var full = Path.GetFullPath(filePath);
        return full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Opens every folder between the root and the current document, so it is on screen.</summary>
    private void OpenPathTo(string? file)
    {
        if (file is null || Root is null || !Contains(file)) return;

        for (var dir = Path.GetDirectoryName(file); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            _expanded.Add(dir);
            if (string.Equals(dir, Root, StringComparison.OrdinalIgnoreCase)) break;
        }
    }

    private List<TreeNode> ChildNodes(string folder, string? current)
    {
        var (folders, files) = ChildrenOf(folder);
        var nodes = new List<TreeNode>(folders.Count + files.Count);

        foreach (var child in folders)
        {
            var open = _expanded.Contains(child);
            nodes.Add(new TreeNode(TreeNodeKind.Folder, child, Path.GetFileName(child), open, false,
                                   open ? ChildNodes(child, current) : []));
        }

        foreach (var file in files) nodes.Add(FileNode(file, current));
        return nodes;
    }

    private TreeNode FileNode(string file, string? current)
    {
        var isCurrent = current is not null && string.Equals(file, current, StringComparison.OrdinalIgnoreCase);
        var open = isCurrent && !string.Equals(file, _outlineShut, StringComparison.OrdinalIgnoreCase);
        return new TreeNode(TreeNodeKind.File, file, Path.GetFileName(file), open, isCurrent, []);
    }

    private (List<string> Folders, List<string> Files) ChildrenOf(string folder)
    {
        if (_children.TryGetValue(folder, out var cached)) return cached;

        var folders = new List<string>();
        var files = new List<string>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(folder))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith('.') || SkippedFolders.Contains(name)) continue;
                if (File.GetAttributes(dir).HasFlag(FileAttributes.Hidden)) continue;
                folders.Add(dir);
            }

            foreach (var file in Directory.EnumerateFiles(folder))
                if (MarkdownService.IsSupported(file)) files.Add(file);
        }
        catch
        {
            // A folder we may not read simply shows as empty.
        }

        folders.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
        files.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));

        var result = (folders, files);
        _children[folder] = result;
        return result;
    }
}
