using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MarkdownBlaze.Models;
using MarkdownBlaze.Security;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>
/// SEC-14 and SEC-12: links and pictures are resolved on the sanitized tree, by one piece of code, whether
/// Markdown or an author's own HTML wrote them — and there is a limit to how many are looked at on disk.
/// </summary>
public sealed class ReferenceResolverTests : IDisposable
{
    private static readonly MarkdownService Markdown = new();

    /// <summary>A 1x1 PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly TempFolder _wiki = CreateWiki();

    public void Dispose() => _wiki.Dispose();
    private RenderResult Render(string markdown) => Markdown.Render(_wiki.File("page.md", markdown))!;
    private string Html(string markdown) => Render(markdown).BodyHtml;

    private static IReadOnlyList<IElement> Elements(string html, string selector)
    {
        var body = new HtmlParser().ParseDocument("<!doctype html><html><body>" + html).Body!;
        return [.. body.QuerySelectorAll(selector)];
    }

    // ---- raw HTML pictures ------------------------------------------------------------------------------

    [Fact]
    public void A_picture_written_as_html_is_shown_like_one_written_as_markdown()
    {
        _ = _wiki.File("pic.png", Png);
        _ = _wiki.File("img/pic.png", Png);

        var result = Render("![md](pic.png)\n\n<img src=\"pic.png\" width=\"40\" alt=\"html\">\n\n<img src=\"img/pic.png\" alt=\"html in a subfolder\">");

        var token = result.Assets!.Token;
        var images = Elements(result.BodyHtml, "img");
        Assert.Equal(3, images.Count);
        Assert.Equal("mdasset://doc/" + token + "/pic.png", images[0].GetAttribute("src"));
        Assert.Equal("mdasset://doc/" + token + "/pic.png", images[1].GetAttribute("src"));
        Assert.Equal("mdasset://doc/" + token + "/img%2Fpic.png", images[2].GetAttribute("src"));
        Assert.Equal("40", images[1].GetAttribute("width")); // what the author wrote beside it is kept
        Assert.Equal("html", images[1].GetAttribute("alt"));
    }

    [Fact]
    public void A_percent_encoded_address_is_decoded_before_the_file_is_looked_for_whoever_wrote_it()
    {
        _ = _wiki.File("my pic.png", Png);

        var result = Render(
            "![md](<my pic.png>)\n\n![encoded](my%20pic.png)\n\n<img src=\"my pic.png\" alt=\"html\">\n\n<img src=\"my%20pic.png\" alt=\"html encoded\">");

        var images = Elements(result.BodyHtml, "img");
        Assert.All(images, image => Assert.Equal("mdasset://doc/" + result.Assets!.Token + "/my%20pic.png", image.GetAttribute("src")));
        Assert.Equal(4, images.Count);
    }

