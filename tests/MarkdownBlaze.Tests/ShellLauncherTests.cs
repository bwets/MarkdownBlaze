using MarkdownBlaze.Platform;

namespace MarkdownBlaze.Tests;

/// <summary>
/// ARCH-3: the shell seam. The real launcher is only ever asked about addresses that must be refused here —
/// a test that started something would open a browser or a program on the machine that runs it.
/// </summary>
public class ShellLauncherTests
{
    private readonly ShellLauncher _launcher = new();

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("search-ms:query=x")]
    [InlineData("data:text/html,x")]
    [InlineData("markdown://document/C:/x.md")]
    [InlineData("ms-windows-store://review/?PFN=x")] // the Store has its own method, and no link may reach it
    public void The_launcher_refuses_an_address_that_is_not_on_the_allow_list_even_when_asked_directly(string address) =>
        Assert.False(_launcher.OpenExternal(new Uri(address)));

    [Fact]
    public void A_relative_address_is_refused()
    {
        Assert.False(_launcher.OpenExternal(new Uri("docs/readme.md", UriKind.Relative)));
        Assert.False(_launcher.OpenStoreReview(new Uri("docs/readme.md", UriKind.Relative)));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("mailto:someone@example.com")]
    public void The_store_method_opens_nothing_but_the_store(string address) =>
        Assert.False(_launcher.OpenStoreReview(new Uri(address)));

    [Fact]
    public void Revealing_a_file_that_is_not_there_starts_nothing() =>
        // Does not throw, and does not start Explorer for a path that does not exist.
        _launcher.RevealInFileManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "nothing.md"));
}
