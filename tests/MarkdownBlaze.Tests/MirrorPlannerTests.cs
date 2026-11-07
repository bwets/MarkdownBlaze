using MarkdownBlaze.Services.Storage;

namespace MarkdownBlaze.Tests;

/// <summary>What a sync has to do: the listing of the provider compared with what the mirror holds.</summary>
public sealed class MirrorPlannerTests
{
    private static RemoteEntry Remote(string path, long size = 10, long modified = 1000) => new(path, size, modified, "id:" + path);
    private static LocalEntry Local(string path, long size = 10, long modified = 1000) => new(path, size, modified);

    [Fact]
    public void A_file_the_mirror_does_not_have_is_copied()
    {
        var plan = MirrorPlanner.Plan([Remote("a.md")], [], complete: true);

        Assert.Equal(["a.md"], plan.Copy.Select(e => e.Path), StringComparer.Ordinal);
        Assert.Empty(plan.Delete);
    }

    [Fact]
    public void A_file_that_is_the_same_size_and_age_is_left_alone()
    {
        var plan = MirrorPlanner.Plan([Remote("a.md")], [Local("a.md")], complete: true);

        Assert.Empty(plan.Copy);
        Assert.Empty(plan.Delete);
    }

    [Theory]
    [InlineData(11, 1000)]
    [InlineData(10, 1001)]
    [InlineData(10, 999)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    public void A_file_whose_size_or_time_differs_is_copied_and_one_whose_time_is_unknown_always_is(long size, long modified)
    {
        var plan = MirrorPlanner.Plan([Remote("a.md", size, modified)], [Local("a.md")], complete: true);

        _ = Assert.Single(plan.Copy);
    }

    [Fact]
    public void A_mirrored_file_the_folder_no_longer_has_is_deleted()
    {
        var plan = MirrorPlanner.Plan([Remote("a.md")], [Local("a.md"), Local("gone.md"), Local("sub/gone.png")], complete: true);

        Assert.Equal(["gone.md", "sub/gone.png"], plan.Delete, StringComparer.Ordinal);
    }

    [Fact]
    public void Nothing_is_deleted_when_the_listing_did_not_cover_the_folder()
    {
        var plan = MirrorPlanner.Plan([Remote("a.md")], [Local("a.md"), Local("deep/b.md")], complete: false);

        Assert.Empty(plan.Delete);
    }

    [Fact]
    public void The_document_being_opened_is_copied_first_then_the_files_nearest_the_top()
    {
        var plan = MirrorPlanner.Plan(
            [Remote("z.md"), Remote("a/b/c.md"), Remote("a/x.md"), Remote("a/target.md"), Remote("b.md")],
            [],
            complete: true,
            first: "a/target.md");

        Assert.Equal(["a/target.md", "b.md", "z.md", "a/x.md", "a/b/c.md"], plan.Copy.Select(e => e.Path), StringComparer.Ordinal);
    }

    [Fact]
    public void Two_files_listed_under_one_name_are_copied_once()
    {
        var plan = MirrorPlanner.Plan([Remote("a.md"), Remote("a.md", size: 99)], [], complete: true);

        _ = Assert.Single(plan.Copy);
    }

    [Fact]
    public void Names_are_compared_exactly_because_the_mirror_is_case_sensitive()
    {
        var plan = MirrorPlanner.Plan([Remote("A.md")], [Local("a.md")], complete: true);

        Assert.Equal(["A.md"], plan.Copy.Select(e => e.Path), StringComparer.Ordinal);
        Assert.Equal(["a.md"], plan.Delete, StringComparer.Ordinal);
    }
}
