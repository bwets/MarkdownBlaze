using System.Globalization;
using System.Text;
using AngleSharp.Html.Parser;
using MarkdownBlaze.Security;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>
/// SEC-11: a document can nest HTML far deeper than anything that walks the tree recurses safely. The
/// tree is cut down to a browser's own depth before the sanitizer sees it, and the renders that used to end
/// the process ("Stack overflow" in <c>Node.Normalize</c>) come back with a page.
/// </summary>
public sealed class NestingLimitTests
{
    private static string Nested(string open, string close, int depth, string inside = "x") =>
        string.Concat(Enumerable.Repeat(open, depth)) + inside + string.Concat(Enumerable.Repeat(close, depth));

    private static AngleSharp.Html.Dom.IHtmlDocument Parse(string html) =>
        new HtmlParser().ParseDocument("<!doctype html><html><body>" + html);

    /// <summary>The deepest element of markup the page would be given, measured by parsing it as the page does.</summary>
    private static int DepthOfMarkup(string html) => NestingLimit.DepthOf(Parse(html).Body!);

    // ---- the cut ---------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_within_the_limit_is_touched()
    {
        var html = Nested("<div>", "</div>", NestingLimit.MaxDepth);
        using var document = Parse(html);

        var flattened = NestingLimit.Apply(document.Body!);

        Assert.Equal(0, flattened);
        Assert.Equal(NestingLimit.MaxDepth, NestingLimit.DepthOf(document.Body!));
        Assert.Equal(html, document.Body!.InnerHtml);
    }

