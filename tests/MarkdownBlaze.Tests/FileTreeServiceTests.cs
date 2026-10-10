using MarkdownBlaze.Models;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>The sidebar tree: what it lists, what it hides, and how it opens itself around the current document.</summary>
public sealed class FileTreeServiceTests : IDisposable
{
    private readonly TempFolder _root = new();
    private readonly FileTreeService _tree = new();

    public void Dispose() => _root.Dispose();

    [Fact]
    public void Folders_come_first_then_documents_each_in_alphabetical_order_ignoring_case()
    {
        MakeFolders("beta", "Alpha");
        MakeFiles("z.md", "A.markdown", "b.txt", "c.mdx");

        _tree.SetRoot(_root.Path);

        Assert.Equal(["Alpha", "beta", "A.markdown", "b.txt", "c.mdx", "z.md"], Labels(Top()));
        Assert.All(Top().Take(2), node => Assert.Equal(TreeNodeKind.Folder, node.Kind));
        Assert.All(Top().Skip(2), node => Assert.Equal(TreeNodeKind.File, node.Kind));
    }

    [Fact]
    public void Only_documents_the_viewer_can_open_are_listed()
    {
        MakeFiles("a.md", "b.png", "c.cs", "d.json", "e.MD", "noextension");

        _tree.SetRoot(_root.Path);

        Assert.Equal(["a.md", "e.MD"], Labels(Top()));
    }

    [Fact]
    public void Folders_that_are_never_worth_showing_are_left_out()
    {
        MakeFolders(".git", ".svn", ".hg", ".vs", ".vscode", ".idea", "node_modules", "bin", "obj", "__pycache__", ".anything-with-a-dot", "docs");

        _tree.SetRoot(_root.Path);

        Assert.Equal(["docs"], Labels(Top()));
    }

    [SkippableFact]
    public void A_folder_marked_hidden_is_left_out()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        MakeFolders("visible", "secret");
        File.SetAttributes(Path.Combine(_root.Path, "secret"), FileAttributes.Directory | FileAttributes.Hidden);

        _tree.SetRoot(_root.Path);

