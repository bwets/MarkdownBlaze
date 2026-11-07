using MarkdownBlaze.Services.Storage;

namespace MarkdownBlaze.Tests;

/// <summary>
/// The mirror of a granted folder, made on disk from an in-memory provider: what is copied and what is left, that a sync
/// only touches what changed, and that nothing a provider names can be written outside the mirror.
/// </summary>
public sealed class MirrorEngineTests : IDisposable
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string Root => Path.Combine(_folder.Path, "mirror", "tree", "wiki");
    private string Temp => Path.Combine(_folder.Path, "mirror", "tree", ".tmp");

    private MirrorEngine Engine(FakeFolderSource source) => new(source, Root, Temp);
    private string Mirrored(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    private IEnumerable<string> MirroredFiles() =>
        Directory.Exists(Root)
            ? Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(Root, f).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal)
            : [];

    [Fact]
    public void A_sync_copies_the_markdown_and_the_pictures_and_nothing_else()
    {
        var source = new FakeFolderSource()
            .File(FakeFolderSource.Root, "index.md", "# Index")
            .File(FakeFolderSource.Root, "pic.png", Png)
            .File(FakeFolderSource.Root, "script.sh", "rm -rf /")
            .File(FakeFolderSource.Root, "data.zip", "PK")
            .Folder(FakeFolderSource.Root, "sub")
            .File("root/sub", "page.md", "# Page");

        var outcome = Engine(source).Sync(FakeFolderSource.Root, maxDepth: -1);

        Assert.Equal(["index.md", "pic.png", "sub/page.md"], MirroredFiles(), StringComparer.Ordinal);
        Assert.Equal("# Index", File.ReadAllText(Mirrored("index.md")));
        Assert.Equal(Png, File.ReadAllBytes(Mirrored("pic.png")));
        Assert.Equal(3, outcome.Copied.Count);
        Assert.Equal(MirrorCap.None, outcome.Cap);
    }

    [Fact]
    public void Hidden_folders_and_node_modules_are_not_entered()
    {
        var source = new FakeFolderSource()
            .File(FakeFolderSource.Root, "index.md", "# Index")
            .Folder(FakeFolderSource.Root, ".git")
            .File("root/.git", "README.md", "inside git")
            .Folder(FakeFolderSource.Root, ".obsidian")
            .File("root/.obsidian", "notes.md", "inside obsidian")
            .Folder(FakeFolderSource.Root, "node_modules")
            .File("root/node_modules", "readme.md", "inside node_modules")
            .File(FakeFolderSource.Root, ".secret.md", "hidden file");

        _ = Engine(source).Sync(FakeFolderSource.Root, maxDepth: -1);

        Assert.Equal(["index.md"], MirroredFiles(), StringComparer.Ordinal);
    }

    [Fact]
    public void A_second_sync_copies_only_what_changed()
    {
        var source = new FakeFolderSource()
            .File(FakeFolderSource.Root, "a.md", "one")
            .File(FakeFolderSource.Root, "b.md", "two");
        var engine = Engine(source);
        _ = engine.Sync(FakeFolderSource.Root, maxDepth: -1);
        var readsAfterFirst = source.Reads;

        var again = engine.Sync(FakeFolderSource.Root, maxDepth: -1);

        Assert.Empty(again.Copied);
        Assert.False(again.Changed);
        Assert.Equal(readsAfterFirst, source.Reads);

        source.Edit("root/a.md", "one, edited", modified: 2000);
        var edited = engine.Sync(FakeFolderSource.Root, maxDepth: -1);

        Assert.Equal([Mirrored("a.md")], edited.Copied, StringComparer.Ordinal);
        Assert.True(edited.Changed);
        Assert.Equal("one, edited", File.ReadAllText(Mirrored("a.md")));
        Assert.Equal("two", File.ReadAllText(Mirrored("b.md")));
    }

    [Fact]
    public void A_copy_has_the_providers_time_so_that_the_next_sync_finds_it_unchanged()
    {
        var source = new FakeFolderSource().File(FakeFolderSource.Root, "a.md", "one", modified: 1_700_000_000_000);

        _ = Engine(source).Sync(FakeFolderSource.Root, maxDepth: -1);

        Assert.Equal(1_700_000_000_000, new DateTimeOffset(File.GetLastWriteTimeUtc(Mirrored("a.md"))).ToUnixTimeMilliseconds());
    }

    [Fact]
    public void A_file_that_disappeared_is_deleted_with_the_folders_it_leaves_empty()
    {
        var source = new FakeFolderSource()
            .File(FakeFolderSource.Root, "keep.md", "keep")
            .Folder(FakeFolderSource.Root, "old")
            .Folder("root/old", "deeper")
            .File("root/old/deeper", "gone.md", "gone");
        var engine = Engine(source);
        _ = engine.Sync(FakeFolderSource.Root, maxDepth: -1);
        Assert.True(File.Exists(Mirrored("old/deeper/gone.md")));

        source.Remove("root/old/deeper/gone.md");
        var outcome = engine.Sync(FakeFolderSource.Root, maxDepth: -1);

        Assert.Equal(1, outcome.Deleted);
        Assert.Equal(["keep.md"], MirroredFiles(), StringComparer.Ordinal);
        Assert.False(Directory.Exists(Mirrored("old")));
    }

    [Fact]
    public void A_sync_that_could_not_list_a_folder_deletes_nothing()
    {
        var source = new FakeFolderSource()
            .File(FakeFolderSource.Root, "keep.md", "keep")
            .Folder(FakeFolderSource.Root, "sub")
            .File("root/sub", "page.md", "page");
        var engine = Engine(source);
        _ = engine.Sync(FakeFolderSource.Root, maxDepth: -1);

        source.FailToList("root/sub");
        var outcome = engine.Sync(FakeFolderSource.Root, maxDepth: -1);

        Assert.Equal(0, outcome.Deleted);
        Assert.Equal(["keep.md", "sub/page.md"], MirroredFiles(), StringComparer.Ordinal);
    }

    [Fact]
    public void A_shallow_sync_copies_the_top_and_leaves_what_is_deeper_alone()
    {
        var source = new FakeFolderSource()
            .File(FakeFolderSource.Root, "index.md", "top")
            .Folder(FakeFolderSource.Root, "sub")
            .File("root/sub", "page.md", "deeper");
        var engine = Engine(source);
        _ = engine.Sync(FakeFolderSource.Root, maxDepth: -1);

        source.Remove("root/sub/page.md");
        _ = engine.Sync(FakeFolderSource.Root, maxDepth: 0);

        Assert.Equal(["index.md", "sub/page.md"], MirroredFiles(), StringComparer.Ordinal);
    }

    [Fact]
    public void A_first_pass_of_the_top_level_is_followed_by_the_rest()
    {
        var source = new FakeFolderSource()
            .File(FakeFolderSource.Root, "index.md", "top")
            .Folder(FakeFolderSource.Root, "sub")
            .File("root/sub", "page.md", "deeper");
        var engine = Engine(source);

        _ = engine.Sync(FakeFolderSource.Root, maxDepth: 0);
        Assert.Equal(["index.md"], MirroredFiles(), StringComparer.Ordinal);

        _ = engine.Sync(FakeFolderSource.Root, maxDepth: -1);
        Assert.Equal(["index.md", "sub/page.md"], MirroredFiles(), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("..")]
    [InlineData("a/../../escape.md")]
    [InlineData("/etc/cron.d/x.md")]
    [InlineData("..\\..\\escape.md")]
    [InlineData("sub\\..\\..\\escape.md")]
    [InlineData("nul\0.md")]
    [InlineData("")]
    public void A_name_that_would_leave_the_mirror_is_not_written_anywhere(string name)
    {
        var source = new FakeFolderSource()
            .File(FakeFolderSource.Root, "fine.md", "fine")
            .File(FakeFolderSource.Root, name, "hostile");

        _ = Engine(source).Sync(FakeFolderSource.Root, maxDepth: -1);

        // Nothing but the one good file exists anywhere under the scratch folder.
        var everything = Directory.EnumerateFiles(_folder.Path, "*", SearchOption.AllDirectories).Select(f => Path.GetFileName(f)).ToList();
        Assert.Equal(["fine.md"], everything, StringComparer.Ordinal);
    }

    [Fact]
    public void A_folder_with_a_hostile_name_is_not_entered()
    {
        var source = new FakeFolderSource()
            .File(FakeFolderSource.Root, "fine.md", "fine")
            .Folder(FakeFolderSource.Root, "..", id: "up")
            .File("up", "escape.md", "hostile")
            .Folder(FakeFolderSource.Root, "a/b", id: "slash")
            .File("slash", "escape2.md", "hostile");

        _ = Engine(source).Sync(FakeFolderSource.Root, maxDepth: -1);

        Assert.Equal(["fine.md"], Directory.EnumerateFiles(_folder.Path, "*", SearchOption.AllDirectories).Select(f => Path.GetFileName(f)), StringComparer.Ordinal);
    }

    [Fact]
    public void A_file_the_provider_will_not_give_is_counted_and_left_for_the_next_sync()
    {
        var source = new FakeFolderSource()
            .File(FakeFolderSource.Root, "fine.md", "fine")
            .File(FakeFolderSource.Root, "broken.md", "x", id: "nowhere");
        source.FailToOpen("nowhere"); // listed, but its content cannot be opened

        var outcome = Engine(source).Sync(FakeFolderSource.Root, maxDepth: -1);

        Assert.Equal(["fine.md"], MirroredFiles(), StringComparer.Ordinal);
        Assert.Equal(1, outcome.Skipped);
    }

    [Fact]
    public void A_file_that_turns_out_larger_than_it_said_is_not_kept()
    {
        var huge = new byte[(int)Services.MarkdownService.MaxFileBytes + 1];
        var source = new FakeFolderSource()
            .File(FakeFolderSource.Root, "fine.md", "fine")
            .File(FakeFolderSource.Root, "liar.md", huge, id: "liar")
            .File(FakeFolderSource.Root, "honest.md", huge, id: "honest");
        source.Misreport("liar", 5); // says it is small; the walk lets it in and the copy has to catch it

        _ = Engine(source).Sync(FakeFolderSource.Root, maxDepth: -1);

        // honest.md said how large it was and was never asked for.
        Assert.Equal(["fine.md"], MirroredFiles(), StringComparer.Ordinal);
        Assert.Equal(2, source.Reads);
        Assert.Empty(Directory.EnumerateFiles(Temp));
    }

    [Fact]
    public void One_document_can_be_copied_ahead_of_the_rest()
    {
        var source = new FakeFolderSource().File(FakeFolderSource.Root, "a.md", "one").Folder(FakeFolderSource.Root, "sub").File("root/sub", "target.md", "target");
        var engine = Engine(source);

        var path = engine.CopyOne(new RemoteEntry("sub/target.md", 6, 5000, "root/sub/target.md"));

        Assert.NotNull(path);
        Assert.Equal(Mirrored("sub/target.md"), path);
        Assert.Equal("target", File.ReadAllText(path));
        Assert.Equal(["sub/target.md"], MirroredFiles(), StringComparer.Ordinal);

        // The rest of the sync leaves it alone, since it is the same size and age.
        source.Edit("root/sub/target.md", "target", modified: 5000);
        var outcome = engine.Sync(FakeFolderSource.Root, maxDepth: -1);
        Assert.Equal([Mirrored("a.md")], outcome.Copied, StringComparer.Ordinal);
    }

    [Fact]
    public void A_document_with_a_hostile_path_is_refused_when_copied_ahead()
    {
        var source = new FakeFolderSource().File(FakeFolderSource.Root, "a.md", "one");

        Assert.Null(Engine(source).CopyOne(new RemoteEntry("../outside.md", 3, 0, "root/a.md")));
        Assert.False(File.Exists(Path.Combine(_folder.Path, "mirror", "tree", "outside.md")));
    }

    [Fact]
    public void A_replaced_file_is_never_seen_half_written()
    {
        var source = new FakeFolderSource().File(FakeFolderSource.Root, "a.md", "old");
        var engine = Engine(source);
        _ = engine.Sync(FakeFolderSource.Root, maxDepth: -1);

        source.Edit("root/a.md", "new content", modified: 2000);
        _ = engine.Sync(FakeFolderSource.Root, maxDepth: -1);

        Assert.Equal("new content", File.ReadAllText(Mirrored("a.md")));
        Assert.Empty(Directory.EnumerateFiles(Temp));
    }
}
