using System.Globalization;
using MarkdownBlaze.Services.Storage;

namespace MarkdownBlaze.Tests;

/// <summary>The walk of a granted folder: what it lists, in what order, and where it stops.</summary>
public sealed class MirrorWalkerTests
{
    /// <summary>A provider that answers each folder from a function.</summary>
    private sealed class ScriptedSource(Func<string, IReadOnlyList<ListedEntry>?> list) : IFolderSource
    {
        public IReadOnlyList<ListedEntry>? List(string folderId) => list(folderId);
        public Stream? Open(string fileId) => null;
    }

    private static ListedEntry File(string name, long size = 10) => new(name, IsFolder: false, size, 1000, "f:" + name);
    private static ListedEntry Dir(string name) => new(name, IsFolder: true, 0, 0, "d:" + name);
    private static string FileName(int number, string extension) => "f" + number.ToString(CultureInfo.InvariantCulture) + extension;

    [Fact]
    public void The_walk_lists_files_nearest_the_top_first_with_paths_below_the_folder()
    {
        var source = new ScriptedSource(id => id switch
        {
            "root" => [Dir("b"), File("z.md"), Dir("a")],
            "d:b" => [File("two.md"), Dir("deep")],
            "d:a" => [File("one.md")],
            "d:deep" => [File("three.md")],
            _ => null,
        });

        var result = MirrorWalker.Walk(source, "root", maxDepth: -1);

        Assert.Equal(["z.md", "b/two.md", "a/one.md", "b/deep/three.md"], result.Files.Select(f => f.Path), StringComparer.Ordinal);
        Assert.True(result.Complete);
        Assert.Equal(MirrorCap.None, result.Cap);
    }

    [Fact]
    public void A_depth_limit_stops_the_walk_and_says_it_did_not_see_everything()
    {
        var source = new ScriptedSource(id => id switch
        {
            "root" => [File("top.md"), Dir("sub")],
            "d:sub" => [File("deeper.md")],
            _ => null,
        });

        var shallow = MirrorWalker.Walk(source, "root", maxDepth: 0);

        Assert.Equal(["top.md"], shallow.Files.Select(f => f.Path), StringComparer.Ordinal);
        Assert.False(shallow.Complete);
    }

    [Fact]
    public void A_folder_that_will_not_list_makes_the_walk_incomplete_and_the_rest_is_still_listed()
    {
        var source = new ScriptedSource(id => id switch
        {
            "root" => [File("top.md"), Dir("bad"), Dir("good")],
            "d:good" => [File("ok.md")],
            _ => null,
        });

        var result = MirrorWalker.Walk(source, "root", maxDepth: -1);

        Assert.Equal(["top.md", "good/ok.md"], result.Files.Select(f => f.Path), StringComparer.Ordinal);
        Assert.False(result.Complete);
    }

    [Fact]
    public void Entries_that_are_hidden_or_not_documents_are_passed_over_without_making_the_walk_incomplete()
    {
        ListedEntry[] top = [Dir(".git"), Dir("node_modules"), File(".hidden.md"), File("run.sh"), File("a.md")];
        var source = new ScriptedSource(id => string.Equals(id, "root", StringComparison.Ordinal) ? top : null);

        var result = MirrorWalker.Walk(source, "root", maxDepth: -1);

        Assert.Equal(["a.md"], result.Files.Select(f => f.Path), StringComparer.Ordinal);
        Assert.True(result.Complete);
    }

    [Fact]
    public void A_folder_of_more_entries_than_the_limit_is_cut_off_and_reported()
    {
        var entries = Enumerable.Range(0, MirrorWalker.MaxEntries + 5).Select(i => File(FileName(i, ".md"), size: 1)).ToList();
        var source = new ScriptedSource(id => string.Equals(id, "root", StringComparison.Ordinal) ? entries : null);

        var result = MirrorWalker.Walk(source, "root", maxDepth: -1);

        Assert.Equal(MirrorCap.Entries, result.Cap);
        Assert.False(result.Complete);
        Assert.Equal(MirrorWalker.MaxEntries, result.Files.Count);
    }

    [Fact]
    public void Entries_that_are_not_wanted_count_towards_the_limit()
    {
        var entries = Enumerable.Range(0, MirrorWalker.MaxEntries + 1).Select(i => File(FileName(i, ".bin"))).ToList();
        var source = new ScriptedSource(id => string.Equals(id, "root", StringComparison.Ordinal) ? entries : null);

        Assert.Equal(MirrorCap.Entries, MirrorWalker.Walk(source, "root", maxDepth: -1).Cap);
    }

    [Fact]
    public void Files_that_add_up_to_more_than_the_size_limit_are_cut_off_and_reported()
    {
        const long Chunk = 30L * 1024 * 1024;
        var entries = Enumerable.Range(0, 20).Select(i => File(FileName(i, ".md"), Chunk)).ToList();
        var source = new ScriptedSource(id => string.Equals(id, "root", StringComparison.Ordinal) ? entries : null);

        var result = MirrorWalker.Walk(source, "root", maxDepth: -1);

        Assert.Equal(MirrorCap.Size, result.Cap);
        Assert.False(result.Complete);
        Assert.Equal(MirrorWalker.MaxBytes / Chunk, result.Files.Count);
        Assert.True(result.Files.Sum(f => f.Size) <= MirrorWalker.MaxBytes);
    }

    [Fact]
    public void A_file_larger_than_a_document_may_be_is_not_listed_and_is_not_a_cap()
    {
        ListedEntry[] top = [File("huge.md", Services.MarkdownService.MaxFileBytes + 1), File("a.md")];
        var source = new ScriptedSource(id => string.Equals(id, "root", StringComparison.Ordinal) ? top : null);

        var result = MirrorWalker.Walk(source, "root", maxDepth: -1);

        Assert.Equal(["a.md"], result.Files.Select(f => f.Path), StringComparer.Ordinal);
        Assert.Equal(MirrorCap.None, result.Cap);
    }

    [Fact]
    public void The_walk_stops_when_it_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _ = Assert.Throws<OperationCanceledException>(() => MirrorWalker.Walk(new ScriptedSource(_ => []), "root", maxDepth: -1, cts.Token));
    }
}
