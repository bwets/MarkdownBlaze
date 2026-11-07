using AngleSharp.Html.Parser;
using MarkdownBlaze.Security;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>
/// The addresses a host supplies: the desktop's two schemes, and the single https origin of a host that
/// cannot register schemes (Android). Everything that writes, serves or refuses a picture address goes
/// through <see cref="HostAddresses"/>, so each is checked against both sets.
/// </summary>
public sealed class HostAddressesTests : IDisposable
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    /// <summary>A 1x1 PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void The_desktop_addresses_are_the_two_schemes_of_the_app()
    {
        Assert.Equal("markdown://document/", TestAddresses.Desktop.AppBase.AbsoluteUri);
        Assert.Equal("mdasset://doc/", TestAddresses.Desktop.AssetPrefix);
        Assert.Equal("mdasset://doc/" + Token + "/img%2Fa.png", TestAddresses.Desktop.AssetUrlFor(Token, "img/a.png"));
    }

    [Fact]
    public void A_host_that_cannot_register_a_scheme_serves_the_pictures_from_a_path_of_its_origin() =>
        Assert.Equal("https://0.0.0.1/_mdasset/" + Token + "/img%2Fa.png", TestAddresses.Android.AssetUrlFor(Token, "img/a.png"));

    [Theory]
    [InlineData("not a uri", "https://0.0.0.1/_mdasset/")]
    [InlineData("https://0.0.0.1/", "https://0.0.0.1/_mdasset")]
    [InlineData("https://0.0.0.1/", "/_mdasset/")]
    public void Addresses_that_are_not_absolute_or_do_not_end_in_a_slash_are_refused(string appBase, string prefix) =>
        _ = Assert.ThrowsAny<ArgumentException>(() => new HostAddresses(new Uri(appBase, UriKind.RelativeOrAbsolute), prefix));

    [Theory]
    [InlineData("https://0.0.0.1/_mdasset/abc/x.png")]
    [InlineData("HTTPS://0.0.0.1/_MDASSET/abc/x.png")]
    [InlineData("/_mdasset/abc/x.png")]
    [InlineData("//0.0.0.1/_mdasset/abc/x.png")]
    [InlineData("/a/../_mdasset/abc/x.png")]
    [InlineData("_mdasset/abc/x.png")]
    [InlineData("  \thttps://0.0.0.1/_md\nasset/abc/x.png")]
    public void Every_way_of_writing_an_android_picture_address_is_recognised(string address) =>
        Assert.True(TestAddresses.Android.IsAssetAddress(address));

    [Theory]
    [InlineData("https://0.0.0.1/index.html")]
    [InlineData("https://example.com/_mdasset/abc/x.png")]
    [InlineData("pic.png")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_addresses_are_not_picture_addresses(string? address) =>
        Assert.False(TestAddresses.Android.IsAssetAddress(address));

    [Theory]
    [InlineData("mdasset://doc/abc/x.png")]
    [InlineData("MDASSET://doc/abc/x.png")]
    public void Every_way_of_writing_a_desktop_picture_address_is_recognised(string address)
    {
        Assert.True(TestAddresses.Desktop.IsAssetAddress(address));
        Assert.False(TestAddresses.Desktop.IsAssetAddress("https://0.0.0.1/_mdasset/abc/x.png"));
    }

    [Theory]
    [InlineData("<img src=\"https://0.0.0.1/_mdasset/abc/x.png\" alt=\"a\">", "src")]
    [InlineData("<img src=\"/_mdasset/abc/x.png\" alt=\"a\">", "src")]
    [InlineData("<img src=\"HTTPS://0.0.0.1/_Mdasset/abc/x.png\" alt=\"a\">", "src")]
    [InlineData("<a href=\"//0.0.0.1/_mdasset/abc/x.png\">a</a>", "href")]
    [InlineData("<a href=\"https://0.0.0.1/q/../_mdasset/abc/x.png\">a</a>", "href")]
    public void The_sanitizer_removes_an_author_written_android_picture_address(string html, string attribute)
    {
        var cleaned = HtmlSanitizer.Sanitize(html, TestAddresses.Android);

        var element = new HtmlParser().ParseDocument(cleaned).Body!.FirstElementChild!;
        Assert.Null(element.GetAttribute(attribute));
    }

    [Fact]
    public void The_sanitizer_keeps_a_picture_from_the_web_and_still_removes_the_literal_scheme()
    {
        var kept = HtmlSanitizer.Sanitize("<img src=\"https://example.com/p.png\" alt=\"a\">", TestAddresses.Android);
        var removed = HtmlSanitizer.Sanitize("<img src=\"mdasset://doc/abc/x.png\" alt=\"a\">", TestAddresses.Android);

        Assert.Contains("https://example.com/p.png", kept, StringComparison.Ordinal);
        Assert.DoesNotContain("mdasset", removed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_rendered_document_points_its_pictures_at_the_hosts_prefix_and_the_handler_serves_them()
    {
        _ = _folder.File("pic.png", Png);
        var service = new MarkdownService(TestAddresses.Android);
        var markdown = _folder.File("page.md", "![a](pic.png)\n\n<img src=\"/_mdasset/forged/pic.png\" alt=\"forged\">");

        var result = service.Render(markdown)!;

        var token = result.Assets!.Token;
        Assert.Contains("https://0.0.0.1/_mdasset/" + token + "/pic.png", result.BodyHtml, StringComparison.Ordinal);
        var scope = new AssetScope();
        scope.Set(result.Assets);
        var handler = new AssetHandler(scope, TestAddresses.Android);
        using var served = handler.Serve(TestAddresses.Android.AssetUrlFor(token, "pic.png"), out var contentType);
        Assert.NotNull(served);
        Assert.Equal("image/png", contentType);
        Assert.Null(handler.Serve(TestAddresses.Desktop.AssetUrlFor(token, "pic.png"), out _));
    }

    [Fact]
    public void The_data_folder_can_be_named_by_the_host_but_only_with_an_absolute_path()
    {
        _ = Assert.Throws<ArgumentException>(() => AppPaths.Configure("relative/folder"));
        _ = Assert.Throws<ArgumentException>(() => AppPaths.Configure(" "));
    }
}
