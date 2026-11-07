using MarkdownBlaze.Platform;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>The two sets of host addresses the tests render with.</summary>
internal static class TestAddresses
{
    /// <summary>The desktop window's own: <c>markdown://document/</c> and <c>mdasset://doc/</c>.</summary>
    public static HostAddresses Desktop => DesktopAddresses.Addresses;

    /// <summary>Android's <c>BlazorWebView</c>: one https origin, pictures under a path of it.</summary>
    public static readonly HostAddresses Android = new(new Uri("https://0.0.0.1/"), "https://0.0.0.1/_mdasset/");
}
