namespace MarkdownBlaze.Services;

/// <summary>What a row in the sidebar tree is.</summary>
public enum TreeRowKind { Folder, File, Heading }

/// <summary>
/// One rendered line of the tree. <paramref name="Depth"/> is the indent level, <paramref name="Key"/>
/// is a stable id for the row (a path, or a path plus heading slug).
/// </summary>
public sealed record TreeRow(
    TreeRowKind Kind,
    string Key,
    string Label,
    int Depth,
    bool Expanded,
    bool Current,
    string? Path = null,
    string? Slug = null);

/// <summary>
/// The folder the reader is working in, and the tree of documents inside it.
/// <para>
/// Folders are read only when they are opened, so pointing the app at a large tree costs nothing
/// until it is explored. The chain of folders leading to the current document is opened
/// automatically, and the current document shows its own headings as children — everything else
/// stays shut.
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
        if (rootChanged) _expanded.Clear();
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

    /// <summary>
    /// The visible rows, in display order: the root's contents, opened folders expanded, and the
    /// current document followed by its headings.
    /// </summary>
    public IReadOnlyList<TreeRow> Rows(string? currentFile, IReadOnlyList<Heading> headings)
    {
        var rows = new List<TreeRow>();
        if (Root is null) return rows;

        var current = string.IsNullOrEmpty(currentFile) ? null : Path.GetFullPath(currentFile);
        OpenPathTo(current);
        Append(rows, Root, 0, current, headings);
        return rows;
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

    private void Append(List<TreeRow> rows, string folder, int depth, string? current,
                        IReadOnlyList<Heading> headings)
    {
        var (folders, files) = ChildrenOf(folder);

        foreach (var child in folders)
        {
            var open = _expanded.Contains(child);
            rows.Add(new TreeRow(TreeRowKind.Folder, child, Path.GetFileName(child), depth, open, false, child));
            if (open) Append(rows, child, depth + 1, current, headings);
        }

        foreach (var file in files)
        {
            var isCurrent = current is not null && string.Equals(file, current, StringComparison.OrdinalIgnoreCase);
            rows.Add(new TreeRow(TreeRowKind.File, file, Path.GetFileName(file), depth, isCurrent, isCurrent, file));

            // The open document carries its own outline as children.
            if (!isCurrent) continue;
            foreach (var heading in headings)
                rows.Add(new TreeRow(TreeRowKind.Heading, file + "#" + heading.Slug, heading.Text,
                                     depth + heading.Level, false, false, file, heading.Slug));
        }
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
