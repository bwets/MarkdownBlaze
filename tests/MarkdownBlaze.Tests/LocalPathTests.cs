using MarkdownBlaze.Security;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>SEC-4: which files a document may make the app read, and how much of them.</summary>
public class LocalPathTests
{
    /// <summary>A 1x1 PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly MarkdownService Markdown = new();

    // ---- the path helper ------------------------------------------------------------------------

    [Theory]
    [InlineData("\\\\server\\share\\x.png")]
    [InlineData("//server/share/x.png")]
    [InlineData("\\/server/share/x.png")]
    [InlineData("/\\server\\share\\x.png")]
    [InlineData("\\\\?\\C:\\Users\\x.png")]
    [InlineData("\\\\.\\pipe\\x")]
    [InlineData("//?/C:/x.png")]
    [InlineData("  \\\\server\\share\\x.png")]
    public void Unc_and_device_paths_are_not_safe(string path)
    {
        Assert.True(LocalPath.IsUncOrDevice(path.TrimStart()));
        Assert.False(LocalPath.IsSafe(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("a\0b.png")]
    [InlineData("a\nb.png")]
    public void Empty_and_control_characters_are_not_safe(string? path) => Assert.False(LocalPath.IsSafe(path));

    [Theory]
    [InlineData("images/a.png")]
    [InlineData("../images/a.png")]
    [InlineData("C:\\pictures\\a.png")]
    [InlineData("/home/user/a.png")]
    [InlineData("a b.png")]
    public void Plain_paths_are_safe(string path) => Assert.True(LocalPath.IsSafe(path));

    [Theory]
    [InlineData("\\\\server\\share\\x", "server")]
    [InlineData("//SERVER/share/x", "SERVER")]
    [InlineData("\\\\server", "server")]
    [InlineData("\\\\?\\C:\\x", null)]
    [InlineData("\\\\.\\pipe", null)]
    [InlineData("C:\\x", null)]
    [InlineData(null, null)]
    public void The_server_of_a_unc_path(string? path, string? server) =>
        Assert.Equal(server, LocalPath.UncServer(path));

    [Fact]
    public void Resolve_refuses_unsafe_paths_and_resolves_relative_ones()
    {
        using var temp = new TempFolder();
        Assert.Null(LocalPath.Resolve("\\\\server\\share\\x.png", temp.Path));
        Assert.Null(LocalPath.Resolve("//server/share/x.png", temp.Path));
        Assert.Equal(Path.Combine(temp.Path, "a", "b.png"), LocalPath.Resolve("a/b.png", temp.Path));
        Assert.Equal(Path.GetFullPath(Path.Combine(temp.Path, "..", "b.png")), LocalPath.Resolve("../b.png", temp.Path));
    }

    [SkippableFact]
    public void Resolve_refuses_windows_device_names()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        using var temp = new TempFolder();
        // The Win32 layer treats CON.png as the console, and GetFullPath says so by returning \\.\CON.
        Assert.Null(LocalPath.Resolve("CON.png", temp.Path));
        Assert.Null(LocalPath.Resolve("nul", temp.Path));
    }

    [Theory]
    [InlineData(".png", "image/png")]
    [InlineData(".PNG", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".svg", "image/svg+xml")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".bmp", "image/bmp")]
    [InlineData(".ico", "image/x-icon")]
    [InlineData(".avif", "image/avif")]
    public void Every_allowed_image_type_has_a_mime_type(string extension, string mime)
    {
        Assert.Contains(extension, LocalPath.ImageExtensions, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(mime, LocalPath.MimeFor(extension));
    }

    [Fact]
    public void There_is_no_mime_type_for_anything_else()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => LocalPath.MimeFor(".txt"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => LocalPath.MimeFor(""));
    }

