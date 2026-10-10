namespace MarkdownBlaze.Tests;

internal static class TestFiles
{
    /// <summary>The repository root: the folder that holds <c>src/MarkdownBlaze/MarkdownBlaze.csproj</c>.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "MarkdownBlaze", "MarkdownBlaze.csproj")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Cannot find the repository root from " + AppContext.BaseDirectory);
    }

    /// <summary>The files of a fixture folder (copied next to the test assembly).</summary>
    public static IEnumerable<string> Fixtures(string folder, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", folder), pattern)
            .Order(StringComparer.Ordinal);

    /// <summary>The file names of a fixture folder, as theory data.</summary>
    public static TheoryData<string> FixtureNames(string folder, string pattern)
    {
        var data = new TheoryData<string>();
        foreach (var fixture in Fixtures(folder, pattern))
            data.Add(Path.GetFileName(fixture));

        return data;
    }

    public static string Fixture(string folder, string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", folder, name));
}
