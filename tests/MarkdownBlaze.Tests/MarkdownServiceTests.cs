using System.Text.RegularExpressions;
using MarkdownBlaze.Models;
using MarkdownBlaze.Security;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>
/// What the renderer makes of a document: the link rewrites the bundled link-test page documents (as a
/// table), headings and their anchors, the title, the language and the "what's new" page.
/// </summary>
public sealed partial class MarkdownServiceTests : IDisposable
{
    private static readonly MarkdownService Markdown = new();
    private readonly TempFolder _wiki = CreateWiki();

    public void Dispose() => _wiki.Dispose();

    // ---- links: the table of samples/wiki/link-test.md ---------------------------------------------------

    /// <summary>
    /// Every kind of link the link-test page contains, with what the renderer is expected to leave in the
    /// page for it: a <c>data-nav</c> target relative to the wiki folder, an unchanged <c>href</c>, an
    /// inert <c>#</c> carrying <c>data-md-blocked</c>, or no <c>href</c> at all.
    /// </summary>
    public static TheoryData<string, string?, string?, bool> LinkCases() => new()
    {
        // Should open in the default browser / mail client / other allowed handlers: left alone.
        { "https://example.com", null, "https://example.com", false },
        { "http://example.com", null, "http://example.com", false },
        { "https://example.com/search?q=markdown#results", null, "https://example.com/search?q=markdown#results", false },
        { "//example.com", null, "//example.com", false },
        { "mailto:gregory.kieffer@gmail.com", null, "mailto:gregory.kieffer@gmail.com", false },
        { "mailto:someone@example.com?subject=MarkdownBlaze&body=Hello", null, "mailto:someone@example.com?subject=MarkdownBlaze&amp;body=Hello", false },
        { "tel:+3212345678", null, "tel:+3212345678", false },
        { "ftp://ftp.gnu.org/gnu/", null, "ftp://ftp.gnu.org/gnu/", false },

        // Should navigate in-app (local Markdown).
        { "index.md", "index.md", "#", false },
        { "dune/houses", Path.Combine("dune", "houses.md"), "#", false },
        { "asimov/", Path.Combine("asimov", "index.md"), "#", false },
        { "dune/houses.md#atreides", Path.Combine("dune", "houses.md"), "#", false },
        { "dune/houses.md?x=1", Path.Combine("dune", "houses.md"), "#", false },
        { "my%20notes.md", "my notes.md", "#", false },

        // Should scroll within the page.
        { "#link-handling-test", null, "#link-handling-test", false },

        // Should do nothing: the renderer leaves them as written and the page ignores the click.
        { "/etc/hosts", null, "/etc/hosts", false },
        { "./diagram.png", null, "./diagram.png", false },
        { "./some-page.html", null, "./some-page.html", false },

        // Dangerous if they did anything: no address reaches the page at all.
        { "javascript:alert('nope')", null, null, false },
        { "ms-msdt:/id", null, null, false },
    };

    [Theory]
    [MemberData(nameof(LinkCases))]
    public void A_link_becomes_what_the_link_test_page_says(string url, string? navigatesTo, string? href, bool blocked)
    {
        var html = Markdown.Render(_wiki.File("doc.md", $"[label](<{url}>)"))!.BodyHtml;

        if (navigatesTo is null)
            Assert.DoesNotContain("data-nav", html, StringComparison.Ordinal);
        else
            Assert.Contains($"data-nav=\"{Path.Combine(_wiki.Path, navigatesTo)}\"", html, StringComparison.Ordinal);

        if (href is null)
            Assert.DoesNotContain("href=", html, StringComparison.Ordinal);
        else
            Assert.Contains($"href=\"{href}\"", html, StringComparison.Ordinal);

        Assert.Equal(blocked, html.Contains("data-md-blocked", StringComparison.Ordinal));
    }

