using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>SEC-2 step 3 and SEC-7: what a link becomes, and which targets the app will follow.</summary>
public class LinkRewriteTests
{
    private static readonly MarkdownService Markdown = new();

    private static string Render(TempFolder temp, string markdown) =>
        Markdown.Render(temp.File("doc.md", markdown))!.BodyHtml;

    /// <summary>The opening tag of the anchor whose text is <paramref name="text"/>.</summary>
    private static string AnchorOf(string html, string text)
    {
        var end = html.IndexOf(">" + text + "</a>", StringComparison.Ordinal);
        Assert.True(end > 0, $"no anchor with the text '{text}' in: {html}");
        return html[html.LastIndexOf("<a ", end, StringComparison.Ordinal)..(end + 1)];
    }

    [Fact]
    public void A_relative_markdown_link_navigates_in_app()
    {
        using var temp = new TempFolder();
        _ = temp.File("other.md", "# Other");

        var html = Render(temp, "[other](other.md) [bare](other) [folder](sub/)");

        Assert.Contains($"data-nav=\"{Path.Combine(temp.Path, "other.md")}\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-nav=\"{Path.Combine(temp.Path, "sub", "index.md")}\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// SEC-7: <c>data-nav</c> is what the page follows on a click, so it must be something only the
    /// renderer can produce. A hand-written one, pointing at a Markdown file that exists, would otherwise
    /// survive the sanitizer and be followed.
    /// </summary>
    [Fact]
    public void A_data_nav_written_by_the_author_is_removed_and_the_renderers_own_is_not()
    {
        using var temp = new TempFolder();
        var existing = temp.File("existing.md", "# Existing");
        _ = temp.File("other.md", "# Other");

        var html = Render(
            temp,
            $"<a data-nav=\"{existing}\" href=\"#\">forged</a>\n\n[x](other.md)");

        Assert.DoesNotContain(existing, html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-nav", AnchorOf(html, "forged"), StringComparison.Ordinal);
        Assert.Contains($"data-nav=\"{Path.Combine(temp.Path, "other.md")}\"", AnchorOf(html, "x"), StringComparison.Ordinal);
        Assert.DoesNotContain("mdnav-", html, StringComparison.Ordinal); // every token was swapped back
    }

    /// <summary>
    /// SEC-14: the renderer's own stage runs after the sanitizer and nothing runs between them, so what an
    /// author wrote by hand of what only the renderer may write is gone before the renderer starts. The
    /// two things are <c>data-nav</c> and <c>mdasset://</c>; the renderer's own, in the same document, survive.
    /// </summary>
    [Fact]
    public void What_only_the_renderer_may_write_is_removed_from_the_authors_html_and_the_renderers_own_is_not()
    {
        using var temp = new TempFolder();
        var existing = temp.File("existing.md", "# Existing");
        _ = temp.File("pic.png", Png);
        var doc = temp.File(
            "doc.md",
            $"<a data-nav=\"{existing}\" href=\"#\">forged link</a>\n\n"
                + "<img src=\"mdasset://doc/00000000000000000000000000000000/x.png\" alt=\"forged picture\">\n\n"
                + "<img src=\"pic.png\" alt=\"mine\">\n\n"
                + "<a href=\"existing.md\">mine too</a>");

        var result = Markdown.Render(doc)!;
        var html = result.BodyHtml;

        Assert.DoesNotContain("data-nav", AnchorOf(html, "forged link"), StringComparison.Ordinal);
        Assert.Contains($"data-nav=\"{existing}\"", AnchorOf(html, "mine too"), StringComparison.Ordinal);
        var forged = ImageOf(html, "forged picture");
        Assert.DoesNotContain("src=", forged, StringComparison.Ordinal);
        Assert.DoesNotContain("00000000000000000000000000000000", html, StringComparison.Ordinal);
        Assert.Contains("src=\"mdasset://doc/" + result.Assets!.Token + "/pic.png\"", ImageOf(html, "mine"), StringComparison.Ordinal);
    }

    private static string ImageOf(string html, string alt)
    {
        var at = html.IndexOf("alt=\"" + alt + "\"", StringComparison.Ordinal);
        Assert.True(at > 0, $"no image with the alt text '{alt}' in: {html}");
        return html[html.LastIndexOf("<img", at, StringComparison.Ordinal)..(html.IndexOf('>', at) + 1)];
    }

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void Web_and_mail_links_are_left_alone()
    {
        using var temp = new TempFolder();

        var html = Render(
            temp,
            "[a](https://example.com/x?y=1#z) [b](mailto:a@example.com) [c](tel:+3212345678) [d](ftp://example.com/) [e](#top)");

        Assert.Contains("href=\"https://example.com/x?y=1#z\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:a@example.com\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"tel:+3212345678\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"ftp://example.com/\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#top\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-nav", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("vbscript:x")]
    [InlineData("ms-msdt:/id")]
    [InlineData("search-ms:query=x")]
    public void Script_and_handler_urls_do_not_reach_the_page(string url)
    {
        using var temp = new TempFolder();

        var html = Render(temp, $"[x](<{url}>)");

        // The anchor may survive; the address must not.
        Assert.DoesNotContain(url.Split(':')[0] + ":", html, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableTheory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("C:/Windows/System32/calc.exe")]
    [InlineData("file://attacker.invalid/share/x.exe")]
    public void An_absolute_local_link_that_is_not_a_markdown_file_is_inert(string url)
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        using var temp = new TempFolder();

        var html = Render(temp, $"[x](<{url}>)");

        Assert.DoesNotContain("calc.exe", html, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker.invalid", html, StringComparison.Ordinal);
        Assert.DoesNotContain("file:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-nav", html, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void An_absolute_link_to_an_existing_markdown_file_navigates_in_app()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        using var temp = new TempFolder();
        var target = temp.File("target.md", "# Target");

        var viaDrive = Render(temp, $"[x]({target.Replace('\\', '/')})");
        var viaFileUri = Render(temp, $"[x]({new Uri(target).AbsoluteUri})");

        Assert.Contains($"data-nav=\"{target}\"", viaDrive, StringComparison.Ordinal);
        Assert.Contains($"data-nav=\"{target}\"", viaFileUri, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void An_absolute_link_to_a_missing_markdown_file_is_inert()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        using var temp = new TempFolder();

        var html = Render(temp, "[x](C:/does/not/exist.md)");

        Assert.DoesNotContain("data-nav", html, StringComparison.Ordinal);
        Assert.Contains("data-md-blocked=\"link\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#\"", html, StringComparison.Ordinal);
    }

    // ---- SEC-7: what the page may ask the app to open ----------------------------------------------

    [Fact]
    public void Only_existing_documents_of_a_supported_type_are_followed()
    {
        using var temp = new TempFolder();
        var md = temp.File("a.md", "# A");
        var txt = temp.File("a.txt", "A");
        var exe = temp.File("a.exe", "MZ");
        var png = temp.File("a.png", "x");

        foreach (var fromLink in new[] { true, false })
        {
            Assert.True(NavigationService.IsFollowable(md, currentDocument: null, fromLink));
            Assert.True(NavigationService.IsFollowable(txt, currentDocument: null, fromLink));
            Assert.False(NavigationService.IsFollowable(exe, currentDocument: null, fromLink));
            Assert.False(NavigationService.IsFollowable(png, currentDocument: null, fromLink));
            Assert.False(NavigationService.IsFollowable(Path.Combine(temp.Path, "missing.md"), currentDocument: null, fromLink));
            Assert.False(NavigationService.IsFollowable("", currentDocument: null, fromLink));
            Assert.False(NavigationService.IsFollowable(path: null, currentDocument: null, fromLink));
        }
    }

    [Theory]
    [InlineData("\\\\attacker.invalid\\share\\a.md")]
    [InlineData("//attacker.invalid/share/a.md")]
    [InlineData("\\\\?\\C:\\a.md")]
    public void A_link_cannot_point_at_another_servers_share(string target)
    {
        Assert.False(NavigationService.IsFollowable(target, currentDocument: null, fromLink: true));
        Assert.False(NavigationService.IsFollowable(target, "C:\\notes\\a.md", fromLink: true));
        Assert.False(NavigationService.IsFollowable(target, "\\\\other\\share\\a.md", fromLink: true));
    }
}