    [Fact]
    public void Opening_an_image_applies_the_size_limits()
    {
        using var temp = new TempFolder();
        _ = temp.File("small.png", Png);
        var big = temp.File("big.png", new byte[2000]);
        Assert.Equal(2000, new FileInfo(big).Length);

        Assert.Equal(
            LocalPath.ImageOutcome.Allowed,
            LocalPath.TryOpenImage("small.png", temp.Path, 1000, 1000, out var stream, out var mediaType));
        using (stream)
        {
            Assert.Equal("image/png", mediaType);
            Assert.Equal(Png.Length, stream!.Length);
        }

        // over the per-image cap
        Assert.Equal(LocalPath.ImageOutcome.TooLarge, LocalPath.Check("big.png", temp.Path, 1000, 10_000, out var length));
        Assert.Equal(0, length);

        // within the cap for an image, but not what is left of the document's budget
        Assert.Equal(LocalPath.ImageOutcome.TooLarge, LocalPath.Check("small.png", temp.Path, 1000, Png.Length - 1, out _));
    }

    [Fact]
    public void Checking_an_image_reports_its_size_and_keeps_nothing_open()
    {
        using var temp = new TempFolder();
        var path = temp.File("small.png", Png);

        Assert.Equal(LocalPath.ImageOutcome.Allowed, LocalPath.Check("small.png", temp.Path, 1000, 1000, out var length));
        Assert.Equal(Png.Length, length);
        File.Delete(path); // would fail if a handle were still open without FileShare.Delete
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Without_a_folder_only_rooted_paths_resolve()
    {
        using var temp = new TempFolder();
        var rooted = temp.File("pic.png", Png);

        Assert.Equal(LocalPath.ImageOutcome.Missing, LocalPath.Check("pic.png", string.Empty, 1000, 1000, out _));
        Assert.Equal(LocalPath.ImageOutcome.Allowed, LocalPath.Check(rooted, string.Empty, 1000, 1000, out _));
    }

    [Fact]
    public void Reading_a_missing_file_is_missing_not_blocked()
    {
        using var temp = new TempFolder();
        Assert.Equal(
            LocalPath.ImageOutcome.Missing,
            LocalPath.TryOpenImage("nope.png", temp.Path, 1000, 1000, out _, out _));
    }

    // ---- through the renderer -------------------------------------------------------------------

    private static string Render(TempFolder temp, string markdown)
    {
        var path = temp.File("doc.md", markdown);
        return Markdown.Render(path)!.BodyHtml;
    }

    private static int Count(string text, string needle) => text.Split(needle).Length - 1;

    [Fact]
    public void A_relative_png_next_to_the_document_gets_an_asset_address()
    {
        using var temp = new TempFolder();
        _ = temp.File("pic.png", Png);
        _ = temp.File("sub/deep.png", Png);

        var html = Render(temp, "![a](pic.png) ![b](sub/deep.png) ![c](./pic.png?x=1#y)");

        Assert.Equal(3, Count(html, "src=\"mdasset://doc/"));
        Assert.DoesNotContain("base64", html, StringComparison.Ordinal); // no byte of a picture is carried in the page
        Assert.DoesNotContain("mdimg-", html, StringComparison.Ordinal); // every token was swapped back
    }

    [Fact]
    public void A_file_with_an_image_name_but_not_an_image_extension_is_not_shown()
    {
        using var temp = new TempFolder();
        _ = temp.File("notes.txt", "secret");
        _ = temp.File("id_rsa", "-----BEGIN OPENSSH PRIVATE KEY-----");

        var html = Render(temp, "![a](notes.txt) ![b](id_rsa)");

        Assert.DoesNotContain("c2VjcmV0", html, StringComparison.Ordinal); // base64 of "secret"
        Assert.DoesNotContain("octet-stream", html, StringComparison.Ordinal);
        Assert.Equal(2, Count(html, "data-md-skipped=\"blocked\""));
    }

    [Fact]
    public void A_traversal_to_an_existing_text_file_is_refused()
    {
        using var temp = new TempFolder();
        var secret = temp.File("secret/passwords.txt", "hunter2");
        var doc = temp.File("docs/doc.md", "![](../secret/passwords.txt)");

        var html = Markdown.Render(doc)!.BodyHtml;

        Assert.True(File.Exists(secret));
        Assert.DoesNotContain(Convert.ToBase64String("hunter2"u8.ToArray()), html, StringComparison.Ordinal);
        Assert.Contains("data-md-skipped=\"blocked\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rooted_path_to_a_non_image_is_refused()
    {
        using var temp = new TempFolder();
        var secret = temp.File("secret.txt", "hunter2");

        var html = Render(temp, $"![]({secret.Replace('\\', '/')})");

        Assert.DoesNotContain(Convert.ToBase64String("hunter2"u8.ToArray()), html, StringComparison.Ordinal);
        Assert.Contains("data-md-skipped=\"blocked\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("//attacker.invalid/share/x.png")]
    [InlineData("\\\\attacker.invalid\\share\\x.png")]
    [InlineData("file://attacker.invalid/share/x.png")]
    [InlineData("\\\\?\\C:\\Windows\\win.ini")]
    [InlineData("\\\\.\\pipe\\x.png")]
    public void Unc_and_device_paths_are_refused_without_being_touched(string url)
    {
        using var temp = new TempFolder();

        var html = Render(temp, $"![x](<{url.Replace("\\", "\\\\", StringComparison.Ordinal)}>)");

        Assert.Contains("data-md-skipped=\"blocked\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker.invalid", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_oversize_image_is_replaced_by_a_marked_placeholder()
    {
        using var temp = new TempFolder();
        var big = temp.File("big.png", Png);
        using (var stream = new FileStream(big, FileMode.Open, FileAccess.Write))
            stream.SetLength(LocalPath.MaxImageBytes + 1);

        var html = Render(temp, "![huge](big.png)");

        Assert.Contains("data-md-skipped=\"too-large\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"data:image/svg+xml;base64,", html, StringComparison.Ordinal); // the placeholder
        Assert.DoesNotContain("mdasset:", html, StringComparison.Ordinal); // and no address that would serve it
    }

    [Fact]
    public void Remote_and_inline_images_are_left_alone()
    {
        using var temp = new TempFolder();
        var html = Render(temp, "![r](https://example.com/a.png) ![d](data:image/png;base64,AAAA)");

        Assert.Contains("src=\"https://example.com/a.png\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"data:image/png;base64,AAAA\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-md-skipped", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_that_does_not_exist_is_left_as_written()
    {
        using var temp = new TempFolder();
        var html = Render(temp, "![gone](gone.png)");

        Assert.Contains("src=\"gone.png\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-md-skipped", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Dropped_text_has_no_folder_so_relative_images_are_not_resolved()
    {
        // The working directory must not stand in for the document's folder.
        using var temp = new TempFolder();
        _ = temp.File("pic.png", Png);
        var before = Environment.CurrentDirectory;
        Environment.CurrentDirectory = temp.Path;
        try
        {
            var html = Markdown.RenderText("dropped.md", "![x](pic.png)").BodyHtml;
            Assert.Contains("src=\"pic.png\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Environment.CurrentDirectory = before;
        }
    }

    [Fact]
    public void An_address_that_looks_like_a_token_is_only_a_relative_address_to_a_file_that_is_no_picture()
    {
        using var temp = new TempFolder();
        _ = temp.File("pic.png", Png);

        // There is no token in the markup any more: every image is looked at by the same code, whoever wrote it.
        var html = Render(temp, "![a](pic.png)\n\n<img src=\"mdimg-00000000000000000000000000000000-0\">");

        Assert.DoesNotContain("mdimg-", html, StringComparison.Ordinal);
        Assert.Equal(1, html.Split("data-md-skipped=\"blocked\"").Length - 1); // not an image type
        Assert.Equal(1, html.Split("src=\"mdasset://").Length - 1); // and the one real picture
    }
}