        Assert.Equal(["visible"], Labels(Top()));
    }

    [Fact]
    public void A_folder_lists_its_contents_only_once_it_is_open()
    {
        MakeFiles("sub/inner.md", "top.md");
        var sub = Path.Combine(_root.Path, "sub");
        _tree.SetRoot(_root.Path);

        var closed = Top().Single(n => n.Kind == TreeNodeKind.Folder);
        Assert.False(closed.Expanded);
        Assert.Empty(closed.Children);

        _tree.Toggle(sub);
        var open = Top().Single(n => n.Kind == TreeNodeKind.Folder);
        Assert.True(open.Expanded);
        Assert.Equal(["inner.md"], Labels(open.Children));

        _tree.Toggle(sub);
        Assert.False(Top().Single(n => n.Kind == TreeNodeKind.Folder).Expanded);
    }

    [Fact]
    public void The_folders_leading_to_the_current_document_are_opened_for_it()
    {
        var deep = _root.File("a/b/c/doc.md", "# deep");
        _ = _root.File("a/other.md", "x");
        _ = _root.File("z/unrelated.md", "x");
        _tree.SetRoot(_root.Path);

        var top = _tree.Nodes(deep);

        var a = Child(top, "a");
        var b = Child(a.Children, "b");
        var c = Child(b.Children, "c");
        Assert.True(a.Expanded);
        Assert.True(b.Expanded);
        Assert.True(c.Expanded);
        Assert.False(Child(top, "z").Expanded);

        var doc = c.Children.Single();
        Assert.True(doc.Current);
        Assert.True(doc.Expanded); // the current document's panel shows its outline
        Assert.False(Child(a.Children, "other.md").Current);
    }

    [Fact]
    public void Revealing_happens_once_per_document_so_an_opened_folder_can_be_shut()
    {
        var deep = _root.File("a/doc.md", "# d");
        _tree.SetRoot(_root.Path);
        var a = Path.Combine(_root.Path, "a");
        Assert.True(Child(_tree.Nodes(deep), "a").Expanded);

        _tree.Toggle(a);

        Assert.False(Child(_tree.Nodes(deep), "a").Expanded);
    }

    [Fact]
    public void Moving_to_another_document_reveals_that_one()
    {
        var first = _root.File("a/one.md", "# 1");
        var second = _root.File("b/two.md", "# 2");
        _tree.SetRoot(_root.Path);

        _ = _tree.Nodes(first);
        var nodes = _tree.Nodes(second);

        Assert.True(Child(nodes, "b").Expanded);
        Assert.True(Child(nodes, "a").Expanded); // stays open: nobody shut it
    }

    [Fact]
    public void The_outline_of_the_current_document_can_be_shut_and_is_forgotten_when_another_is_opened()
    {
        var one = _root.File("one.md", "# 1");
        var two = _root.File("two.md", "# 2");
        _tree.SetRoot(_root.Path);
        Assert.True(Panel(one, one).Expanded);

        _tree.ToggleOutline(one);
        Assert.False(Panel(one, one).Expanded);

        _tree.ToggleOutline(one);
        Assert.True(Panel(one, one).Expanded);

        _tree.ToggleOutline(one);
        Assert.False(Panel(one, one).Expanded);
        _ = _tree.Nodes(two); // reading another document forgets the shut outline
        Assert.True(Panel(one, one).Expanded);
    }

    [Fact]
    public void Only_the_current_document_has_an_open_panel()
    {
        var one = _root.File("one.md", "# 1");
        var two = _root.File("two.md", "# 2");
        _tree.SetRoot(_root.Path);

        Assert.True(Panel(one, one).Current);
        Assert.False(Panel(one, two).Current);
        Assert.False(Panel(one, two).Expanded);
    }

    [Fact]
    public void Pointing_the_tree_at_a_different_root_starts_it_closed_again()
    {
        using var first = new TempFolder();
        _ = first.File("sub/x.md", "x");
        _ = _root.File("other/y.md", "y");
        _tree.SetRoot(first.Path);
        _tree.Toggle(Path.Combine(first.Path, "sub"));
        Assert.True(Child(Top(), "sub").Expanded);

        _tree.SetRoot(_root.Path);

        Assert.Equal(_root.Path, _tree.Root);
        Assert.False(Child(Top(), "other").Expanded);

        _tree.SetRoot(first.Path);
        Assert.False(Child(Top(), "sub").Expanded);
    }

    [Fact]
    public void Setting_the_same_root_again_re_reads_the_folder_but_keeps_what_was_opened()
    {
        _ = _root.File("sub/x.md", "x");
        _tree.SetRoot(_root.Path);
        _tree.Toggle(Path.Combine(_root.Path, "sub"));
        _ = _root.File("sub/added.md", "added");

        _tree.SetRoot(_root.Path);

        var sub = Child(Top(), "sub");
        Assert.True(sub.Expanded);
        Assert.Equal(["added.md", "x.md"], Labels(sub.Children));
    }

    [Fact]
    public void Refresh_picks_up_files_added_meanwhile()
    {
        _ = _root.File("a.md", "a");
        _tree.SetRoot(_root.Path);
        Assert.Equal(["a.md"], Labels(Top()));

        _ = _root.File("b.md", "b");
        Assert.Equal(["a.md"], Labels(Top())); // listings are cached until asked to look again
        _tree.Refresh();

        Assert.Equal(["a.md", "b.md"], Labels(Top()));
    }

    [Fact]
    public void A_folder_that_cannot_be_read_shows_as_empty()
    {
        _ = _root.File("a.md", "a");
        _tree.SetRoot(_root.Path);
        Directory.Delete(_root.Path, recursive: true);

        Assert.Empty(Top());
    }

    [Fact]
    public void A_root_that_is_not_a_folder_is_ignored()
    {
        _tree.SetRoot(_root.Path);

        _tree.SetRoot(Path.Combine(_root.Path, "missing"));
        _tree.SetRoot("");
        _tree.SetRoot(folder: null);

        Assert.Equal(_root.Path, _tree.Root);
    }

    [Fact]
    public void A_document_sets_the_root_to_the_folder_it_is_in()
    {
        var doc = _root.File("sub/doc.md", "# d");

        _tree.SetRootFromFile(doc);

        Assert.Equal(Path.Combine(_root.Path, "sub"), _tree.Root);
        Assert.Null(new FileTreeService().Root);
    }

    [Fact]
    public void Before_any_root_there_is_nothing_to_show()
    {
        Assert.Null(_tree.Root);
        Assert.Empty(Top());
        Assert.False(_tree.Contains(_root.File("a.md", "a")));
    }

    [Fact]
    public void A_document_outside_the_root_gets_a_panel_of_its_own()
    {
        var inside = _root.File("inside.md", "# i");
        using var elsewhere = new TempFolder();
        var outside = elsewhere.File("outside.md", "# o");
        _tree.SetRoot(_root.Path);

        Assert.True(_tree.Contains(inside));
        Assert.False(_tree.Contains(outside));
        Assert.Null(_tree.Detached(inside));
        Assert.Null(_tree.Detached(currentFile: null));

        var detached = _tree.Detached(outside);
        Assert.NotNull(detached);
        Assert.Equal(TreeNodeKind.File, detached.Kind);
        Assert.Equal(outside, detached.Path);
        Assert.True(detached.Current);
        Assert.True(detached.Expanded);
    }

    [Fact]
    public void A_sibling_folder_with_the_same_prefix_is_not_inside_the_root()
    {
        _tree.SetRoot(_root.Path);

        Assert.False(_tree.Contains(_root.Path + "-sibling" + Path.DirectorySeparatorChar + "x.md"));
    }

    [Fact]
    public void Every_change_to_the_tree_is_announced()
    {
        _ = _root.File("a/x.md", "x");
        var raised = 0;
        _tree.Changed += (_, _) => raised++;

        _tree.SetRoot(_root.Path);
        _tree.Toggle(Path.Combine(_root.Path, "a"));
        _tree.ToggleOutline(Path.Combine(_root.Path, "a", "x.md"));
        _tree.Refresh();

        Assert.Equal(4, raised);
    }

    private IReadOnlyList<TreeNode> Top() => _tree.Nodes(currentFile: null);

    private static TreeNode Child(IEnumerable<TreeNode> nodes, string label) =>
        nodes.Single(node => string.Equals(node.Label, label, StringComparison.Ordinal));

    private TreeNode Panel(string current, string file) =>
        FindFile(_tree.Nodes(current), file) ?? throw new InvalidOperationException("no panel for " + file);

    private static TreeNode? FindFile(IReadOnlyList<TreeNode> nodes, string file)
    {
        foreach (var node in nodes)
        {
            if (node.Kind == TreeNodeKind.File && string.Equals(node.Path, file, StringComparison.OrdinalIgnoreCase)) return node;
            var inner = FindFile(node.Children, file);
            if (inner is not null) return inner;
        }

        return null;
    }

    private static List<string> Labels(IEnumerable<TreeNode> nodes) => [.. nodes.Select(node => node.Label)];

    private void MakeFolders(params string[] names)
    {
        foreach (var name in names)
            _ = Directory.CreateDirectory(Path.Combine(_root.Path, name));
    }

    private void MakeFiles(params string[] names)
    {
        foreach (var name in names)
            _ = _root.File(name, "x");
    }
}