    [Fact]
    public void An_element_below_the_limit_becomes_its_text_in_document_order()
    {
        // 512 levels, then at the bottom: text, an element holding text, text again, a comment.
        var html = Nested("<div>", "</div>", NestingLimit.MaxDepth, "a<b>b<i>c</i>d</b>e<!--hidden-->f");
        using var document = Parse(html);

        var flattened = NestingLimit.Apply(document.Body!);

        Assert.Equal(1, flattened); // the <b>, standing for everything below it
        Assert.Equal(NestingLimit.MaxDepth, NestingLimit.DepthOf(document.Body!));
        Assert.Contains("abcde", document.Body!.TextContent, StringComparison.Ordinal); // the comment is not text
        Assert.DoesNotContain("<b>", document.Body!.InnerHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", document.Body!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_element_with_no_text_below_the_limit_is_removed()
    {
        using var document = Parse(Nested("<div>", "</div>", NestingLimit.MaxDepth, "<span></span><hr>"));

        var flattened = NestingLimit.Apply(document.Body!);

        Assert.Equal(2, flattened);
        Assert.Equal(NestingLimit.MaxDepth, NestingLimit.DepthOf(document.Body!));
        Assert.DoesNotContain("<span", document.Body!.InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Markup_written_as_text_below_the_limit_stays_text()
    {
        // The content of a <script> that ends up below the limit is kept as text — and written out as
        // text, because the element that held it is gone.
        var html = Nested("<div>", "</div>", NestingLimit.MaxDepth + 3, "<script>alert('x')</script>");
        using var document = Parse(html);

        _ = NestingLimit.Apply(document.Body!);

        Assert.DoesNotContain("<script", document.Body!.InnerHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alert('x')", document.Body!.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<div>", "</div>", 20_000)]
    [InlineData("<b>", "", 30_000)] // never closed: the parser closes them at the end
    [InlineData("<blockquote>", "</blockquote>", 20_000)]
    [InlineData("<ul><li>", "</li></ul>", 10_000)]
    [InlineData("<span><a href=\"#\">", "</a></span>", 30_000)]
    [InlineData("<table><tr><td>", "</td></tr></table>", 10_000)]
    public void A_tree_of_any_depth_is_cut_without_recursion(string open, string close, int depth)
    {
        // Run on the test's own thread: it has an ordinary stack, which a recursive walk of this tree would overflow.
        using var document = Parse(Nested(open, close, depth));

        _ = NestingLimit.Apply(document.Body!);

        Assert.True(NestingLimit.DepthOf(document.Body!) <= NestingLimit.MaxDepth);
        Assert.Contains("x", document.Body!.TextContent, StringComparison.Ordinal);
    }

    // ---- through the whole renderer ------------------------------------------------------------------------

    private static readonly MarkdownService Markdown = new();

    /// <summary>The probe cases that ended the process: 50,000 divs (300 KB), 100,000 unclosed bold, and their kin.</summary>
    public static TheoryData<string, string, string, int> HostileNesting() => new()
    {
        { "div-nest", "<div>", "</div>", 50_000 },
        { "b-nest-unclosed", "<b>", string.Empty, 100_000 },
        { "ul-li-nest", "<ul><li>", "</li></ul>", 10_000 },
        { "blockquote-html-nest", "<blockquote>", "</blockquote>", 10_000 },
        { "span-nest", "<span>", "</span>", 30_000 },
    };

    [Theory]
    [MemberData(nameof(HostileNesting))]
    public void A_hostile_nesting_of_html_renders_to_a_page_no_deeper_than_a_browser_shows(string name, string open, string close, int depth)
    {
        var html = Markdown.RenderText("hostile.md", Nested(open, close, depth)).BodyHtml;

        var actual = DepthOfMarkup(html);
        Assert.True(actual <= NestingLimit.MaxDepth, name + ": the page is " + Number(actual) + " elements deep");
        Assert.Contains("x", html, StringComparison.Ordinal); // what was there to read is still there
    }

    [Fact]
    public void Ten_thousand_nested_quotes_and_six_hundred_nested_lists_in_markdown_are_refused_by_markdig_not_crashed_on()
    {
        var quotes = string.Concat(Enumerable.Repeat(">", 10_000)) + " x";
        var lists = string.Join('\n', Enumerable.Range(0, 600).Select(i => new string(' ', i * 2) + "- x"));

        foreach (var markdown in new[] { quotes, lists })
        {
            string? html = null;
            Exception? refused = null;
            try
            {
                html = Markdown.RenderText("deep.md", markdown).BodyHtml;
            }
            catch (ArgumentException ex)
            {
                refused = ex;
            }

            if (refused is not null)
                Assert.True(MarkdownService.IsNestingLimit(refused), refused.Message);
            else
                Assert.True(DepthOfMarkup(html!) <= NestingLimit.MaxDepth);
        }
    }

    [Fact]
    public void Markdowns_own_depth_limit_is_recognised_and_other_argument_errors_are_not()
    {
        var quotes = string.Concat(Enumerable.Repeat(">", 20_000)) + " x";

        var ex = Assert.ThrowsAny<ArgumentException>(() => Markdown.RenderText("deep.md", quotes));

        Assert.True(MarkdownService.IsNestingLimit(ex));
        Assert.False(MarkdownService.IsNestingLimit(Argument("bad value", "markdown")));
        Assert.False(MarkdownService.IsNestingLimit(Argument("Markdown elements are too deeply nested", "markdown"))); // it names a parameter: not Markdig's
        Assert.False(MarkdownService.IsNestingLimit(new InvalidOperationException("too deeply nested")));
    }

    [Fact]
    public void A_legitimate_twenty_deep_nested_list_is_unchanged()
    {
        var markdown = string.Join('\n', Enumerable.Range(0, 20).Select(i => new string(' ', i * 2) + "- item " + Number(i + 1)));

        var html = Markdown.RenderText("nested.md", markdown).BodyHtml;

        Assert.Equal(40, DepthOfMarkup(html)); // a <ul> and an <li> for each of the twenty levels
        for (var i = 1; i <= 20; i++)
            Assert.Contains("item " + Number(i), html, StringComparison.Ordinal);

        Assert.Equal(20, CountOf(html, "<ul>"));
    }

    [Fact]
    public void The_text_of_the_deep_part_is_kept_in_order()
    {
        var text = new StringBuilder();
        for (var i = 0; i < 600; i++) _ = text.Append("<div>").Append(i).Append(' ');
        var html = Markdown.RenderText("order.md", text.ToString()).BodyHtml;

        var seen = Parse(html).Body!.TextContent;
        var last = -1;
        foreach (var number in seen.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var value = int.Parse(number, CultureInfo.InvariantCulture);
            Assert.True(value > last, Number(value) + " after " + Number(last));
            last = value;
        }

        Assert.Equal(599, last);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static ArgumentException Argument(string message, string parameter) => new(message, parameter);

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
