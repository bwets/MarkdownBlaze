using MarkdownBlaze.Security;

namespace MarkdownBlaze.Tests;

/// <summary>SEC-2: only a short list of schemes may be handed to the operating system.</summary>
public class ExternalLinksTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/search?q=markdown#results")]
    [InlineData("HTTPS://EXAMPLE.COM")]
    [InlineData("  https://example.com  ")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("mailto:someone@example.com?subject=MarkdownBlaze&body=Hello")]
    [InlineData("tel:+3212345678")]
    [InlineData("ftp://ftp.gnu.org/gnu/")]
    public void Allowed_links_are_allowed(string url) => Assert.True(ExternalLinks.IsAllowed(url));

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("FILE:///C:/x.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("search-ms:query=x&crumb=location:\\\\attacker\\share")]
    [InlineData("ms-officecmd:{}")]
    [InlineData("ms-windows-store://review/?PFN=x")]
    [InlineData("markdown://document/C:/x.md")]
    [InlineData("C:/x.exe")]
    [InlineData("C:\\x.exe")]
    [InlineData("\\\\server\\share\\x.lnk")]
    [InlineData("\\\\server\\x")]
    [InlineData("//server/share/x.lnk")]
    [InlineData("//example.com")]
    [InlineData("/etc/hosts")]
    [InlineData("./diagram.png")]
    [InlineData("example.com")]
    [InlineData("ht\ttps://example.com")]
    [InlineData("https")]
    [InlineData("https:")]
    [InlineData(":")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Everything_else_is_refused(string? url) => Assert.False(ExternalLinks.IsAllowed(url));

    [Fact]
    public void Allowed_links_are_handed_over_in_their_normalised_form()
    {
        Assert.True(ExternalLinks.TryGetOpenable("  HTTPS://Example.com/a b  ", out var uri));
        Assert.Equal("https://example.com/a%20b", uri.AbsoluteUri);
    }
}
