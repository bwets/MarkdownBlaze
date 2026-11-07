using MarkdownBlaze.Services.Storage;

namespace MarkdownBlaze.Tests;

/// <summary>
/// Where a document is, worked out from what Android hands over (a documents provider's id, or MediaStore's columns), and
/// whether that is inside a folder the reader has granted. Everything is text in, text out: a provider that is not
/// understood gives null, and a null leaves the document a single file.
/// </summary>
public sealed class StorageUrisTests
{
    [Theory]
    [InlineData("primary:Documents/wiki/index.md", "primary", "Documents/wiki/index.md")]
    [InlineData("1234-ABCD:notes/a.md", "1234-ABCD", "notes/a.md")]
    [InlineData("primary:", "primary", "")]
    [InlineData("primary:a//b/", "primary", "a/b")]
    [InlineData("home:wiki/index.md", "primary", "Documents/wiki/index.md")]
    public void An_external_storage_id_names_a_volume_and_a_path(string id, string volume, string path)
    {
        var location = StorageUris.FromExternalStorageId(id);

        Assert.NotNull(location);
        Assert.Equal(volume, location.Volume);
        Assert.Equal(path, location.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-colon")]
    [InlineData(":Documents")]
    [InlineData("primary:../etc/passwd")]
    [InlineData("primary:a/./b")]
    [InlineData("primary:a/../../b")]
    [InlineData("primary:a\0b")]
    public void An_id_that_is_not_a_path_on_a_volume_is_not_understood(string? id) => Assert.Null(StorageUris.FromExternalStorageId(id));

    [Fact]
    public void An_opaque_id_reads_as_a_volume_that_no_granted_folder_contains()
    {
        // "msf:1234" (what the Files app hands out for some documents) has the shape of a volume and a path. It names a
        // volume that is nobody's tree, so it is never found inside one: a single file, as an unknown id is.
        var location = StorageUris.FromExternalStorageId("msf:1234")!;

        Assert.False(StorageUris.FromExternalStorageId("primary:")!.Contains(location));
    }

    [Theory]
    [InlineData("raw:/storage/emulated/0/Download/wiki/x.md", "primary", "Download/wiki/x.md")]
    [InlineData("raw:/storage/emulated/10/Documents/x.md", "primary", "Documents/x.md")]
    [InlineData("raw:/sdcard/Download/x.md", "primary", "Download/x.md")]
    [InlineData("raw:/storage/self/primary/Download/x.md", "primary", "Download/x.md")]
    [InlineData("raw:/storage/1234-ABCD/Download/x.md", "1234-ABCD", "Download/x.md")]
    public void A_raw_downloads_id_is_a_path_of_the_shared_storage(string id, string volume, string path)
    {
        var location = StorageUris.FromDownloadsId(id);

        Assert.NotNull(location);
        Assert.Equal(volume, location.Volume);
        Assert.Equal(path, location.Path);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("msf:1234")]
    [InlineData("raw:relative/x.md")]
    [InlineData("raw:/data/user/0/net.bwets.markdownblaze/files/x.md")]
    [InlineData("raw:/storage/emulated/0/../../../data/x.md")]
    [InlineData("raw:/")]
    [InlineData(null)]
    public void Any_other_downloads_id_says_nothing_about_a_folder(string? id) => Assert.Null(StorageUris.FromDownloadsId(id));

    [Theory]
    [InlineData("external_primary", "Documents/wiki/", "index.md", "primary", "Documents/wiki/index.md")]
    [InlineData("external_primary", "Documents/", "a.md", "primary", "Documents/a.md")]
    [InlineData("external_primary", "", "a.md", "primary", "a.md")]
    [InlineData("1234-abcd", "Notes/", "a.md", "1234-abcd", "Notes/a.md")]
    public void MediaStore_columns_give_the_place_of_a_file(string volumeName, string relativePath, string displayName, string volume, string path)
    {
        var location = StorageUris.FromMediaStore(volumeName, relativePath, displayName);

        Assert.NotNull(location);
        Assert.Equal(volume, location.Volume);
        Assert.Equal(path, location.Path);
    }

    [Theory]
    [InlineData(null, "Documents/", "a.md")]
    [InlineData("external", "Documents/", "a.md")]
    [InlineData("external_primary", "Documents/", null)]
    [InlineData("external_primary", "Documents/", "")]
    [InlineData("external_primary", "Documents/", "../a.md")]
    [InlineData("external_primary", "Documents/", "x/a.md")]
    [InlineData("external_primary", "../../data/", "a.md")]
    public void MediaStore_columns_that_are_missing_or_hostile_are_not_understood(string? volumeName, string? relativePath, string? displayName) =>
        Assert.Null(StorageUris.FromMediaStore(volumeName, relativePath, displayName));

    [Theory]
    [InlineData("/storage/emulated/0/x.md", "primary", "x.md")]
    [InlineData("/storage/emulated/0", "primary", "")]
    public void A_file_path_of_the_shared_storage_is_a_location(string path, string volume, string relative)
    {
        var location = StorageUris.FromFilePath(path);

        Assert.NotNull(location);
        Assert.Equal(volume, location.Volume);
        Assert.Equal(relative, location.Path);
    }

    [Theory]
    [InlineData("/storage/emulated")]
    [InlineData("/storage")]
    [InlineData("/data/data/x")]
    [InlineData("storage/emulated/0/x.md")]
    public void A_path_outside_the_shared_storage_is_not(string path) => Assert.Null(StorageUris.FromFilePath(path));

    [Fact]
    public void A_granted_folder_contains_what_is_below_it_on_its_volume()
    {
        var tree = StorageUris.FromExternalStorageId("primary:Documents/wiki")!;

        Assert.True(tree.Contains(StorageUris.FromExternalStorageId("primary:Documents/wiki/index.md")!));
        Assert.True(tree.Contains(StorageUris.FromExternalStorageId("primary:Documents/wiki/a/b/c.md")!));
        Assert.True(tree.Contains(StorageUris.FromExternalStorageId("PRIMARY:documents/WIKI/x.md")!));
    }

    [Fact]
    public void A_granted_folder_does_not_contain_a_sibling_whose_name_starts_the_same_way()
    {
        var tree = StorageUris.FromExternalStorageId("primary:Documents/wiki")!;

        Assert.False(tree.Contains(StorageUris.FromExternalStorageId("primary:Documents/wiki2/x.md")!));
        Assert.False(tree.Contains(StorageUris.FromExternalStorageId("primary:Documents/wikix")!));
        Assert.False(tree.Contains(StorageUris.FromExternalStorageId("primary:Documents/x.md")!));
        Assert.False(tree.Contains(StorageUris.FromExternalStorageId("primary:Documents")!));
        Assert.False(tree.Contains(tree));
    }

    [Fact]
    public void A_folder_on_another_volume_is_another_folder()
    {
        var tree = StorageUris.FromExternalStorageId("primary:Documents/wiki")!;

        Assert.False(tree.Contains(StorageUris.FromExternalStorageId("1234-ABCD:Documents/wiki/x.md")!));
    }

    [Fact]
    public void A_whole_volume_contains_everything_on_it()
    {
        var volume = StorageUris.FromExternalStorageId("primary:")!;

        Assert.True(volume.Contains(StorageUris.FromExternalStorageId("primary:a.md")!));
        Assert.False(volume.Contains(StorageUris.FromExternalStorageId("other:a.md")!));
    }

    [Fact]
    public void The_path_of_a_document_below_its_tree_is_what_the_mirror_repeats()
    {
        var tree = StorageUris.FromExternalStorageId("primary:Documents/wiki")!;

        Assert.Equal("sub/a.md", tree.RelativePathOf(StorageUris.FromExternalStorageId("primary:Documents/wiki/sub/a.md")!));
        Assert.Null(tree.RelativePathOf(StorageUris.FromExternalStorageId("primary:Documents/other/a.md")!));
    }

    [Fact]
    public void A_folder_is_named_to_the_reader_by_its_path_and_a_card_is_added_to_the_name()
    {
        Assert.Equal("Documents/wiki", StorageUris.FromExternalStorageId("primary:Documents/wiki")!.Label);
        Assert.Equal("notes (1234-ABCD)", StorageUris.FromExternalStorageId("1234-ABCD:notes")!.Label);
        Assert.Equal("primary", StorageUris.FromExternalStorageId("primary:")!.Label);
    }

    [Fact]
    public void The_parent_of_a_document_is_its_folder_and_the_top_of_a_volume_has_none()
    {
        var document = StorageUris.FromExternalStorageId("primary:Documents/wiki/index.md")!;

        Assert.Equal("Documents/wiki", document.Parent!.Path);
        Assert.Equal("Documents", document.Parent.Parent!.Path);
        Assert.Equal("", document.Parent.Parent.Parent!.Path);
        Assert.Null(document.Parent.Parent.Parent.Parent);
        Assert.Equal("primary:Documents/wiki", document.Parent.DocumentId);
        Assert.Equal("index.md", document.Name);
    }

    [Fact]
    public void A_folder_is_offered_for_a_document_in_it()
    {
        var offer = FolderOffer.For(StorageUris.FromExternalStorageId("primary:Documents/wiki/index.md")!, restrictedRoots: true);

        Assert.NotNull(offer);
        Assert.Equal("Documents/wiki", offer.Label);
        Assert.Equal("primary:Documents/wiki", offer.Folder.DocumentId);
    }

    [Fact]
    public void Nothing_is_offered_for_a_document_at_the_top_of_a_volume() =>
        Assert.Null(FolderOffer.For(StorageUris.FromExternalStorageId("primary:index.md")!, restrictedRoots: false));

    [Fact]
    public void The_download_folder_itself_is_offered_only_where_the_chooser_allows_it()
    {
        var document = StorageUris.FromDownloadsId("raw:/storage/emulated/0/Download/x.md")!;

        Assert.Null(FolderOffer.For(document, restrictedRoots: true));
        Assert.NotNull(FolderOffer.For(document, restrictedRoots: false));
        Assert.NotNull(FolderOffer.For(StorageUris.FromDownloadsId("raw:/storage/emulated/0/Download/wiki/x.md")!, restrictedRoots: true));
    }
}
