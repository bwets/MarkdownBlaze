namespace MarkdownBlaze.Tests;

/// <summary>A scratch folder that is removed when the test is done with it.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "MarkdownBlazeTests-" + Guid.NewGuid().ToString("N"));

    public TempFolder()
    {
        _ = Directory.CreateDirectory(Path);
    }

    public string File(string name, string content)
    {
        var path = System.IO.Path.Combine(Path, name);
        _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public string File(string name, byte[] content)
    {
        var path = System.IO.Path.Combine(Path, name);
        _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // a temp folder left behind is not a failed test
        }
    }
}