    [Fact]
    public void A_raw_picture_is_held_to_the_same_rules_as_a_markdown_one()
    {
        _ = _wiki.File("notes.txt", "secret");
        _ = _wiki.File("big.png", Png);
        using (var stream = new FileStream(Path.Combine(_wiki.Path, "big.png"), FileMode.Open, FileAccess.Write))
            stream.SetLength(LocalPath.MaxImageBytes + 1);

        var result = Render(
            "<img src=\"notes.txt\" alt=\"a\">\n\n<img src=\"big.png\" alt=\"b\">\n\n<img src=\"//attacker.invalid/s/x.png\" alt=\"c\">\n\n"
                + "<img src=\"gone.png\" alt=\"d\">\n\n<img src=\"https://example.com/p.png\" alt=\"e\">");

        var images = Elements(result.BodyHtml, "img");
        Assert.Equal("blocked", images[0].GetAttribute("data-md-skipped"));
        Assert.Equal("too-large", images[1].GetAttribute("data-md-skipped"));
        Assert.Equal("blocked", images[2].GetAttribute("data-md-skipped"));
        Assert.Equal("gone.png", images[3].GetAttribute("src")); // missing: left to show its alt text
        Assert.Equal("https://example.com/p.png", images[4].GetAttribute("src")); // remote: left alone
        Assert.Null(result.Assets);
        Assert.DoesNotContain("attacker.invalid", result.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void An_address_with_a_scheme_that_is_not_a_web_one_loses_its_source()
    {
        var html = Html("<img src=\"foo:bar.png\" alt=\"a\">\n\n<img src=\"ftp://example.com/p.png\" alt=\"b\">\n\n<img src=\"javascript:alert(1)\" alt=\"c\">");

        Assert.All(Elements(html, "img"), image => Assert.Null(image.GetAttribute("src")));
    }

    // ---- raw HTML links ---------------------------------------------------------------------------------

    [Fact]
    public void A_link_written_as_html_navigates_in_app_like_one_written_as_markdown()
    {
        var html = Html(
            "[md](other.md)\n\n<a href=\"other.md\">html</a> <a href=\"other\">bare</a> <a href=\"dune/\">folder</a> "
                + "<a href=\"dune/houses.md#atreides\">anchor</a>");

        var anchors = Elements(html, "a");
        Assert.Equal(5, anchors.Count);
        Assert.Equal(Path.Combine(_wiki.Path, "other.md"), anchors[0].GetAttribute("data-nav"));
        Assert.Equal(Path.Combine(_wiki.Path, "other.md"), anchors[1].GetAttribute("data-nav"));
        Assert.Equal(Path.Combine(_wiki.Path, "other.md"), anchors[2].GetAttribute("data-nav"));
        Assert.Equal(Path.Combine(_wiki.Path, "dune", "index.md"), anchors[3].GetAttribute("data-nav"));
        Assert.Equal(Path.Combine(_wiki.Path, "dune", "houses.md"), anchors[4].GetAttribute("data-nav"));
        Assert.All(anchors, anchor => Assert.Equal("#", anchor.GetAttribute("href")));
    }

    public static TheoryData<string> Addresses() =>
    [
        "other.md", "other", "dune/", "dune/houses.md#atreides", "dune/houses.md?x=1", "my%20notes.md", "my notes.md",
        "#top", "https://example.com/a?b=1#c", "mailto:a@example.com", "tel:+3212345678", "ftp://example.com/",
        "//example.com", "/etc/hosts", "./diagram.png", "./page.html", "javascript:alert(1)", "ms-msdt:/id", "data:text/html,x",
        "foo:bar", "notes.txt", "", "..", "../outside.md",
    ];

    [Theory]
    [MemberData(nameof(Addresses))]
    public void The_same_address_comes_out_the_same_in_markdown_and_in_html(string address)
    {
        var fromMarkdown = Elements(Html($"[x](<{address}>)"), "a").SingleOrDefault();
        var fromHtml = Elements(Html($"<a href=\"{address}\">x</a>"), "a").SingleOrDefault();

        Assert.NotNull(fromMarkdown);
        Assert.NotNull(fromHtml);
        Assert.Equal(fromMarkdown.GetAttribute("href"), fromHtml.GetAttribute("href"));
        Assert.Equal(fromMarkdown.GetAttribute("data-nav"), fromHtml.GetAttribute("data-nav"));
        Assert.Equal(fromMarkdown.GetAttribute("data-md-blocked"), fromHtml.GetAttribute("data-md-blocked"));
    }

    [SkippableTheory]
    [InlineData("C:/Windows/System32/calc.exe")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("file://attacker.invalid/share/x.exe")]
    [InlineData("\\\\attacker.invalid\\share\\x.lnk")]
    [InlineData("\\\\?\\C:\\x.md")]
    public void An_absolute_local_address_in_html_is_inert_unless_it_is_a_markdown_document_that_exists(string address)
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        var html = Html($"<a href=\"{address}\">x</a>");

        var anchor = Assert.Single(Elements(html, "a"));
        Assert.Equal("#", anchor.GetAttribute("href"));
        Assert.Equal("link", anchor.GetAttribute("data-md-blocked"));
        Assert.Null(anchor.GetAttribute("data-nav"));
        Assert.DoesNotContain("calc.exe", html, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker", html, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void An_absolute_local_address_in_html_to_an_existing_markdown_document_navigates_in_app()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var target = Path.Combine(_wiki.Path, "other.md");

        var html = Html($"<a href=\"{target.Replace('\\', '/')}\">drive</a> <a href=\"{new Uri(target).AbsoluteUri}\">file</a>");

        Assert.All(Elements(html, "a"), anchor => Assert.Equal(target, anchor.GetAttribute("data-nav")));
    }

    [Fact]
    public void A_raw_link_the_sanitizer_leaves_alone_is_left_alone_here_too()
    {
        var html = Html("<a href=\"https://example.com\" title=\"t\">web</a> <a href=\"#a\">anchor</a> <a>nothing</a>");

        var anchors = Elements(html, "a");
        Assert.Equal("https://example.com", anchors[0].GetAttribute("href"));
        Assert.Equal("t", anchors[0].GetAttribute("title"));
        Assert.Equal("#a", anchors[1].GetAttribute("href"));
        Assert.All(anchors, anchor => Assert.Null(anchor.GetAttribute("data-nav")));
    }

    // ---- ordering: what only the renderer may write ------------------------------------------------------

    [Fact]
    public void A_link_to_a_markdown_document_through_an_image_is_resolved_on_both_the_link_and_the_picture()
    {
        _ = _wiki.File("pic.png", Png);

        var result = Render("[![alt](pic.png)](other.md)");

        var anchor = Assert.Single(Elements(result.BodyHtml, "a"));
        var image = Assert.Single(Elements(result.BodyHtml, "img"));
        Assert.Equal(Path.Combine(_wiki.Path, "other.md"), anchor.GetAttribute("data-nav"));
        Assert.StartsWith("mdasset://doc/" + result.Assets!.Token + "/", image.GetAttribute("src"), StringComparison.Ordinal);
    }

    // ---- limits (SEC-12) --------------------------------------------------------------------------------

    [Fact]
    public void Links_past_the_limit_are_inert_and_marked()
    {
        var markdown = new StringBuilder();
        for (var i = 0; i < MarkdownService.MaxResolvedLinks + 10; i++)
            _ = markdown.Append("[l](").Append(i.ToString(CultureInfo.InvariantCulture)).Append(".md) ");

        var html = Html(markdown.ToString());

        var anchors = Elements(html, "a");
        Assert.Equal(MarkdownService.MaxResolvedLinks + 10, anchors.Count);
        Assert.Equal(MarkdownService.MaxResolvedLinks, anchors.Count(anchor => anchor.GetAttribute("data-nav") is not null));
        var inert = anchors.Where(anchor => string.Equals(anchor.GetAttribute("data-md-blocked"), "limit", StringComparison.Ordinal)).ToList();
        Assert.Equal(10, inert.Count);
        Assert.All(inert, anchor => Assert.Equal("#", anchor.GetAttribute("href")));
        Assert.All(inert, anchor => Assert.Null(anchor.GetAttribute("data-nav")));
        Assert.Equal("limit", anchors[MarkdownService.MaxResolvedLinks].GetAttribute("data-md-blocked")); // the first past it
    }

    [Fact]
    public void Web_links_do_not_use_up_the_limit_because_nothing_on_disk_is_looked_at_for_them()
    {
        var markdown = new StringBuilder();
        for (var i = 0; i < MarkdownService.MaxResolvedLinks + 50; i++)
        {
            _ = markdown.Append("[w](https://example.com/").Append(i.ToString(CultureInfo.InvariantCulture)).Append(") ");
        }

        _ = markdown.Append("[last](other.md)");

        var anchors = Elements(Html(markdown.ToString()), "a");

        Assert.All(anchors.Take(MarkdownService.MaxResolvedLinks + 50), anchor => Assert.Null(anchor.GetAttribute("data-md-blocked")));
        Assert.NotNull(anchors[^1].GetAttribute("data-nav"));
    }

    [Fact]
    public void Pictures_past_the_limit_say_there_are_too_many_in_place_of_the_picture()
    {
        _ = _wiki.File("pic.png", Png);
        var markdown = new StringBuilder();
        for (var i = 0; i < MarkdownService.MaxImages + 5; i++)
            _ = markdown.Append(i % 2 == 0 ? "![a](pic.png) " : "<img src=\"pic.png\" alt=\"a\"> ");

        var result = Render(markdown.ToString());

        var images = Elements(result.BodyHtml, "img");
        Assert.Equal(MarkdownService.MaxImages + 5, images.Count);
        Assert.Equal(MarkdownService.MaxImages, images.Count(image => image.GetAttribute("src")?.StartsWith("mdasset://", StringComparison.Ordinal) == true));
        var skipped = images.Where(image => string.Equals(image.GetAttribute("data-md-skipped"), "limit", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, skipped.Count);
        Assert.All(skipped, image => Assert.Null(image.GetAttribute("src"))); // no picture, and no data: address either (see the next test)
        Assert.All(skipped, image => Assert.Equal("Too many images", image.GetAttribute("alt")));
        Assert.NotNull(result.Assets);
    }

    [Fact]
    public void The_limit_keeps_a_page_of_tens_of_thousands_of_pictures_small()
    {
        // 20,000 pictures that are not there: past the limit each is a few dozen characters, not a drawing of its own.
        var html = Html(string.Concat(Enumerable.Repeat("![a](missing.png) ", 20_000)));

        Assert.True(html.Length < 1_500_000, "the page is " + html.Length.ToString(CultureInfo.InvariantCulture) + " characters");
        Assert.Equal(20_000 - MarkdownService.MaxImages, html.Split("data-md-skipped=\"limit\"").Length - 1);
    }

    [Fact]
    public void Remote_pictures_do_not_use_up_the_picture_limit()
    {
        var markdown = new StringBuilder();
        for (var i = 0; i < MarkdownService.MaxImages + 20; i++)
            _ = markdown.Append("![r](https://example.com/p").Append(i.ToString(CultureInfo.InvariantCulture)).Append(".png) ");

        var images = Elements(Html(markdown.ToString()), "img");

        Assert.All(images, image => Assert.Null(image.GetAttribute("data-md-skipped")));
    }

    [Fact]
    public void The_limits_are_the_ones_the_review_asked_for()
    {
        Assert.Equal(5000, MarkdownService.MaxResolvedLinks);
        Assert.Equal(2000, MarkdownService.MaxImages);
    }

    [Fact]
    public void A_render_that_is_cancelled_while_it_resolves_stops()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var resolver = new ReferenceResolver(_wiki.Path, cancelled.Token);
        using var document = HtmlSanitizer.Clean("<a href=\"other.md\">x</a>");

        _ = Assert.Throws<OperationCanceledException>(() => resolver.Resolve(document.Body!));
    }

    private static TempFolder CreateWiki()
    {
        var wiki = new TempFolder();
        _ = wiki.File("index.md", "# Home");
        _ = wiki.File("other.md", "# Other");
        _ = wiki.File("my notes.md", "# My notes");
        _ = wiki.File("dune/index.md", "# Dune");
        _ = wiki.File("dune/houses.md", "# Houses");
        return wiki;
    }
}
