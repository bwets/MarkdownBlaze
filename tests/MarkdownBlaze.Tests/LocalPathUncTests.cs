using MarkdownBlaze.Security;

namespace MarkdownBlaze.Tests;

/// <summary>
/// SEC-15: a document opened from <c>\\server\share\doc.md</c> has pictures on that share, and they are shown;
/// a picture on any other server is not. The decision is made on the text of the two paths — nothing here
/// touches the network, and no test reads a file through a UNC path.
/// </summary>
public sealed class LocalPathUncTests
{
    private const string Folder = "\\\\fileserver\\docs\\wiki";

    [Theory]
    [InlineData("\\\\fileserver\\docs\\wiki\\img\\a.png", Folder, true)]
    [InlineData("\\\\FILESERVER\\other\\a.png", Folder, true)] // the server is compared, not the share, and not by case
    [InlineData("//fileserver/docs/a.png", Folder, true)]
    [InlineData("\\\\fileserver\\docs\\a.png", "//fileserver/docs/wiki", true)]
    [InlineData("\\\\attacker.invalid\\share\\a.png", Folder, false)]
    [InlineData("\\\\fileserver.invalid\\docs\\a.png", Folder, false)] // a different name, not a prefix
    [InlineData("\\\\fileserver2\\docs\\a.png", Folder, false)]
    [InlineData("\\\\?\\C:\\x.png", Folder, false)] // a device path has no server
    [InlineData("\\\\?\\UNC\\fileserver\\docs\\a.png", Folder, false)]
    [InlineData("\\\\.\\pipe\\x", Folder, false)]
    [InlineData("C:\\x.png", Folder, false)] // not a UNC path at all
    [InlineData("\\\\fileserver\\docs\\a.png", "C:\\notes", false)] // the document is on a local disk
    [InlineData("\\\\fileserver\\docs\\a.png", "", false)] // dropped text has no folder
    [InlineData("\\\\fileserver\\docs\\a.png", null, false)]
    [InlineData(null, Folder, false)]
    public void An_image_is_on_the_documents_server_only_when_the_server_is_the_same(string? path, string? baseDir, bool expected) =>
        Assert.Equal(expected, LocalPath.IsOnServerOf(path, baseDir));

    [SkippableFact]
    public void A_relative_path_on_a_document_that_is_on_a_share_resolves_on_that_share()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        Assert.Equal("\\\\fileserver\\docs\\wiki\\img\\a.png", LocalPath.Resolve("img/a.png", Folder));
        Assert.Equal("\\\\fileserver\\docs\\shared\\a.png", LocalPath.Resolve("../shared/a.png", Folder));
        Assert.Equal("\\\\fileserver\\docs\\x.png", LocalPath.Resolve("..\\..\\..\\..\\x.png", Folder)); // above the share's root there is only the root
    }

    [SkippableFact]
    public void A_path_written_out_in_full_resolves_when_it_is_on_the_same_server_and_not_otherwise()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        Assert.Equal("\\\\fileserver\\docs\\a.png", LocalPath.Resolve("//fileserver/docs/a.png", Folder));
        Assert.Equal("\\\\FILESERVER\\other\\a.png", LocalPath.Resolve("\\\\FILESERVER\\other\\a.png", Folder)); // and the case it was written in is kept
        Assert.Null(LocalPath.Resolve("//attacker.invalid/share/a.png", Folder));
        Assert.Null(LocalPath.Resolve("\\\\attacker.invalid\\share\\a.png", Folder));
        Assert.Null(LocalPath.Resolve("\\\\?\\C:\\x.png", Folder));
        Assert.Null(LocalPath.Resolve("\\\\.\\pipe\\x.png", Folder));
        Assert.Null(LocalPath.Resolve("\\\\?\\UNC\\fileserver\\docs\\a.png", Folder));
    }

    [Theory]
    [InlineData("//fileserver/docs/a.png", "C:\\notes")]
    [InlineData("//fileserver/docs/a.png", "")]
    [InlineData("\\\\fileserver\\docs\\a.png", "C:\\notes")]
    public void On_a_local_document_every_unc_path_is_refused_as_it_was_before(string path, string baseDir) =>
        Assert.Null(LocalPath.Resolve(path, baseDir));

    [Fact]
    public void A_control_character_is_still_refused_wherever_the_document_is()
    {
        Assert.Null(LocalPath.Resolve("a\0b.png", Folder));
        Assert.Null(LocalPath.Resolve("a\nb.png", "C:\\notes"));
        Assert.Null(LocalPath.Resolve(string.Empty, "C:\\notes"));
        Assert.Null(LocalPath.Resolve("   ", "C:\\notes"));
    }
}
