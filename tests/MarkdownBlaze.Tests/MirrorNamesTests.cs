using MarkdownBlaze.Services.Storage;

namespace MarkdownBlaze.Tests;

/// <summary>
/// The names a documents provider gives are another app's text. These are the rules that keep them inside the mirror:
/// which names may be written at all, and that a path made from them never leaves the mirror's folder.
/// </summary>
public sealed class MirrorNamesTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData("index.md")]
    [InlineData("My notes (2024).md")]
    [InlineData("naïve — café.md")]
    [InlineData("日本語.md")]
    [InlineData("a:b.md")]
    [InlineData("..md")]
    [InlineData("a..b.md")]
    [InlineData("...hidden-looking-but-not-a-dot-segment.md")]
    public void An_ordinary_name_is_safe(string name) => Assert.True(MirrorNames.IsSafeName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b.md")]
    [InlineData("/etc/passwd")]
    [InlineData("../x.md")]
    [InlineData("..\\x.md")]
    [InlineData("a\\b.md")]
    [InlineData("a\0b.md")]
    [InlineData("a\nb.md")]
    [InlineData("a\tb.md")]
    [InlineData("a\u007fb.md")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public void A_name_that_is_empty_steps_upwards_or_holds_a_separator_or_control_character_is_refused(string? name) =>
        Assert.False(MirrorNames.IsSafeName(name));

    [Fact]
    public void A_name_longer_than_the_file_system_takes_is_refused()
    {
        Assert.True(MirrorNames.IsSafeName(new string('a', 255)));
        Assert.False(MirrorNames.IsSafeName(new string('a', 256)));

        // The limit is in bytes, not characters: 128 two-byte letters are 256 bytes.
        Assert.False(MirrorNames.IsSafeName(new string('é', 128)));
        Assert.True(MirrorNames.IsSafeName(new string('é', 127)));
    }

    [Fact]
    public void A_path_of_safe_names_resolves_below_the_root()
    {
        var root = Path.Combine(_folder.Path, "mirror");

        var full = MirrorNames.Resolve(root, "sub/dir/index.md");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "sub", "dir", "index.md")), full);
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("sub/../../escape.md")]
    [InlineData("sub/../escape.md")]
    [InlineData("./a.md")]
    [InlineData("/etc/passwd")]
    [InlineData("a//b.md")]
    [InlineData("a\\..\\..\\b.md")]
    [InlineData("a/b\0.md")]
    [InlineData("")]
    [InlineData(null)]
    public void A_path_that_steps_out_of_the_root_or_names_nothing_does_not_resolve(string? relative) =>
        Assert.Null(MirrorNames.Resolve(Path.Combine(_folder.Path, "mirror"), relative));

    [Fact]
    public void A_sibling_whose_name_starts_with_the_roots_is_not_inside_it()
    {
        var root = Path.GetFullPath(Path.Combine(_folder.Path, "mirror"));

        Assert.True(MirrorNames.IsInside(root, Path.Combine(root, "a.md")));
        Assert.False(MirrorNames.IsInside(root, root + "-other" + Path.DirectorySeparatorChar + "a.md"));
        Assert.False(MirrorNames.IsInside(root, root));
        Assert.False(MirrorNames.IsInside(root, Path.GetDirectoryName(root)!));
    }

    [Theory]
    [InlineData("sub", true)]
    [InlineData("Wiki Notes", true)]
    [InlineData(".git", false)]
    [InlineData(".obsidian", false)]
    [InlineData(".hidden", false)]
    [InlineData("node_modules", false)]
    [InlineData("Node_Modules", false)]
    [InlineData("node_modules2", true)]
    [InlineData("a/b", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    public void Hidden_folders_and_node_modules_are_not_entered(string name, bool enter) => Assert.Equal(enter, MirrorNames.ShouldEnterFolder(name));

    [Theory]
    [InlineData("index.md", 100, true)]
    [InlineData("INDEX.MD", 100, true)]
    [InlineData("notes.markdown", 100, true)]
    [InlineData("notes.txt", 100, true)]
    [InlineData("pic.png", 100, true)]
    [InlineData("pic.JPG", 100, true)]
    [InlineData("pic.svg", 100, true)]
    [InlineData("script.sh", 100, false)]
    [InlineData("archive.zip", 100, false)]
    [InlineData("noextension", 100, false)]
    [InlineData(".hidden.md", 100, false)]
    [InlineData("a/b.md", 100, false)]
    [InlineData("index.md", 0, true)]
    [InlineData("index.md", -1, true)]
    [InlineData("index.md", 33_554_432, true)]
    [InlineData("index.md", 33_554_433, false)]
    [InlineData("pic.png", 20_971_520, true)]
    [InlineData("pic.png", 20_971_521, false)]
    public void Only_markdown_and_pictures_under_their_limits_are_mirrored(string name, long size, bool mirrored) =>
        Assert.Equal(mirrored, MirrorNames.ShouldMirrorFile(name, size));

    [Fact]
    public void A_mirror_is_named_by_its_tree_the_same_way_every_time()
    {
        const string Tree = "content://com.android.externalstorage.documents/tree/primary%3ADocuments%2Fwiki";

        Assert.Equal(MirrorNames.MirrorId(Tree), MirrorNames.MirrorId(Tree));
        Assert.NotEqual(MirrorNames.MirrorId(Tree), MirrorNames.MirrorId(Tree + "2"), StringComparer.Ordinal);
        Assert.Matches("^t[0-9a-f]{16}$", MirrorNames.MirrorId(Tree));
    }

    [Theory]
    [InlineData("wiki", "wiki")]
    [InlineData("My Notes", "My Notes")]
    [InlineData("..", "folder")]
    [InlineData("a/b", "folder")]
    [InlineData(".git", "folder")]
    [InlineData("", "folder")]
    [InlineData(null, "folder")]
    public void The_mirror_of_a_folder_is_named_after_it_when_its_name_is_safe(string? name, string expected) =>
        Assert.Equal(expected, MirrorNames.RootFolderName(name));

    [Fact]
    public void Single_files_from_different_addresses_get_different_folders()
    {
        Assert.NotEqual(MirrorNames.SingleFolderName("content://a/1"), MirrorNames.SingleFolderName("content://a/2"), StringComparer.Ordinal);
        Assert.Equal(MirrorNames.SingleFolderName("content://a/1"), MirrorNames.SingleFolderName("content://a/1"));
    }
}
