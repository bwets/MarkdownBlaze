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

    // ---- SEC-13: the page's own names are not the document's --------------------------------------------

    [Theory]
    [InlineData("menu-backdrop")]
    [InlineData("rate-bar")]
    [InlineData("splash")]
    [InlineData("ctx")]
    [InlineData("settings-overlay")]
    [InlineData("toast")]
    [InlineData("app-header")]
    [InlineData("Admonition")] // classes are case-sensitive in a page in standards mode, and so is this list
    public void A_class_the_app_gives_a_meaning_to_is_removed_and_the_element_stays(string name)
    {
        var html = HtmlSanitizer.Sanitize($"<div class=\"{name}\">text</div>");

        Assert.Equal("<div>text</div>", html);
    }

    [Fact]
    public void Of_several_classes_only_the_allowed_ones_are_kept()
    {
        var html = HtmlSanitizer.Sanitize("<div class=\"menu-backdrop  admonition\tadmonition-note rate-bar language-x\">x</div>");

        Assert.Equal("<div class=\"admonition admonition-note language-x\">x</div>", html);
    }

    [Theory]
    [InlineData("language-csharp")]
    [InlineData("language-")]
    [InlineData("admonition-title")]
    [InlineData("admonition-question")]
    [InlineData("admonition-anything")]
    public void The_two_prefixes_are_allowed(string name) =>
        Assert.Equal($"<div class=\"{name}\">x</div>", HtmlSanitizer.Sanitize($"<div class=\"{name}\">x</div>"));

    [Theory]
    [InlineData("language")] // not the prefix: no hyphen
    [InlineData("xlanguage-csharp")]
    [InlineData("my-admonition-note")]
    public void Names_that_only_contain_a_prefix_are_not_allowed(string name) =>
        Assert.DoesNotContain("class=", HtmlSanitizer.Sanitize($"<div class=\"{name}\">x</div>"), StringComparison.Ordinal);

    [Fact]
    public void The_overlay_class_cannot_come_in_through_a_generic_attribute_or_a_custom_container()
    {
        const string markdown = "[a](https://example.com){.menu-backdrop}\n\n::: menu-backdrop\ninside the container\n:::\n\n"
            + "## Title {.rate-bar #splash-id}\n\n![](x.png){.splash}\n";

        var html = Markdown.RenderText("x.md", markdown).BodyHtml;

        foreach (var name in new[] { "menu-backdrop", "rate-bar", "splash\"" })
            Assert.DoesNotContain("class=\"" + name.TrimEnd('"'), html, StringComparison.Ordinal);

        Assert.Contains("inside the container", html, StringComparison.Ordinal); // the container's content is still there
        Assert.Contains("href=\"https://example.com\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"splash-id\"", html, StringComparison.Ordinal); // an id that is not the app's is the document's
    }

    [Fact]
    public void Every_class_the_pipeline_emits_for_the_documents_of_the_repository_survives_unchanged()
    {
        // The documents a reader of this app is meant to see: the sample wiki (but for the hostile pages), the
        // fixtures that exercise the features, the README and the bundled manual and change log. What Markdig writes
        // before the sanitizer is compared with what is left after it.
        var root = TestFiles.RepoRoot;
        var documents = Directory.EnumerateFiles(Path.Combine(root, "samples", "wiki"), "*.md", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.Combine("wiki", "security") + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests", "fixtures", "features"), "*.md"))
            .Append(Path.Combine(root, "README.md"))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src", "MarkdownBlaze", "Content"), "*.md"))
            .ToList();
        Assert.True(documents.Count >= 8, "found only " + documents.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " documents");

        var seen = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            var markdown = File.ReadAllText(document);
            var before = ClassesOf(Markdig.Markdown.ToHtml(markdown, MarkdownService.Pipeline));
            var after = ClassesOf(Markdown.RenderText(Path.GetFileName(document), markdown).BodyHtml);

            var removed = string.Join(", ", before.Except(after, StringComparer.Ordinal));
            Assert.True(before.SetEquals(after), Path.GetFileName(document) + ": the sanitizer removed " + removed);
            seen.UnionWith(after);
        }

        // What is emitted is exactly what the allow-list names (language-* standing for the fences' languages).
        foreach (var name in seen)
        {
            Assert.True(
                ExpectedClasses.Contains(name, StringComparer.Ordinal) || name.StartsWith("language-", StringComparison.Ordinal),
                name + " is emitted and not on the list the documentation gives");
        }
    }

    private static readonly string[] ExpectedClasses =
    [
        "admonition", "admonition-body", "admonition-danger", "admonition-icon", "admonition-info", "admonition-note",
        "admonition-success", "admonition-tip", "admonition-warning", "admonition-title", "admonition-abstract", "admonition-bug",
        "admonition-example", "admonition-question", "admonition-quote", "contains-task-list", "footnote-back-ref",
        "footnote-ref", "footnotes", "math", "mermaid", "tag", "task-list-item", "wikilink",
    ];

    private static HashSet<string> ClassesOf(string html)
    {
        var classes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in Parse(html).QuerySelectorAll("[class]"))
            classes.UnionWith(element.GetAttribute("class")!.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return classes;
    }

    [Theory]
    [InlineData("md-tree")]
    [InlineData("md-splitter")]
    [InlineData("md-current-doc")]
    [InlineData("md-anything")]
    [InlineData("MD-Tree")]
    [InlineData("tab-contents")]
    [InlineData("tab-history")]
    [InlineData("app")]
    [InlineData("App")]
    [InlineData("shortcuts-title")]
    [InlineData("whats-new-title")]
    [InlineData("global-overlay")]
    public void An_id_the_app_looks_elements_up_by_is_removed_and_the_element_stays(string id) =>
        Assert.Equal("<div>x</div>", HtmlSanitizer.Sanitize($"<div id=\"{id}\">x</div>"));

    [Theory]
    [InlineData("section")]
    [InlineData("fn1")]
    [InlineData("tabs")] // "tab-" needs its hyphen
    [InlineData("md")]
    [InlineData("application")]
    [InlineData("my-md-tree")]
    public void Other_ids_are_the_documents_own(string id) =>
        Assert.Equal($"<div id=\"{id}\">x</div>", HtmlSanitizer.Sanitize($"<div id=\"{id}\">x</div>"));

    [Fact]
    public void A_heading_whose_generated_id_is_one_of_the_apps_loses_it_which_is_the_accepted_price()
    {
        // "MD notes" becomes the id md-notes, and "Tab stops" tab-stops: both are the app's prefixes. The anchor
        // link to such a heading and its place in the outline no longer scroll to it (documented in the sanitizer).
        var result = Markdown.RenderText("x.md", "# MD notes\n\n## Tab stops\n\n## Notes");

        Assert.DoesNotContain("id=\"md-notes\"", result.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"tab-stops\"", result.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"notes\"", result.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("MD notes", result.BodyHtml, StringComparison.Ordinal); // the heading itself is there
    }

    [Theory]
    [InlineData("dialog")]
    [InlineData("alertdialog")]
    [InlineData("alert")]
    [InlineData("button")]
    [InlineData("menu")]
    [InlineData("menuitem")]
    [InlineData("tab")]
    [InlineData("tablist")]
    [InlineData("status")]
    [InlineData("textbox")]
    [InlineData("tooltip")]
    [InlineData("DIALOG")]
    public void A_role_that_makes_a_screen_reader_announce_a_widget_is_removed(string role) =>
        Assert.Equal("<div>x</div>", HtmlSanitizer.Sanitize($"<div role=\"{role}\">x</div>"));

    [Theory]
    [InlineData("doc-noteref")]
    [InlineData("doc-endnotes")]
    [InlineData("doc-backlink")]
    [InlineData("note")]
    [InlineData("img")]
    [InlineData("figure")]
    [InlineData("presentation")]
    [InlineData("none")]
    [InlineData("list")]
    [InlineData("listitem")]
    [InlineData("table")]
    [InlineData("row")]
    [InlineData("cell")]
    [InlineData("columnheader")]
    [InlineData("rowheader")]
    [InlineData("group")]
    [InlineData("region")]
    [InlineData("heading")]
    [InlineData("link")]
    [InlineData("separator")]
    public void A_role_that_describes_content_is_kept(string role) =>
        Assert.Equal($"<div role=\"{role}\">x</div>", HtmlSanitizer.Sanitize($"<div role=\"{role}\">x</div>"));

    [Fact]
    public void Renders_on_several_threads_at_once_give_what_they_give_one_at_a_time()
    {
        // A render that was given up on goes on in the background while the next one starts, so the sanitizer
        // is used from several threads (there is no lock around it any more, which a stuck render would hold).
        string[] inputs =
        [
            "<div class=\"menu-backdrop admonition\" id=\"md-tree\" role=\"dialog\"><a href=\"javascript:alert(1)\" onclick=\"x()\">a</a><img src=\"C:/x.png\"></div>",
            "<table><tr><td style=\"text-align:right; position:fixed\">1</td></tr></table><script>alert(1)</script><p class=\"tag\">#t</p>",
            "<ul class=\"contains-task-list\"><li class=\"task-list-item\"><input type=\"checkbox\" checked> x</li></ul><details open><summary>s</summary>b</details>",
            string.Concat(Enumerable.Repeat("<div>", 700)) + "deep" + string.Concat(Enumerable.Repeat("</div>", 700)),
        ];
        var expected = inputs.Select(HtmlSanitizer.Sanitize).ToArray();

        var results = new string[8 * 25];
        _ = Parallel.For(
            0,
            results.Length,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            i => results[i] = HtmlSanitizer.Sanitize(inputs[i % inputs.Length]));

        for (var i = 0; i < results.Length; i++)
            Assert.Equal(expected[i % inputs.Length], results[i]);
    }

    [Fact]
    public void Of_several_roles_only_the_content_ones_are_kept()
    {
        Assert.Equal("<div role=\"note\">x</div>", HtmlSanitizer.Sanitize("<div role=\"dialog note\">x</div>"));
        Assert.Equal("<div>x</div>", HtmlSanitizer.Sanitize("<div role=\"dialog alertdialog\">x</div>"));
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
