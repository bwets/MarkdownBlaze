using MarkdownBlaze.Platform.Windows;

namespace MarkdownBlaze.Tests;

/// <summary>PKG-2: the app can tell it runs from an MSIX package, and keeps its hands off the registry if so.</summary>
public sealed class PackageIdentityTests
{
    [Fact]
    public void A_process_that_was_started_from_a_folder_has_no_package() =>
        // The test host is an ordinary unpackaged process, on every platform.
        Assert.False(PackageIdentity.IsPackaged);

    [Theory]
    [InlineData(PackageIdentity.AppModelErrorNoPackage, false)] // APPMODEL_ERROR_NO_PACKAGE
    [InlineData(PackageIdentity.ErrorInsufficientBuffer, true)] // a packaged process asked with no buffer
    [InlineData(0, true)]                                       // ERROR_SUCCESS
    [InlineData(5, false)]                                      // anything else (access denied...) is not a package
    public void The_answer_of_the_api_is_read_as_packaged_or_not(int result, bool packaged) =>
        Assert.Equal(packaged, PackageIdentity.IsPackagedResult(result));
}
