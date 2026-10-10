using MarkdownBlaze.Models;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>The document themes and the layout enums: looked up by id or by name, never by position.</summary>
public class ContentThemesTests
{
    [Fact]
    public void Every_theme_has_an_id_of_its_own_and_the_one_that_follows_the_app_is_findable_by_it()
    {
        Assert.Equal(ContentThemes.All.Count, ContentThemes.All.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ContentThemes.Auto, ContentThemes.Resolve(ContentThemes.Auto).Id);
        Assert.Contains(ContentThemes.All, t => string.Equals(t.Id, ContentThemes.Auto, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("night")]
    [InlineData("book")]
    [InlineData("typewriter")]
    public void A_known_id_is_its_theme(string id) => Assert.Equal(id, ContentThemes.Resolve(id).Id);

    [Theory]
    [InlineData("")]
    [InlineData("Night")]
    [InlineData("a-theme-from-the-future")]
    [InlineData(null)]
    public void An_unknown_id_follows_the_app(string? id) => Assert.Equal(ContentThemes.Auto, ContentThemes.Resolve(id).Id);

    [Fact]
    public void The_place_of_a_theme_in_the_list_is_found_by_its_id()
    {
        for (var i = 0; i < ContentThemes.All.Count; i++)
            Assert.Equal(i, ContentThemes.IndexOf(ContentThemes.All[i].Id));

        Assert.Equal(-1, ContentThemes.IndexOf("nothing"));
        Assert.Equal(-1, ContentThemes.IndexOf(null));
    }

    [Theory]
    [InlineData("continuous", "Continuous")]
    [InlineData("page", "Page")]
    [InlineData("PAGE", "Page")]
    [InlineData("Page", "Page")]
    [InlineData("pages", "Continuous")]
    [InlineData("1", "Continuous")]
    [InlineData("", "Continuous")]
    [InlineData(null, "Continuous")]
    // The enums are internal, so a theory cannot take them as parameters: the member is named instead.
    public void A_view_mode_is_read_by_name(string? text, string expected) => Assert.Equal(Enum.Parse<ViewMode>(expected), ViewModes.ResolveMode(text));

    [Theory]
    [InlineData("width", "Width")]
    [InlineData("page", "Page")]
    [InlineData("Page", "Page")]
    [InlineData("fit", "Width")]
    [InlineData("1", "Width")]
    [InlineData(null, "Width")]
    public void A_page_fit_is_read_by_name(string? text, string expected) => Assert.Equal(Enum.Parse<PageFit>(expected), ViewModes.ResolveFit(text));

    /// <summary>The page's script is handed these names, and they are what the preferences file has always held.</summary>
    [Fact]
    public void The_names_the_page_knows_are_lower_case()
    {
        Assert.Equal("continuous", ViewModes.Name(ViewMode.Continuous));
        Assert.Equal("page", ViewModes.Name(ViewMode.Page));
        Assert.Equal("width", ViewModes.Name(PageFit.Width));
        Assert.Equal("page", ViewModes.Name(PageFit.Page));
    }

    [Fact]
    public void Every_name_reads_back_as_the_member_it_came_from()
    {
        foreach (var mode in Enum.GetValues<ViewMode>())
            Assert.Equal(mode, ViewModes.ResolveMode(ViewModes.Name(mode)));

        foreach (var fit in Enum.GetValues<PageFit>())
            Assert.Equal(fit, ViewModes.ResolveFit(ViewModes.Name(fit)));
    }
}
