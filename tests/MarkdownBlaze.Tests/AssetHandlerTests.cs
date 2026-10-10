using MarkdownBlaze.Security;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>
/// ARCH-5: the mdasset:// handler serves the pictures of the render that is on the page, by the rules of
/// <see cref="LocalPath"/> and no others.
/// </summary>
public sealed class AssetHandlerTests : IDisposable
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly TempFolder _folder = new();
    private readonly AssetScope _scope = new();
    private readonly AssetHandler _handler;

    public AssetHandlerTests()
    {
        _handler = new AssetHandler(_scope);
        _scope.Set(new AssetGrant(Token, _folder.Path));
    }

    public void Dispose() => _folder.Dispose();

    /// <summary>Asks for a path as the page would: the way the renderer writes the address.</summary>
    private Stream? Ask(string decodedPath, out string? contentType, string? token = null) =>
        _handler.Serve(AssetHandler.UrlFor(token ?? Token, decodedPath), out contentType);

    /// <summary>Makes a file the given size without writing it (a sparse file on the file systems that have them).</summary>
    private static void Resize(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
        stream.SetLength(length);
    }

    private static byte[] ReadAll(Stream? stream)
    {
        Assert.NotNull(stream);
        using (stream)
        using (var copy = new MemoryStream())
        {
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }

    // ---- what is served -----------------------------------------------------------------------------

    [Theory]
    [InlineData("a.png", "image/png")]
    [InlineData("a.JPG", "image/jpeg")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("a.svg", "image/svg+xml")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.bmp", "image/bmp")]
    [InlineData("a.ico", "image/x-icon")]
    [InlineData("a.avif", "image/avif")]
    public void An_image_is_served_with_its_own_content_and_media_type(string name, string mediaType)
    {
        _ = _folder.File(name, Png);

        var stream = Ask(name, out var contentType);

        Assert.Equal(mediaType, contentType);
        Assert.Equal(Png, ReadAll(stream));
    }

    [Fact]
    public void A_path_with_awkward_characters_survives_the_round_trip_through_the_address()
    {
        foreach (var name in new[] { "my pic.png", "a#b.png", "100%.png", "semi;colon.png", "d\u00e9j\u00e0 vu.png", "plus+sign.png", "sub/in folder/x&y.png" })
        {
            _ = _folder.File(name, Png);

            var url = AssetHandler.UrlFor(Token, name);

            Assert.Equal(Png, ReadAll(_handler.Serve(url, out _)));
            Assert.DoesNotContain("..", url[(url.IndexOf(Token, StringComparison.Ordinal) + Token.Length)..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_path_that_climbs_and_comes_back_inside_is_served()
    {
        _ = _folder.File("sub/pic.png", Png);
        _ = _folder.File("pic.png", Png);

        Assert.Equal(Png, ReadAll(Ask("sub/../pic.png", out _)));
        Assert.Equal(Png, ReadAll(Ask("./sub/./pic.png", out _)));

        // and the address written as segments, which a browser would have folded but a caller might not
        Assert.Equal(Png, ReadAll(_handler.Serve("mdasset://doc/" + Token + "/sub/../pic.png", out _)));
    }

    [Fact]
    public void An_image_outside_the_documents_folder_is_served_exactly_as_the_renderer_decides()
    {
        // SEC-4 decided: an image anywhere on disk may be shown (see LocalPath). The handler must agree.
        using var elsewhere = new TempFolder();
        var shared = elsewhere.File("shared.png", Png);
        _scope.Set(new AssetGrant(Token, Path.Combine(_folder.Path, "docs")));

        Assert.Equal(Png, ReadAll(Ask(shared, out _)));
        Assert.Equal(Png, ReadAll(Ask("../../" + Path.GetFileName(elsewhere.Path) + "/shared.png", out _)));
    }

    [Fact]
    public void The_handler_and_the_renderer_agree_on_every_path()
    {
        using var elsewhere = new TempFolder();
        _ = _folder.File("ok.png", Png);
        _ = _folder.File("notes.txt", "secret");
        _ = _folder.File("noext", "secret");
        var big = _folder.File("big.png", Png);
        Resize(big, LocalPath.MaxImageBytes + 1);
        string[] paths =
        [
            "ok.png", "notes.txt", "noext", "big.png", "gone.png", "//server/share/x.png", "\\\\server\\share\\x.png",
            "\\\\?\\C:\\x.png", "CON.png", "../" + Path.GetFileName(_folder.Path) + "/ok.png", elsewhere.File("e.png", Png), string.Empty,
        ];

        foreach (var path in paths)
        {
            var renderer = LocalPath.Check(path, _folder.Path, LocalPath.MaxImageBytes, LocalPath.MaxDocumentImageBytes, out _);
            using var served = Ask(path, out _);
            Assert.Equal(renderer == LocalPath.ImageOutcome.Allowed, served is not null);
        }
    }

    // ---- what is refused ------------------------------------------------------------------------------

    [Fact]
    public void A_file_that_is_not_an_image_is_refused()
    {
        _ = _folder.File("notes.txt", "secret");
        _ = _folder.File("id_rsa", "-----BEGIN OPENSSH PRIVATE KEY-----");
        _ = _folder.File("page.md", "# Page");

        foreach (var name in new[] { "notes.txt", "id_rsa", "page.md" })
        {
            Assert.Null(Ask(name, out var contentType));
            Assert.Equal("text/plain", contentType);
        }
    }

    [Fact]
    public void A_traversal_to_an_existing_file_that_is_not_an_image_is_refused()
    {
        _ = _folder.File("secret/passwords.txt", "hunter2");
        _scope.Set(new AssetGrant(Token, Path.Combine(_folder.Path, "docs")));

        Assert.Null(Ask("../secret/passwords.txt", out _));
        Assert.Null(Ask("..\\secret\\passwords.txt", out _));
        Assert.Null(Ask(Path.Combine(_folder.Path, "secret", "passwords.txt"), out _));
    }

    [Theory]
    [InlineData("//attacker.invalid/share/x.png")]
    [InlineData("\\\\attacker.invalid\\share\\x.png")]
    [InlineData("/\\attacker.invalid/share/x.png")]
    [InlineData("\\\\?\\C:\\Windows\\x.png")]
    [InlineData("\\\\.\\pipe\\x.png")]
    [InlineData("//?/C:/x.png")]
    public void A_share_or_device_path_is_refused_without_being_touched(string path) => Assert.Null(Ask(path, out _));

    [SkippableFact]
    public void A_reserved_device_name_is_refused()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        Assert.Null(Ask("CON.png", out _));
        Assert.Null(Ask("nul", out _));
    }

    [Fact]
    public void An_image_over_the_cap_is_refused_before_it_is_read()
    {
        var path = _folder.File("big.png", Png);
        Resize(path, LocalPath.MaxImageBytes + 1);

        Assert.Null(Ask("big.png", out _));

        Resize(path, LocalPath.MaxImageBytes);
        using var served = Ask("big.png", out _);
        Assert.NotNull(served); // exactly at the cap is fine (and costs nothing to open: sparse file)
    }

    [Fact]
    public void A_missing_image_is_refused_and_so_is_a_folder()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Path, "folder.png"));

        Assert.Null(Ask("gone.png", out _));
        Assert.Null(Ask("folder.png", out _));
    }

    // ---- the token ------------------------------------------------------------------------------------

    [Fact]
    public void A_token_from_a_render_that_has_been_replaced_is_refused()
    {
        _ = _folder.File("pic.png", Png);
        var stale = AssetHandler.UrlFor(Token, "pic.png");
        Assert.NotNull(_handler.Serve(stale, out _)); // served while it is current ...
        _scope.Set(new AssetGrant("fedcba9876543210fedcba9876543210", _folder.Path)); // ... the next render

        Assert.Null(_handler.Serve(stale, out var contentType));
        Assert.Equal("text/plain", contentType);
        Assert.NotNull(_handler.Serve(AssetHandler.UrlFor("fedcba9876543210fedcba9876543210", "pic.png"), out _));
    }

    [Theory]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")] // the token is case-sensitive
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("x")]
    public void A_token_nobody_issued_is_refused(string token)
    {
        _ = _folder.File("pic.png", Png);

        Assert.Null(Ask("pic.png", out _, token));
    }

    [Fact]
    public void With_no_grant_at_all_nothing_is_served()
    {
        _ = _folder.File("pic.png", Png);
        var url = AssetHandler.UrlFor(Token, "pic.png");
        _scope.Clear();

        Assert.Null(_handler.Serve(url, out _));
    }

    [Fact]
    public void Dropped_text_has_no_folder_so_only_rooted_paths_are_served()
    {
        var rooted = _folder.File("pic.png", Png);
        _scope.Set(new AssetGrant(Token, string.Empty));
        var before = Environment.CurrentDirectory;
        Environment.CurrentDirectory = _folder.Path;
        try
        {
            Assert.Null(Ask("pic.png", out _)); // the working directory is not a folder of the document's
            Assert.Equal(Png, ReadAll(Ask(rooted, out _)));
        }
        finally
        {
            Environment.CurrentDirectory = before;
        }
    }

    // ---- addresses that are not ours -----------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("mdasset://doc/")]
    [InlineData("mdasset://doc/0123456789abcdef0123456789abcdef")]
    [InlineData("mdasset://doc/0123456789abcdef0123456789abcdef/")]
    [InlineData("mdasset://doc//pic.png")]
    [InlineData("mdasset://other/0123456789abcdef0123456789abcdef/pic.png")]
    [InlineData("markdown://document/0123456789abcdef0123456789abcdef/pic.png")]
    [InlineData("https://example.com/pic.png")]
    [InlineData("file:///C:/pic.png")]
    public void An_address_that_is_not_the_apps_own_form_is_refused(string url)
    {
        _ = _folder.File("pic.png", Png);

        Assert.Null(_handler.Serve(url, out _));
    }

    [Fact]
    public void The_scheme_and_host_are_matched_without_regard_to_case()
    {
        _ = _folder.File("pic.png", Png);

        Assert.NotNull(_handler.Serve("MDASSET://DOC/" + Token + "/pic.png", out _));
    }

    // ---- the scope -----------------------------------------------------------------------------------

    [Fact]
    public void A_reader_never_sees_the_token_of_one_grant_with_the_folder_of_another()
    {
        var stop = false;
        var torn = 0;
        var writer = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                _scope.Set(new AssetGrant("a", "A"));
                _scope.Set(new AssetGrant("b", "B"));
            }
        });
        writer.Start();
        try
        {
            for (var i = 0; i < 200_000; i++)
            {
                if (_scope.TryGetBaseFolder("a", out var folder) && !string.Equals(folder, "A", StringComparison.Ordinal)) torn++;
                if (_scope.TryGetBaseFolder("b", out folder) && !string.Equals(folder, "B", StringComparison.Ordinal)) torn++;
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            writer.Join();
        }

        Assert.Equal(0, torn);
    }
}
