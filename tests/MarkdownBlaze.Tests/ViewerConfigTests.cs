using System.Reflection;
using System.Text.Json;
using MarkdownBlaze.Interop;
using MarkdownBlaze.Security;
using MarkdownBlaze.Services;
using Microsoft.JSInterop;

namespace MarkdownBlaze.Tests;

/// <summary>
/// ARCH-2 / SEC-7: the values that used to be written twice, in C# and in JavaScript, are written once
/// and handed to the page when it starts. These tests are what keeps the page from growing a copy again.
/// </summary>
public class ViewerConfigTests
{
    private static readonly JsonSerializerOptions JsInterop = new(JsonSerializerDefaults.Web);

    private const BindingFlags Everything =
        BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void The_config_carries_the_lists_the_dotnet_side_applies()
    {
        var config = ViewerConfig.Create(["f5"]);

        Assert.Equal(
            ExternalLinks.AllowedSchemes.Order(StringComparer.Ordinal),
            config.LinkSchemes.Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Assert.Equal(MarkdownService.SupportedExtensions, config.Extensions, StringComparer.Ordinal);
        Assert.Equal(["f5"], config.Shortcuts, StringComparer.Ordinal);
        Assert.Equal(ZoomLimits.Default, config.Zoom);
    }

    /// <summary>The names the modules read (<c>config().zoom.min</c>, <c>linkSchemes</c>, ...).</summary>
    [Fact]
    public void The_config_reaches_the_page_under_the_names_the_modules_read()
    {
        var json = JsonSerializer.Serialize(ViewerConfig.Create(["alt+left"]), JsInterop);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(0.4, root.GetProperty("zoom").GetProperty("min").GetDouble());
        Assert.Equal(4, root.GetProperty("zoom").GetProperty("max").GetDouble());
        Assert.Equal(1.1, root.GetProperty("zoom").GetProperty("step").GetDouble());
        Assert.Equal(210, root.GetProperty("page").GetProperty("widthMm").GetDouble());
        Assert.Equal(297, root.GetProperty("page").GetProperty("heightMm").GetDouble());
        Assert.Equal(15, root.GetProperty("page").GetProperty("marginMm").GetDouble());
        Assert.Equal(16, root.GetProperty("page").GetProperty("gapPx").GetDouble());
        Assert.Contains(".md", root.GetProperty("extensions").EnumerateArray().Select(e => e.GetString()), StringComparer.Ordinal);
        Assert.Contains("https", root.GetProperty("linkSchemes").EnumerateArray().Select(e => e.GetString()), StringComparer.Ordinal);
        Assert.Equal("alt+left", root.GetProperty("shortcuts")[0].GetString());
    }

    /// <summary>
    /// The scripts have no copy of what the config carries: no list of schemes, extensions or zoom limits.
    /// (The old test compared a copy in app.js with the C# list; now there is nothing to compare.)
    /// </summary>
    [Fact]
    public void The_scripts_do_not_repeat_what_the_config_carries()
    {
        var scripts = Directory.EnumerateFiles(Path.Combine(TestFiles.RepoRoot, "src", "MarkdownBlaze", "wwwroot", "js"), "*.js").ToList();
        Assert.NotEmpty(scripts);
        Assert.DoesNotContain(scripts, f => string.Equals(Path.GetFileName(f), "app.js", StringComparison.Ordinal));

        foreach (var script in scripts)
        {
            var text = File.ReadAllText(script);
            foreach (var scheme in ExternalLinks.AllowedSchemes.Where(s => s is not ("http" or "https")))
            {
                Assert.DoesNotContain($"'{scheme}'", text, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("'.markdown'", text, StringComparison.Ordinal);
            Assert.DoesNotContain("MD_ZOOM", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DotNet.invokeMethodAsync", text, StringComparison.Ordinal);
            Assert.DoesNotContain("window.md", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// SEC-7: everything the page may call is an instance method of an object it was handed a reference to; the assembly
    /// exposes no static <c>[JSInvokable]</c> at all.
    /// </summary>
    [Fact]
    public void No_javascript_invokable_method_is_static()
    {
        var invokable = typeof(ViewerInterop).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(Everything))
            .Where(m => m.IsDefined(typeof(JSInvokableAttribute), inherit: false))
            .ToList();

        Assert.NotEmpty(invokable);
        Assert.All(invokable, m => Assert.False(m.IsStatic, m.DeclaringType!.Name + "." + m.Name));
        // The page's own connection, and the focus scope of a popup (its own reference, for a menu to close itself).
        Assert.All(invokable, m => Assert.True(m.DeclaringType == typeof(ViewerInterop) || m.DeclaringType == typeof(Components.Shared.FocusScope), m.Name));
    }
}
