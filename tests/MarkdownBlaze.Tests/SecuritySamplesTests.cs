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

    private static readonly MarkdownService Markdown = new();
    private static readonly string SamplesFolder = Path.Combine(TestFiles.RepoRoot, "samples", "wiki", "security");

    public static TheoryData<string> Samples()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(SamplesFolder, "*.md").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(file));

        return data;
    }

    [GeneratedRegex(@"\b(?:href|src)\s*=\s*""(?<g>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex UrlAttribute { get; }

    [GeneratedRegex(@"\bdata-nav\s*=\s*""(?<g>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex NavAttribute { get; }

    [GeneratedRegex(@"<code>.*?</code>", RegexOptions.Singleline, RegexTimeoutMilliseconds)]
    private static partial Regex CodeSpan { get; }

    [GeneratedRegex(@"\bon[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex EventHandler { get; }

    [GeneratedRegex(@"position\s*:\s*fixed", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex FixedPosition { get; }

    [GeneratedRegex(@"<input\b(?![^>]*type=""checkbox"")", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex NonCheckboxInput { get; }

    [GeneratedRegex(@"\bstyle\s*=\s*""(?<g>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex StyleAttribute { get; }

    [Fact]
    public void The_four_sample_pages_and_their_index_exist_and_the_library_links_to_them()
    {
        var pages = Directory.EnumerateFiles(SamplesFolder, "*.md").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["dangerous-links.md", "file-access.md", "index.md", "page-hijack.md", "script-injection.md"], pages, StringComparer.Ordinal);
        Assert.Contains("security/", File.ReadAllText(Path.Combine(TestFiles.RepoRoot, "samples", "wiki", "index.md")), StringComparison.Ordinal);
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
        var html = Render(name);

        foreach (Match match in UrlAttribute.Matches(html))
        {
            var url = WebUtility.HtmlDecode(match.Groups["g"].Value);
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                // The only data: address is the app's own placeholder for an image it refused.
                Assert.StartsWith("data:image/svg+xml;base64,", url, StringComparison.Ordinal);
                var svg = Encoding.UTF8.GetString(Convert.FromBase64String(url["data:image/svg+xml;base64,".Length..]));
                Assert.Matches("(Image not displayed|Image too large to display)", svg);
                continue;
            }

            Assert.True(
                url.StartsWith('#') || !url.Contains(':', StringComparison.Ordinal) || ExternalLinks.IsAllowed(url),
                $"{name}: '{url}' is neither an anchor, a relative address nor on the allow-list");
            Assert.True(
                !url.Contains(':', StringComparison.Ordinal) || ExternalLinks.IsAllowed(url),
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
        // (mdasset://) that would make the app serve one.
        Assert.Equal(7, Occurrences(html, "<img "));
        Assert.DoesNotContain("src=\"mdasset:", html, StringComparison.Ordinal);
        Assert.Equal(7, Occurrences(html, "data-md-skipped=\"blocked\""));
        Assert.Equal(7, Occurrences(html, "data:image/svg+xml;base64,"));
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
    public void The_index_describes_the_folder_and_links_to_every_page()
    {
        var html = Render("index.md");

        foreach (var page in new[] { "script-injection", "dangerous-links", "file-access", "page-hijack" })
            Assert.Contains(page + ".md", html, StringComparison.Ordinal);
    }

    private static string Render(string name)
    {
        var result = Markdown.Render(Path.Combine(SamplesFolder, name));
        Assert.NotNull(result);
        return result.BodyHtml;
    }

    /// <summary>The page without its code spans: the pages quote each payload in one, and a quote is not a payload.</summary>
    private static string MarkupOnly(string html) => CodeSpan.Replace(html, string.Empty);

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