    [SkippableTheory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("C:/Windows/System32/calc.exe")]
    public void A_link_to_a_program_by_its_address_is_inert(string url)
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        var html = Markdown.Render(_wiki.File("doc.md", $"[label](<{url}>)"))!.BodyHtml;

        Assert.Contains("href=\"#\"", html, StringComparison.Ordinal);
        Assert.Contains("data-md-blocked=\"link\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("calc.exe", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_in_app_link_of_the_bundled_link_test_page_leads_to_a_page_that_exists()
    {
        var page = FindSamplePage("link-test.md");

        var html = Markdown.Render(page)!.BodyHtml;
        var targets = NavTarget.Matches(html).Select(m => m.Groups["target"].Value).ToList();

        Assert.True(targets.Count >= 3, "the link-test page should navigate in-app to at least three pages");
        foreach (var target in targets)
            Assert.True(File.Exists(target), $"data-nav target does not exist: {target}");
    }

    // ---- headings and slugs ----------------------------------------------------------------------

    [Fact]
    public void Headings_are_listed_in_order_with_their_level_text_and_anchor()
    {
        var result = Markdown.RenderText(
            "doc.md",
            "# The Title\n\n## Section *One*\n\n### A `code` and **bold** heading\n\n## Section *One*\n\n#### Déjà vu & more\n");

        Assert.Collection(
            result.Headings,
            h => AssertHeading(h, 1, "The Title", "the-title"),
            h => AssertHeading(h, 2, "Section One", "section-one"),
            h => AssertHeading(h, 3, "A code and bold heading", "a-code-and-bold-heading"),
            h => AssertHeading(h, 2, "Section One", "section-one-1"),
            h => AssertHeading(h, 4, "Déjà vu & more", "deja-vu-more")); // the anchor has no accents
    }

    [Fact]
    public void A_custom_heading_id_is_the_anchor_and_empty_headings_are_skipped()
    {
        var result = Markdown.RenderText("doc.md", "# Title {#custom}\n\n## \n\n##   \n\n## Next\n");

        Assert.Collection(
            result.Headings,
            h => AssertHeading(h, 1, "Title", "custom"),
            h => AssertHeading(h, 2, "Next", "next"));
    }

    [Fact]
    public void Headings_inside_code_blocks_are_not_headings()
    {
        var result = Markdown.RenderText("doc.md", "# Real\n\n```\n# not a heading\n```\n\n    # indented code\n");

        _ = Assert.Single(result.Headings);
    }

    // ---- title -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("# First\n\n## Second\n", "First")]
    [InlineData("## Only a second level\n\ntext\n", "Only a second level")]
    [InlineData("## Second comes first\n\n# Then the h1\n", "Then the h1")]
    [InlineData("just text, no heading at all\n", "notes")]
    [InlineData("", "notes")]
    public void The_title_is_the_first_h1_else_the_first_heading_else_the_file_name(string markdown, string expected)
    {
        var result = Markdown.Render(_wiki.File("notes.md", markdown))!;

        Assert.Equal(expected, result.Title);
    }

    [Fact]
    public void A_dropped_document_has_a_name_but_no_folder()
    {
        var result = Markdown.RenderText("  Dropped.md ", "text");

        Assert.Equal("Dropped.md", result.File);
        Assert.Equal("Dropped", result.Title);
        Assert.Equal("Untitled.md", Markdown.RenderText("   ", "text").File);
    }

    // ---- language --------------------------------------------------------------------------------

    [Theory]
    [InlineData("lang: fr", "fr")]
    [InlineData("language: pt_BR", "pt-BR")]
    [InlineData("locale: \"nb\"", "nb")]
    [InlineData("title: Hi\nlang: de-AT\ntags: [a]", "de-AT")]
    public void The_front_matter_declares_the_language(string frontMatter, string expected)
    {
        var result = Markdown.RenderText("doc.md", $"---\n{frontMatter}\n---\n\n# Hello\n\nA short note.\n");

        Assert.Equal(expected, result.Language);
    }

    [Fact]
    public void A_declared_language_wins_over_what_the_prose_reads_as()
    {
        var english = string.Concat(Enumerable.Repeat("The cat is on the mat and the dog is in the house with you. ", 5));

        var result = Markdown.RenderText("doc.md", $"---\nlang: fr\n---\n\n{english}");

        Assert.Equal("fr", result.Language);
    }

    [Fact]
    public void Without_a_declaration_the_prose_decides_and_a_short_document_has_no_language()
    {
        var english = string.Concat(Enumerable.Repeat("The cat is on the mat and the dog is in the house with you. ", 5));

        Assert.Equal("en", Markdown.RenderText("doc.md", english).Language);
        Assert.Null(Markdown.RenderText("doc.md", "Too short to tell.").Language);
    }

    // ---- what's new ------------------------------------------------------------------------------

    [Fact]
    public void The_whats_new_page_names_the_version_that_is_running()
    {
        var html = Markdown.RenderWhatsNew("# What's new\n\n## {{version}}\n\n- something\n\n## 1.0.3\n\n- older\n", "2.5.7");

        Assert.Contains("2.5.7", html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{version}}", html, StringComparison.Ordinal);
        Assert.Contains("1.0.3", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_build_without_a_version_says_so_instead_of_showing_the_placeholder()
    {
        var html = Markdown.RenderWhatsNew("## {{version}}\n\n- something\n", version: null);

        Assert.Contains("This build", html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{version}}", html, StringComparison.Ordinal);
    }

    /// <summary>A build made without a version of its own says 0.0.0-dev, and that is "no version", not a number to show.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("0.0.0-dev", null)]
    [InlineData("0.0.0-dev+3f9c2a1", null)]
    [InlineData("2.1.0-DEV", null)]
    [InlineData("1.0.24", "1.0.24")]
    [InlineData("1.0.24+3f9c2a1", "1.0.24")]
    [InlineData("1.0.0", "1.0.0")] // no longer a sentinel: a release can be called 1.0.0
    [InlineData("2.0.0-beta.1", "2.0.0-beta.1")]
    public void The_version_is_what_the_build_announces_unless_it_is_a_dev_build(string? informational, string? expected) =>
        Assert.Equal(expected, MarkdownService.VersionOf(informational));

    /// <summary>The csproj sets 0.0.0-dev as the default; the release workflow overrides it with -p:Version.</summary>
    [Fact]
    public void A_build_from_this_checkout_has_no_version() => Assert.Null(MarkdownService.AppVersion);

    [Fact]
    public void The_bundled_whats_new_page_renders_with_no_placeholder_left()
    {
        var html = Markdown.RenderWhatsNew();

        Assert.NotNull(html);
        Assert.DoesNotContain("{{version}}", html, StringComparison.Ordinal);
        Assert.Contains(MarkdownService.AppVersion ?? "This build", html, StringComparison.Ordinal);
    }

    // ---- the bundled files and the command line ---------------------------------------------------

    [Fact]
    public void The_bundled_manual_is_found_and_recognised()
    {
        var manual = Markdown.GetUserManualPath();

        Assert.NotNull(manual);
        Assert.True(MarkdownService.IsUserManual(manual));
        Assert.False(MarkdownService.IsUserManual(_wiki.File("UserManual.md", "# not it")));
    }

    [Theory]
    [InlineData("a.md", true)]
    [InlineData("A.MD", true)]
    [InlineData("notes.markdown", true)]
    [InlineData("page.mdx", true)]
    [InlineData("readme.txt", true)]
    [InlineData("image.png", false)]
    [InlineData("script.js", false)]
    [InlineData("md", false)]
    [InlineData("", false)]
    [InlineData("notes.md.exe", false)]
    [InlineData("notes.exe.md", true)]
    [InlineData("backup.md.txt", true)]
    [InlineData("folder.md/file", false)]
    [InlineData("C:\\wiki\\Dune.MARKDOWN", true)]
    public void Only_markdown_like_files_are_supported(string path, bool expected) =>
        Assert.Equal(expected, MarkdownService.IsSupported(path));

    [Fact]
    public void A_folder_on_the_command_line_opens_its_entry_document()
    {
        _ = _wiki.File("zebra.md", "# z");
        _ = _wiki.File("with-readme/zebra.md", "# z");
        _ = _wiki.File("with-readme/README.md", "# r");
        _ = _wiki.File("no-entry/zeta.md", "# z");
        _ = _wiki.File("no-entry/Alpha.md", "# a");
        _ = _wiki.File("no-entry/picture.png", "x");

        // index.md first, then main, then readme, else the first document alphabetically.
        Assert.Equal(Path.Combine(_wiki.Path, "index.md"), MarkdownService.FindEntryFile(_wiki.Path), ignoreCase: true);
        Assert.Equal(Path.Combine(_wiki.Path, "with-readme", "README.md"), MarkdownService.FindEntryFile(Path.Combine(_wiki.Path, "with-readme")), ignoreCase: true);
        Assert.Equal(Path.Combine(_wiki.Path, "no-entry", "Alpha.md"), MarkdownService.FindEntryFile(Path.Combine(_wiki.Path, "no-entry")), ignoreCase: true);
        Assert.Null(MarkdownService.FindEntryFile(Path.Combine(_wiki.Path, "does-not-exist")));
    }

    [Fact]
    public void The_command_line_opens_the_first_readable_supported_argument_else_the_manual()
    {
        var chosen = _wiki.File("chosen.md", "# chosen");

        Assert.Equal(chosen, Markdown.GetStartupFilePath(["", "not-a-document.png", chosen]));
        Assert.Equal(Markdown.GetUserManualPath(), Markdown.GetStartupFilePath([]));
    }

    [Fact]
    public void A_missing_or_unreadable_document_renders_to_nothing()
    {
        Assert.Null(Markdown.Render(Path.Combine(_wiki.Path, "missing.md")));
        Assert.Null(Markdown.Render(""));
        Assert.Null(Markdown.Render(_wiki.Path));
    }

    [Fact]
    public void A_relative_argument_is_looked_for_in_the_working_directory_and_nowhere_else()
    {
        // Used to be searched for in every folder above the executable as well, so `MarkdownBlaze
        // readme.md` could open a readme.md from outside the folder the reader was standing in.
        using var elsewhere = new TempFolder();
        _ = elsewhere.File("deep/only-here.md", "# here");
        var previous = Directory.GetCurrentDirectory();
        try
        {
            // The test assembly's own folder is below the repository root, which holds samples/wiki/index.md.
            Directory.SetCurrentDirectory(elsewhere.Path);
            Assert.Equal(Markdown.GetUserManualPath(), Markdown.GetStartupFilePath(["samples/wiki/index.md"]));
            Assert.Equal(
                Path.Combine(elsewhere.Path, "deep", "only-here.md"),
                Markdown.GetStartupFilePath(["deep/only-here.md"]),
                ignoreCase: true);

            Directory.SetCurrentDirectory(TestFiles.RepoRoot);
            Assert.Equal(Path.Combine(TestFiles.RepoRoot, "samples", "wiki", "index.md"), Markdown.GetStartupFilePath(["samples/wiki/index.md"]), ignoreCase: true);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    // ---- reading a file: what became of it ----------------------------------------------------------

    [Fact]
    public void Rendering_a_file_says_what_became_of_it()
    {
        Assert.Equal(RenderStatus.Rendered, Markdown.RenderFile(Path.Combine(_wiki.Path, "index.md")).Status);
        Assert.Equal(RenderStatus.Missing, Markdown.RenderFile(Path.Combine(_wiki.Path, "missing.md")).Status);
        Assert.Equal(RenderStatus.Missing, Markdown.RenderFile(_wiki.Path).Status); // a folder is not a document
        Assert.Equal(RenderStatus.Missing, Markdown.RenderFile("").Status);
    }

    [Fact]
    public void A_file_over_the_size_cap_is_not_rendered_and_says_how_large_it_is()
    {
        var path = Path.Combine(_wiki.Path, "huge.md");
        using (var stream = File.Create(path))
            stream.SetLength(MarkdownService.MaxFileBytes + 1); // sparse: no 32 MB is written

        var outcome = Markdown.RenderFile(path);

        Assert.Equal(RenderStatus.TooLarge, outcome.Status);
        Assert.Null(outcome.Result);
        Assert.Equal(MarkdownService.MaxFileBytes + 1, outcome.Length);
        Assert.Null(Markdown.Render(path));
    }

    [Fact]
    public void A_file_the_editor_still_has_open_can_be_read()
    {
        var path = _wiki.File("locked.md", "# Locked by an editor");

        // Another program holds the file open for writing and would not let it be deleted: a read that
        // does not share (File.ReadAllText) fails on this, FileShare.ReadWrite|Delete does not.
        using var editor = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        var outcome = Markdown.RenderFile(path);

        Assert.Equal(RenderStatus.Rendered, outcome.Status);
        Assert.Equal("Locked by an editor", outcome.Result!.Title);
    }

    [Fact]
    public async Task A_render_that_is_cancelled_does_not_produce_a_result()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Markdown.RenderFileAsync(Path.Combine(_wiki.Path, "index.md"), cancelled.Token));
    }

    // ---- helpers ---------------------------------------------------------------------------------

    // ---- images: mdasset:// addresses (ARCH-5) ----------------------------------------------------------

    /// <summary>A 1x1 PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void A_local_image_gets_an_address_with_this_renders_token_and_a_grant_for_the_handler()
    {
        _ = _wiki.File("pic.png", Png);
        _ = _wiki.File("sub/deeper pic.png", Png);
        var doc = _wiki.File("page.md", "![a](pic.png) ![b](sub/deeper%20pic.png)");

        var result = Markdown.Render(doc)!;

        Assert.NotNull(result.Assets);
        Assert.Equal(_wiki.Path, result.Assets.BaseFolder);
        Assert.Equal(32, result.Assets.Token.Length);
        Assert.Contains(
            "src=\"mdasset://doc/" + result.Assets.Token + "/pic.png\"",
            result.BodyHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "src=\"mdasset://doc/" + result.Assets.Token + "/sub%2Fdeeper%20pic.png\"",
            result.BodyHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("base64", result.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_render_has_a_token_of_its_own()
    {
        _ = _wiki.File("pic.png", Png);
        var doc = _wiki.File("page.md", "![a](pic.png)");

        var first = Markdown.Render(doc)!;
        var second = Markdown.Render(doc)!;

        Assert.NotEqual(first.Assets!.Token, second.Assets!.Token, StringComparer.Ordinal);
        Assert.DoesNotContain(first.Assets.Token, second.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_without_a_displayable_image_has_no_grant()
    {
        _ = _wiki.File("notes.txt", "secret");
        var doc = _wiki.File("page.md", "![a](notes.txt) ![gone](gone.png) ![r](https://example.com/a.png)");

        var result = Markdown.Render(doc)!;

        Assert.Null(result.Assets);
        Assert.Contains("data-md-skipped=\"blocked\"", result.BodyHtml, StringComparison.Ordinal); // placeholder as before
        Assert.Contains("src=\"gone.png\"", result.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Placeholders_for_blocked_and_too_large_images_are_unchanged_next_to_a_good_one()
    {
        _ = _wiki.File("pic.png", Png);
        var big = _wiki.File("big.png", Png);
        using (var stream = new FileStream(big, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(LocalPath.MaxImageBytes + 1);
        }

        var doc = _wiki.File("page.md", "![ok](pic.png) ![huge](big.png) ![unc](//attacker.invalid/s/x.png) ![text](page.md)");

        var html = Markdown.Render(doc)!.BodyHtml;

        Assert.Equal(1, html.Split("src=\"mdasset://").Length - 1);
        Assert.Equal(1, html.Split("data-md-skipped=\"too-large\"").Length - 1);
        Assert.Equal(2, html.Split("data-md-skipped=\"blocked\"").Length - 1);
        Assert.Equal(3, html.Split("src=\"data:image/svg+xml;base64,").Length - 1);
        Assert.DoesNotContain("attacker.invalid", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_documents_image_budget_is_still_added_up_at_render_time()
    {
        // 20 MB each is the cap for one image; three of them are 60 MB and the fourth takes the document
        // past 64 MB. Sparse files: nothing is written, nothing is read.
        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            var path = _wiki.File(name + ".png", Png);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
            stream.SetLength(LocalPath.MaxImageBytes);
        }

        var html = Markdown.Render(_wiki.File("page.md", "![a](a.png) ![b](b.png) ![c](c.png) ![d](d.png)"))!.BodyHtml;

        Assert.Equal(3, html.Split("src=\"mdasset://").Length - 1);
        Assert.Equal(1, html.Split("data-md-skipped=\"too-large\"").Length - 1);
    }

    [Fact]
    public void Dropped_text_with_a_rooted_image_gets_an_address_and_a_grant_without_a_folder()
    {
        var rooted = _wiki.File("pic.png", Png).Replace('\\', '/');

        var result = Markdown.RenderText("dropped.md", $"![x]({rooted}) ![y](pic.png)");

        Assert.NotNull(result.Assets);
        Assert.Equal(string.Empty, result.Assets.BaseFolder);
        Assert.Equal(1, result.BodyHtml.Split("src=\"mdasset://").Length - 1);
        Assert.Contains("src=\"pic.png\"", result.BodyHtml, StringComparison.Ordinal); // relative: left alone, no folder
        Assert.DoesNotContain("base64,iVBOR", result.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_address_carries_a_parent_path_as_one_segment()
    {
        _ = _wiki.File("shared/pic.png", Png);
        var doc = _wiki.File("docs/page.md", "![a](../shared/pic.png)");

        var result = Markdown.Render(doc)!;

        // no ".." segment for the browser to fold into the token
        Assert.Contains("/..%2Fshared%2Fpic.png\"", result.BodyHtml, StringComparison.Ordinal);
    }

    private static TempFolder CreateWiki()
    {
        var wiki = new TempFolder();
        _ = wiki.File("index.md", "# Home");
        _ = wiki.File("my notes.md", "# My notes");
        _ = wiki.File("dune/index.md", "# Dune");
        _ = wiki.File("dune/houses.md", "# Houses\n\n## Atreides");
        _ = wiki.File("asimov/index.md", "# Asimov");
        return wiki;
    }

    private static void AssertHeading(Heading heading, int level, string text, string slug)
    {
        Assert.Equal(level, heading.Level);
        Assert.Equal(text, heading.Text);
        Assert.Equal(slug, heading.Slug);
    }

    /// <summary>A page of the sample wiki (samples/wiki).</summary>
    /// <exception cref="FileNotFoundException">The page is not in the sample wiki.</exception>
    private static string FindSamplePage(string name)
    {
        var candidate = Path.Combine(TestFiles.RepoRoot, "samples", "wiki", name);
        return File.Exists(candidate) ? candidate : throw new FileNotFoundException("Sample page not found: " + name);
    }

    [GeneratedRegex("data-nav=\"(?<target>[^\"]+)\"", RegexOptions.None, 1000)]
    private static partial Regex NavTarget { get; }
}
