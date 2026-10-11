using MarkdownBlaze.Models;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>
/// A renderer that takes as long as the test says. Files named by <see cref="Blocks"/> do not return until
/// <see cref="Release"/> — and do not look at their cancellation token while they wait, as a call into a
/// library that is stuck in a quadratic parse would not — so a test can hold a render open past the
/// deadline, and let it finish afterwards to see that nothing it makes reaches the page.
/// </summary>
internal sealed class FakeDocumentRenderer : IDocumentRenderer, IDisposable
{
    private readonly ManualResetEventSlim _gate = new(initialState: false);
    private int _calls;

    /// <summary>Which files block. Everything else renders at once.</summary>
    public Func<string, bool> Blocks { get; set; } = _ => false;

    /// <summary>How many times <see cref="RenderFile"/> was called.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Lets every blocked render go.</summary>
    public void Release() => _gate.Set();
    public string? GetStartupFilePath(IEnumerable<string> args) => null;

    public FileRender RenderFile(string markdownFilePath, CancellationToken cancellationToken = default)
    {
        _ = Interlocked.Increment(ref _calls);
        if (Blocks(markdownFilePath)) _gate.Wait(CancellationToken.None); // deliberately not the token

        return new FileRender(RenderStatus.Rendered, Result(markdownFilePath));
    }

    public RenderResult RenderText(string fileName, string markdown) => Result(fileName);

    public void Dispose()
    {
        _gate.Set();
        _gate.Dispose();
    }

    private static RenderResult Result(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return new RenderResult(path, Path.GetFileName(path), "Title of " + name, "<p>Rendered " + name + "</p>", []);
    }
}
