using MarkdownBlaze.Platform;

namespace MarkdownBlaze.Tests;

/// <summary>
/// A shell that starts nothing and writes down what it was asked to: a test hands this to the code under
/// test and then says what should — and, as important, what should not — have got as far as the shell.
/// </summary>
internal sealed class FakeShellLauncher : IShellLauncher
{
    private readonly Lock _lock = new();

    /// <summary>The addresses that were handed to <see cref="OpenExternal"/>, as the shell would have received them.</summary>
    public List<Uri> Opened { get; } = [];

    /// <summary>The documents <see cref="OpenInNewWindow"/> was asked to open.</summary>
    public List<string> NewWindows { get; } = [];

    /// <summary>The files <see cref="RevealInFileManager"/> was asked to show.</summary>
    public List<string> Revealed { get; } = [];

    /// <summary>The addresses <see cref="OpenStoreReview"/> was asked to open.</summary>
    public List<Uri> StoreReviews { get; } = [];

    public bool OpenExternal(Uri uri)
    {
        lock (_lock) Opened.Add(uri);
        return true;
    }

    public void OpenInNewWindow(string path)
    {
        lock (_lock) NewWindows.Add(path);
    }

    public void RevealInFileManager(string path)
    {
        lock (_lock) Revealed.Add(path);
    }

    public bool OpenStoreReview(Uri review)
    {
        lock (_lock) StoreReviews.Add(review);
        return true;
    }
}
