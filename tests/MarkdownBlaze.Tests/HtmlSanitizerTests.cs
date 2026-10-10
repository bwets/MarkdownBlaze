using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using MarkdownBlaze.Security;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>SEC-1: a document's HTML is untrusted and must be cleaned before it reaches the page.</summary>
public partial class HtmlSanitizerTests
{
    private static readonly MarkdownService Markdown = new();

    /// <summary>Nothing that can run, load a frame, or change the page may survive, whichever spelling it came in.</summary>
    private static readonly string[] Forbidden =
    [
        "onerror", "onload", "onclick", "onmouseover", "ontoggle", "onfocus", "onauxclick",
        "javascript:", "vbscript:", "<iframe", "<object", "<embed", "<applet", "<meta", "<base",
        "<form", "<script", "<svg", "<style", "<link", "<math", "srcdoc", "url(", "position:",
    ];

    public static TheoryData<string> PayloadFixtures() => TestFiles.FixtureNames("xss", "*.md");

    [GeneratedRegex(@"^\s*(javascript|vbscript|data):", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex ScriptUrl { get; }

    [Theory]
    [MemberData(nameof(PayloadFixtures))]
    public void Payloads_do_not_survive_rendering(string fixture)
    {
        var result = Markdown.RenderText(fixture, TestFiles.Fixture("xss", fixture));
        var html = result.BodyHtml;

        foreach (var needle in Forbidden)
            Assert.DoesNotContain(needle, html, StringComparison.OrdinalIgnoreCase);

        // No attribute starting with "on", whatever the event.
        foreach (var element in Parse(html).QuerySelectorAll("*"))
        {
            Assert.DoesNotContain(element.Attributes, a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase));
            Assert.False(
                element.HasAttribute("style") && !StyleIsOnlyTextAlign(element.GetAttribute("style")!),
                $"style survived on <{element.LocalName}>: {element.GetAttribute("style")}");
            if (element.HasAttribute("href"))
                Assert.DoesNotMatch(ScriptUrl, element.GetAttribute("href"));
        }
    }

    [Fact]
    public void Markdown_link_with_a_script_url_loses_its_href()
    {
        var html = Markdown.RenderText("x.md", "[click](javascript:alert(1))").BodyHtml;
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("click", html, StringComparison.Ordinal);
    }

    [Fact]
    public void What_is_new_page_is_sanitized_too()
    {
        // RenderWhatsNew goes through RenderText → RenderCore, the one place the sanitizer lives.
        var html = Markdown.RenderText("WhatsNew.md", "## 1.0\n\n<img src=x onerror=alert(1)>").BodyHtml;
        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<string> AllowedFixtures() => TestFiles.FixtureNames("allowed", "*.html");

    [Theory]
    [MemberData(nameof(AllowedFixtures))]
    public void Harmless_html_comes_out_unchanged(string fixture)
    {
        var html = TestFiles.Fixture("allowed", fixture);

        // The sanitizer re-serialises, so "unchanged" is measured against the same parse and
        // serialise without any removal.
        Assert.Equal(Normalise(html), HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Markdown_features_that_become_html_survive()
    {
        var markdown = TestFiles.Fixture("features", "markdown-features.md");

        var html = Markdown.RenderText("x.md", markdown).BodyHtml;

        Assert.Contains("<details>", html, StringComparison.Ordinal);
        Assert.Contains("<summary>", html, StringComparison.Ordinal);
        Assert.Contains("<kbd>Ctrl</kbd>", html, StringComparison.Ordinal);
        Assert.Contains("<mark>", html, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
        Assert.Contains("checked", html, StringComparison.Ordinal);
        Assert.Contains("disabled", html, StringComparison.Ordinal);
        Assert.Contains("text-align", html, StringComparison.Ordinal); // aligned table columns
        Assert.Contains("class=\"language-csharp\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"mermaid\"", html, StringComparison.Ordinal);
        Assert.Contains("<h1 id=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Checkboxes_are_always_disabled_and_other_inputs_are_removed()
    {
        var html = HtmlSanitizer.Sanitize("<input type=checkbox checked><input type=text value=x><input>");

        Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
        Assert.Contains("disabled", html, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"text\"", html, StringComparison.Ordinal);
        Assert.Equal(1, html.Split("<input").Length - 1);
    }

    [Fact]
    public void Data_urls_are_allowed_for_images_only()
    {
        var html = HtmlSanitizer.Sanitize(
            "<img src=\"data:image/png;base64,AAAA\"><img src=\"data:text/html;base64,AAAA\">"
                + "<a href=\"data:image/png;base64,AAAA\">x</a>");

        Assert.Contains("src=\"data:image/png;base64,AAAA\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("text/html", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"data:", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<img src=\"mdasset://doc/0123456789abcdef/x.png\" alt=\"a\">")]
    [InlineData("<img src=\"MDASSET://doc/0123456789abcdef/x.png\" alt=\"a\">")]
    [InlineData("<img src=\"  mdasset://doc/0123456789abcdef/x.png\" alt=\"a\">")]
    [InlineData("<img src=\"md\tasset://doc/0123456789abcdef/x.png\" alt=\"a\">")]
    [InlineData("<a href=\"mdasset://doc/0123456789abcdef/x.png\">x</a>")]
    [InlineData("<a href=\" mdasset://doc/0123456789abcdef/x.png\">x</a>")]
    public void The_asset_scheme_is_never_let_through_by_the_sanitizer(string html) =>
        // mdasset: addresses are written by the renderer after the sanitizer has run (MarkdownService.ImageRefs),
        // so one that reaches the sanitizer was written by an author, in src or in href, and goes.
        Assert.DoesNotContain("mdasset", HtmlSanitizer.Sanitize(html), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void A_forged_asset_address_in_a_document_is_removed_whatever_token_it_carries()
    {
        using var temp = new TempFolder();
        const string forged = "mdasset://doc/00000000000000000000000000000000/pic.png";
        var doc = temp.File(
            "doc.md",
            $"![mine](pic.png)\n\n![forged]({forged})\n\n<img alt=\"raw\" src=\"{forged}\">\n\n[link]({forged})");
        _ = temp.File("pic.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

        var result = Markdown.Render(doc)!;

        // the one address in the page is the one the renderer made, with this render's token
        var token = result.Assets!.Token;
        Assert.Equal(1, result.BodyHtml.Split("mdasset:", StringSplitOptions.None).Length - 1);
        Assert.Contains("src=\"mdasset://doc/" + token + "/", result.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("00000000000000000000000000000000", result.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void App_data_attributes_survive_and_other_data_attributes_do_not()
    {
        var html = HtmlSanitizer.Sanitize(
            "<a href=\"#\" data-nav=\"x.md\" data-md-blocked=\"link\" data-md-skipped=\"too-large\" data-evil=\"1\" data-processed=\"true\">x</a>");

        // data-nav is the exception to "the app's own": only the renderer may write it (SEC-7), after
        // the sanitizer has run, so one that reaches the sanitizer is an author's and goes.
        Assert.DoesNotContain("data-nav", html, StringComparison.Ordinal);
        Assert.Contains("data-md-blocked=\"link\"", html, StringComparison.Ordinal);
        Assert.Contains("data-md-skipped=\"too-large\"", html, StringComparison.Ordinal);
        Assert.Contains("data-processed=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-evil", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Style_is_reduced_to_text_align()
    {
        // (a <td> outside a table is dropped by the parser, so the cell is put in one)
        var html = HtmlSanitizer.Sanitize(
            "<table><tr><td style=\"text-align: right; background: url(https://example.com/x.png); position: fixed\">1</td></tr></table>");

        Assert.Contains("text-align", html, StringComparison.Ordinal);
        Assert.DoesNotContain("url(", html, StringComparison.Ordinal);
        Assert.DoesNotContain("position", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_list_checkboxes_cannot_be_forged_from_the_outside_to_look_interactive()
    {
        var html = HtmlSanitizer.Sanitize("<input type=\"checkbox\" onclick=\"alert(1)\">");
        Assert.DoesNotContain("onclick", html, StringComparison.Ordinal);
        Assert.Contains("disabled", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Aria_roles_survive()
    {
        // Footnote markup carries them in the Markdig versions that emit them (1.4.0 does not), and an
        // author can write them in raw HTML. A role only names what an element is; it cannot run anything.
        var html = HtmlSanitizer.Sanitize(
            "<a href=\"#fn:1\" role=\"doc-noteref\">1</a><section role=\"doc-endnotes\"><p>x</p></section>");

        Assert.Contains("role=\"doc-noteref\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"doc-endnotes\"", html, StringComparison.Ordinal);
    }

    private static AngleSharp.Dom.IElement Parse(string html)
    {
        var parser = new HtmlParser();
        var body = parser.ParseDocument("<!DOCTYPE html><html><head></head><body></body></html>").Body!;
        foreach (var node in parser.ParseFragment(html, body).ToList()) _ = body.AppendChild(node);
        return body;
    }

    private static bool StyleIsOnlyTextAlign(string style) =>
        style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(declaration => declaration.StartsWith("text-align", StringComparison.OrdinalIgnoreCase));

    private static string Normalise(string html) => Parse(html).InnerHtml;
}
