using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>SEC-8: a document path must survive the trip through a markdown:// address.</summary>
public class UriSchemeTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\notes" : "/home/user/notes";

    /// <summary>
    /// File names that have gone wrong, or could: separators of the URL grammar, escapes, spaces, and
    /// anything outside ASCII.
    /// </summary>
    public static TheoryData<string> AwkwardNames() =>
    [
            "Dune.md",
            "a#b.md",
            "#hash-first.md",
            "100% done.md",
            "%41.md",
            "%2F.md",
            "%23.md",
            "50%.md",
            "a b.md",
            "  leading and trailing  .md",
            "a+b.md",
            "a&b=c.md",
            "a;b,c.md",
            "it's.md",
            "(parens) [brackets] {braces}.md",
            "été français.md",
            "日本語のノート.md",
            "emoji 🎉.md",
            "100%25.md",
            "a%.md",
            "ünïcödé#%41.md",
    ];

    [Theory]
    [MemberData(nameof(AwkwardNames))]
    public void A_path_survives_the_round_trip_through_its_address(string name)
    {
        var path = Path.Combine(Root, "sub folder", name);

        var address = UriScheme.AppBase.AbsoluteUri.TrimEnd('/') + "/" + UriScheme.FragmentForFile(path);

        Assert.Equal(path, UriScheme.TryGetFilePath(address));
    }

    [Theory]
    [MemberData(nameof(AwkwardNames))]
    public void A_path_survives_the_round_trip_through_the_browsers_own_normalisation(string name)
    {
        // The WebView hands back the address after its own URL parser has had a go at the fragment.
        var path = Path.Combine(Root, name);
        var typed = UriScheme.AppBase.AbsoluteUri + UriScheme.FragmentForFile(path);

        var normalised = new Uri(typed).AbsoluteUri;

        Assert.Equal(path, UriScheme.TryGetFilePath(normalised));
    }

    [Fact]
    public void A_hash_and_a_percent_are_escaped_in_the_fragment()
    {
        var fragment = UriScheme.FragmentForFile(Path.Combine(Root, "a#b%41.md"));

        Assert.Contains("a%23b%2541.md", fragment, StringComparison.Ordinal);
        Assert.StartsWith("#", fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("#", fragment[1..], StringComparison.Ordinal);
    }

    [Fact]
    public void The_separators_and_the_drive_colon_stay_readable()
    {
        Assert.Equal("#C:/notes/Dune.md", UriScheme.FragmentForFile(@"C:\notes\Dune.md"));
        Assert.Equal("#/home/user/Dune.md", UriScheme.FragmentForFile("/home/user/Dune.md"));
        Assert.Equal("#", UriScheme.FragmentForFile(path: null));
        Assert.Equal("#", UriScheme.FragmentForFile(""));
    }

    [SkippableFact]
    public void The_windows_forms_another_application_is_likely_to_produce_are_read()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        Assert.Equal(@"C:\notes\Dune.md", UriScheme.TryGetFilePath("markdown://document/C:/notes/Dune.md"));
        Assert.Equal(@"C:\notes\Dune.md", UriScheme.TryGetFilePath("markdown://document/#C:/notes/Dune.md"));
        Assert.Equal(@"C:\notes\Dune.md", UriScheme.TryGetFilePath("\"markdown://document/C:/notes/Dune.md\""));
        Assert.Equal(@"C:\notes\my file.md", UriScheme.TryGetFilePath("markdown://document/C:/notes/my%20file.md"));
        Assert.Equal(@"C:\notes\a#b.md", UriScheme.TryGetFilePath("markdown://document/#C:/notes/a%23b.md"));
        Assert.Equal(@"C:\notes\%41.md", UriScheme.TryGetFilePath("markdown://document/#C:/notes/%2541.md"));
    }

    [Theory]
    [InlineData("markdown://C:/notes/Dune.md")] // the shorthand: the drive lands in the host
    [InlineData("MARKDOWN://document/notes/Dune.md")]
    public void The_shorthand_and_case_variants_give_a_path(string address)
    {
        var path = UriScheme.TryGetFilePath(address);

        Assert.NotNull(path);
        Assert.EndsWith("Dune.md", path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\notes\\Dune.md")]
    [InlineData("https://example.com/Dune.md")]
    [InlineData("file:///C:/notes/Dune.md")]
    [InlineData("markdown://document/")]
    [InlineData("markdown://document/#")]
    public void Anything_that_is_not_a_document_address_is_not_one(string? argument) =>
        Assert.Null(UriScheme.TryGetFilePath(argument));

    [Theory]
    [InlineData("markdown://document/#//server/share/x.md")]
    [InlineData("markdown://document/#%5C%5Cserver%5Cshare%5Cx.md")]
    [InlineData("markdown://document/#%2F%2Fserver/share/x.md")]
    [InlineData("markdown://document/%5C%5Cserver%5Cshare%5Cx.md")]
    [InlineData("markdown://document//server/share/x.md")]
    [InlineData("markdown://document/#/%5C%5Cserver%5Cshare%5Cx.md")]
    [InlineData("markdown://document/#%5C%5C%3F%5CC%3A%5Cx.md")]
    [InlineData("markdown://document/#%5C%5C.%5Cpipe%5Cx.md")]
    public void Unc_and_device_paths_are_refused(string address) =>
        Assert.Null(UriScheme.TryGetFilePath(address));

    [Fact]
    public void A_unc_path_does_not_survive_the_trip_either()
    {
        // The window never produces one itself; this guards the reverse direction.
        var fragment = UriScheme.FragmentForFile(@"\\server\share\x.md");

        Assert.Null(UriScheme.TryGetFilePath("markdown://document/" + fragment));
    }
}
