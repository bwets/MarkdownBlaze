using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MarkdownBlaze.Security;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>
/// The hostile-but-harmless sample documents in <c>samples/wiki/security</c>: each one is rendered the way the
/// app renders it, and the markers of what it tries are looked for in what would reach the window. They
/// are the same payloads a person is asked to open and check by eye, so the pages cannot go stale.
/// </summary>
public sealed partial class SecuritySamplesTests
{
    private const int RegexTimeoutMilliseconds = 2000;
    private const string DeepNesting = "deep-nesting.md";

    private static readonly MarkdownService Markdown = new();
    private static readonly string SamplesFolder = Path.Combine(TestFiles.RepoRoot, "samples", "wiki", "security");

    public static TheoryData<string> Samples()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(SamplesFolder, "*.md").Order(StringComparer.Ordinal))
        {
            // deep-nesting.md is the page that is refused: it has its own test, and nothing to look at in what it renders to
            if (!file.EndsWith(DeepNesting, StringComparison.Ordinal)) data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [GeneratedRegex(@"\b(?:href|src)\s*=\s*""(?<g>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex UrlAttribute { get; }

    [GeneratedRegex(@"\bdata-nav\s*=\s*""(?<g>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex NavAttribute { get; }

    [GeneratedRegex(@"<code>.*?</code>", RegexOptions.Singleline, RegexTimeoutMilliseconds)]
    private static partial Regex CodeSpan { get; }

    /// <summary>
    /// Text that is source for a library and not markup: a diagram's source in <c>&lt;pre class="mermaid"&gt;</c> and a formula's
    /// in <c>class="math"</c>. It is escaped text in the page, and the pages quote hostile source on purpose; what stops it from
    /// doing anything is the configuration the libraries are started with (the JavaScript tests), not the sanitizer.
    /// </summary>
    [GeneratedRegex(@"<(?<tag>pre|span|div) class=""(?:mermaid|math)"".*?</\k<tag>>", RegexOptions.Singleline | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex LibrarySource { get; }

    [GeneratedRegex(@"\bon[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex EventHandler { get; }

    [GeneratedRegex(@"position\s*:\s*fixed", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex FixedPosition { get; }

    [GeneratedRegex(@"<input\b(?![^>]*type=""checkbox"")", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex NonCheckboxInput { get; }

    [GeneratedRegex(@"\bstyle\s*=\s*""(?<g>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex StyleAttribute { get; }

    [Fact]
    public void The_sample_pages_and_their_index_exist_and_the_library_links_to_them()
    {
        var pages = Directory.EnumerateFiles(SamplesFolder, "*.md").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            [
                "arguments-and-protocol.md", "dangerous-links.md", "deep-nesting.md", "diagrams-and-math.md", "file-access.md", "index.md", "page-hijack.md",
                "phishing.md", "resource-limits.md", "script-injection.md", "ui-spoofing.md",
            ],
            pages,
            StringComparer.Ordinal);
        Assert.Contains("security/", File.ReadAllText(Path.Combine(TestFiles.RepoRoot, "samples", "wiki", "index.md")), StringComparison.Ordinal);
    }

    /// <summary>
    /// SEC-13 and SEC-11, for every page: the page takes no class outside the list the renderer emits, no id or role
    /// of the app's, and is no deeper than a browser nests. A page that is not clean here would cover the window or
    /// pass for it, or would be flattened by the browser anyway.
    /// </summary>
    [Theory]
    [MemberData(nameof(Samples))]
    public void No_page_borrows_a_name_of_the_apps_or_nests_deeper_than_a_browser_shows(string name)
    {
        var complaints = PagePolicy.Violations(Render(name));

        Assert.Empty(complaints);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void No_dangerous_marker_survives_in_what_reaches_the_window(string name)
    {
        var html = MarkupOnly(Render(name));

        foreach (var marker in new[]
            {
                "<script",
                "javascript:",
                "vbscript:",
                "<iframe",
                "<object",
                "<embed",
                "<meta",
                "<base",
                "<form",
                "<style",
                "<link",
                "<svg",
                "<math",
                "<frame",
                "srcdoc",
                "formaction",
                "target=",
            })
        {
            Assert.DoesNotContain(marker, html, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotMatch(EventHandler, html); // onerror, onload, onfocus, ontoggle, onauxclick, ...
        Assert.DoesNotMatch(FixedPosition, html);
        Assert.DoesNotMatch(NonCheckboxInput, html);
        Assert.All(
            StyleAttribute.Matches(html).Select(m => m.Groups["g"].Value),
            style => Assert.Matches(@"^\s*text-align\s*:\s*(left|right|center)\s*;?\s*$", style)); // what Markdig writes on a table cell
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_address_is_inert_or_on_the_allow_list_and_nothing_carries_data_but_the_placeholder(string name)
    {
        var html = MarkupOnly(Render(name)); // the pages quote their payloads in code spans, where an address is text

        foreach (Match match in UrlAttribute.Matches(html))
        {
            var url = WebUtility.HtmlDecode(match.Groups["g"].Value);
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                // The only data: address is the app's own placeholder for an image it refused.
                Assert.StartsWith("data:image/svg+xml;base64,", url, StringComparison.Ordinal);
                var svg = Encoding.UTF8.GetString(Convert.FromBase64String(url["data:image/svg+xml;base64,".Length..]));
                Assert.Matches("(Image not displayed|Image too large to display|Too many images)", svg);
                continue;
            }

            Assert.True(
                url.StartsWith('#') || !url.Contains(':', StringComparison.Ordinal) || ExternalLinks.IsAllowed(url),
                $"{name}: '{url}' is neither an anchor, a relative address nor on the allow-list");
            Assert.True(
                url.StartsWith('#') || !url.Contains(':', StringComparison.Ordinal) || ExternalLinks.IsAllowed(url),
                $"{name}: '{url}' carries a scheme that is not allowed");
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Nothing_in_a_sample_can_lead_the_viewer_outside_the_sample_folder(string name)
    {
        var html = Render(name);

        foreach (Match match in NavAttribute.Matches(html))
        {
            var target = WebUtility.HtmlDecode(match.Groups["g"].Value);
            if (NavigationService.IsFollowable(target, currentDocument: null, fromLink: true))
            {
                // A link the viewer wrote itself, to a page of the sample wiki.
                Assert.StartsWith(Path.Combine(TestFiles.RepoRoot, "samples", "wiki"), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---- what each page is expected to leave ------------------------------------------------------------

    [Fact]
    public void Script_injection_keeps_the_harmless_markup_and_drops_the_hostile()
    {
        var html = Render("script-injection.md");
        var markup = MarkupOnly(html);

        Assert.Contains("<details", html, StringComparison.Ordinal); // the collapsible section survives, without its handler
        Assert.Contains("<summary>Open this section</summary>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal); // the task list
        // the disguised javascript: links are text, not links
        Assert.Contains("written with a character entity", html, StringComparison.Ordinal);
        Assert.DoesNotContain("alert(", markup, StringComparison.Ordinal); // outside the code spans that quote the payloads
    }

    [Fact]
    public void Dangerous_links_are_inert_and_the_allowed_ones_still_open()
    {
        var html = Render("dangerous-links.md");

        // file:, a drive path, a UNC path and a file: share are marked inert; the schemes that are not on
        // the allow-list lose their address altogether and are plain text in an anchor without a target.
        Assert.Equal(4, Occurrences(html, "data-md-blocked=\"link\""));
        Assert.Equal(5, Occurrences(html, "<a>"));
        Assert.Contains("href=\"https://example.com\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:someone@example.com\"", html, StringComparison.Ordinal);
        Assert.Contains("data-nav=", html, StringComparison.Ordinal); // the link to a local page
        Assert.DoesNotContain("calc.exe", MarkupOnly(html), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attacker", MarkupOnly(html), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void File_access_shows_only_placeholders_and_no_file_content()
    {
        var html = Render("file-access.md");

        // Seven hostile images, seven refusals, and not one byte of a file in the page — nor an address
        // (mdasset://) that would make the app serve one. One of the seven, CON.png, is a device only
        // on Windows: elsewhere it is an ordinary name with no file behind it, left as written.
        var refused = OperatingSystem.IsWindows() ? 7 : 6;
        Assert.Equal(7, Occurrences(html, "<img "));
        Assert.DoesNotContain("src=\"mdasset:", html, StringComparison.Ordinal);
        Assert.Equal(refused, Occurrences(html, "data-md-skipped=\"blocked\""));
        Assert.Equal(refused, Occurrences(html, "data:image/svg+xml;base64,"));
        if (!OperatingSystem.IsWindows()) Assert.Contains("src=\"CON.png\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[fonts]", html, StringComparison.OrdinalIgnoreCase); // win.ini
        Assert.DoesNotContain("root:", html, StringComparison.Ordinal); // /etc/passwd
        Assert.DoesNotContain("attacker", MarkupOnly(html), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Page_hijack_loses_everything_but_the_remote_picture()
    {
        var html = Render("page-hijack.md");

        Assert.Contains("src=\"https://example.com/pixel.png\"", html, StringComparison.Ordinal); // the accepted behaviour
        Assert.DoesNotContain("example.com/collect", html, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.css", html, StringComparison.Ordinal);
        Assert.DoesNotContain("display: none", html, StringComparison.OrdinalIgnoreCase);
        // the forged in-app link is gone altogether: the sanitizer removes every data-nav an author wrote
        // (the page's one real link, to the folder's index, is the only anchor that still has one) ...
        Assert.DoesNotContain("win.ini", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Occurrences(html, "data-nav="));
        // ... and the second layer would refuse it anyway
        Assert.False(NavigationService.IsFollowable("C:\\Windows\\win.ini", currentDocument: null, fromLink: true));
        Assert.Contains("A forged sidebar", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Ui_spoofing_keeps_the_text_and_the_elements_and_loses_the_names()
    {
        var html = Render("ui-spoofing.md");
        var body = PagePolicy.Parse(html);

        foreach (var borrowed in new[] { "menu-backdrop", "rate-bar", "splash", "splash-name", "ctx", "ctx-right", "settings-overlay" })
            Assert.Empty(body.QuerySelectorAll("." + borrowed));

        Assert.Empty(body.QuerySelectorAll("#md-tree, #md-splitter, [role=dialog], [aria-modal], [aria-label]"));
        Assert.Contains("Please type your password into the box below", body.TextContent, StringComparison.Ordinal); // the element stays, as text
        Assert.Contains("Copy", body.TextContent, StringComparison.Ordinal);
        Assert.Contains("Your computer is at risk", body.TextContent, StringComparison.Ordinal);
        Assert.Contains("the content of a custom container", body.TextContent, StringComparison.Ordinal);
        Assert.Equal("https://example.com", body.QuerySelector("a[href^='https://example.com']")!.GetAttribute("href")); // the link carrying {.menu-backdrop}
        Assert.Contains("A heading that tries to be the rating bar", body.QuerySelector("h3")!.TextContent, StringComparison.Ordinal);

        // and what is legitimate keeps its classes
        Assert.NotEmpty(body.QuerySelectorAll(".admonition.admonition-note"));
        Assert.NotEmpty(body.QuerySelectorAll("ul.contains-task-list > li.task-list-item"));
        Assert.NotEmpty(body.QuerySelectorAll("code.language-csharp"));
        Assert.NotEmpty(body.QuerySelectorAll("a.footnote-ref"));
        Assert.NotEmpty(body.QuerySelectorAll("span.math"));
    }

    [Fact]
    public void Deep_nesting_is_refused_by_the_parser_and_the_page_says_so()
    {
        var markdown = File.ReadAllText(Path.Combine(SamplesFolder, DeepNesting));

        var refused = Assert.ThrowsAny<ArgumentException>(() => Markdown.RenderText(DeepNesting, markdown));

        Assert.True(MarkdownService.IsNestingLimit(refused));
        Assert.Contains(new string('>', 1200), markdown, StringComparison.Ordinal);
        Assert.Contains(new string('[', 4000), markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Resource_limits_renders_at_all_and_is_cut_to_the_depth_of_a_browser()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var html = Render("resource-limits.md");
        var seconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
        var body = PagePolicy.Parse(html);

        Assert.True(seconds < 20, "rendering took " + seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s");
        Assert.True(NestingLimit.DepthOf(body) <= NestingLimit.MaxDepth);
        Assert.Contains("This sentence sits a thousand elements deep", body.TextContent, StringComparison.Ordinal);
        Assert.Contains("This sentence sits in a quotation inside 1,199 others", body.TextContent, StringComparison.Ordinal);
        Assert.Equal(3000, body.QuerySelectorAll("h6").Count(heading => string.Equals(heading.TextContent, "h", StringComparison.Ordinal)));
        Assert.Contains(new string('[', 120), body.TextContent, StringComparison.Ordinal); // the 60 pairs of brackets are text

        // the diagram with 600 edges and the 104,000-character block of code reach the page as they are; Mermaid and
        // highlight.js are the ones with limits for those (the JavaScript tests)
        var diagram = body.QuerySelectorAll("pre.mermaid").Single(source => source.TextContent.Contains("N599", StringComparison.Ordinal));
        Assert.Equal(600, diagram.TextContent.Split("-->").Length - 1);
        var big = body.QuerySelectorAll("pre > code.language-javascript").Single(code => code.TextContent.Length > 100_000);
        Assert.InRange(big.TextContent.Length, 100_001, 110_000);
        Assert.Equal(2, body.QuerySelectorAll("span.math, div.math").Length);
    }

    [Fact]
    public void Diagrams_and_math_hands_the_libraries_their_source_and_makes_nothing_of_it_itself()
    {
        var html = Render("diagrams-and-math.md");
        var body = PagePolicy.Parse(html);

        var diagrams = body.QuerySelectorAll("pre.mermaid").Select(diagram => diagram.TextContent).ToList();
        Assert.Equal(6, diagrams.Count);
        Assert.Contains(diagrams, source => source.Contains("\"securityLevel\": \"loose\"", StringComparison.Ordinal));
        Assert.Contains(diagrams, source => source.Contains("themeCSS", StringComparison.Ordinal));
        Assert.Contains(diagrams, source => source.Contains("click A \"https://example.com\" _blank", StringComparison.Ordinal));
        Assert.Contains(diagrams, source => source.Contains("<img src=x onerror=alert('label')>", StringComparison.Ordinal)); // text, as written

        var formulas = body.QuerySelectorAll("span.math, div.math").Select(formula => formula.TextContent).ToList();
        Assert.Equal(6, formulas.Count);
        foreach (var command in new[] { "\\href", "\\url", "\\includegraphics", "\\htmlClass", "\\htmlId" })
            Assert.Contains(formulas, formula => formula.Contains(command, StringComparison.Ordinal));

        // no anchor, picture or class has come out of any of it
        Assert.Empty(body.QuerySelectorAll("a[href*='javascript'], img"));
        Assert.Empty(body.QuerySelectorAll(".menu-backdrop"));
    }

    [Fact]
    public void Arguments_and_protocol_leaves_no_link_and_no_picture_address_of_the_apps_own()
    {
        var html = Render("arguments-and-protocol.md");
        var body = PagePolicy.Parse(html);

        // no address on the page is one of the hostile ones, wherever it would be written (the page quotes them in code spans, as text)
        foreach (var attribute in body.QuerySelectorAll("[href], [src], [data-nav]").SelectMany(element => element.Attributes))
        {
            foreach (var forbidden in new[] { "markdown:", "mdasset", "--unregister", "calc.exe" })
                Assert.DoesNotContain(forbidden, attribute.Value, StringComparison.OrdinalIgnoreCase);
        }

        string[] hostileTexts = ["calculator", "Unregister", "A link to a picture"];
        var hostile = body.QuerySelectorAll("a")
            .Where(anchor => hostileTexts.Any(text => anchor.TextContent.Contains(text, StringComparison.Ordinal)))
            .ToList();
        Assert.Equal(3, hostile.Count);
        Assert.All(hostile, anchor => Assert.Null(anchor.GetAttribute("href"))); // text in an anchor with no address: not a link

        var picture = Assert.Single(body.QuerySelectorAll("img"));
        Assert.Null(picture.GetAttribute("src"));
    }

    [Fact]
    public void Phishing_keeps_the_real_address_in_the_link_and_the_lie_in_the_text()
    {
        var html = Render("phishing.md");
        var body = PagePolicy.Parse(html);

        var lying = Link(body, "https://good.example/account");
        Assert.Equal("https://evil.invalid/login", lying.GetAttribute("href"));

        var overridden = body.QuerySelectorAll("a").Single(anchor => anchor.TextContent.Contains((char)0x202E, StringComparison.Ordinal));
        Assert.Equal("https://evil.invalid/annex.exe", overridden.GetAttribute("href"));

        var homoglyph = Link(body, "https://apple.com");
        Assert.Equal("https://" + (char)0x430 + "pple.com/", homoglyph.GetAttribute("href")); // a Cyrillic "a": the status strip writes it as xn--pple-43d.com

        var titled = Link(body, "Documentation");
        Assert.Equal("https://evil.invalid/docs", titled.GetAttribute("href"));
        Assert.Equal("https://good.example/docs", titled.GetAttribute("title"));

        // the address of every link that opens one is an allowed one, as on every other page
        Assert.All(
            body.QuerySelectorAll("a[href]").Select(anchor => anchor.GetAttribute("href")!).Where(href => !href.StartsWith('#')),
            href => Assert.True(ExternalLinks.IsAllowed(href), href));
    }

    private static AngleSharp.Dom.IElement Link(AngleSharp.Dom.IElement body, string text) =>
        body.QuerySelectorAll("a").Single(anchor => string.Equals(anchor.TextContent, text, StringComparison.Ordinal));

    [Fact]
    public void The_index_describes_the_folder_and_links_to_every_page()
    {
        var html = Render("index.md");

        string[] pages =
        [
            "script-injection", "dangerous-links", "file-access", "page-hijack", "ui-spoofing", "resource-limits", "diagrams-and-math",
            "arguments-and-protocol", "phishing",
        ];
        foreach (var page in pages)
            Assert.Contains(page + ".md", html, StringComparison.Ordinal);

        Assert.Contains("deep-nesting", html, StringComparison.Ordinal); // the page that is refused, described in the index
    }

    private static string Render(string name)
    {
        var result = Markdown.Render(Path.Combine(SamplesFolder, name));
        Assert.NotNull(result);
        return result.BodyHtml;
    }

    /// <summary>The page without its code spans: the pages quote each payload in one, and a quote is not a payload.</summary>
    private static string MarkupOnly(string html) => LibrarySource.Replace(CodeSpan.Replace(html, string.Empty), string.Empty);

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
