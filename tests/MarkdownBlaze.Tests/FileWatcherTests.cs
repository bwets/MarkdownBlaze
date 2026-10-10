using System.Globalization;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>The watcher of the open file: edits, replacements and deletions each end in one debounced event.</summary>
public sealed class FileWatcherTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void An_edit_is_reported_once_after_the_debounce()
    {
        var path = _folder.File("doc.md", "# one");
        using var watcher = new FileWatcher();
        var count = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref count);
        watcher.Watch(path);

        for (var i = 0; i < 5; i++) File.WriteAllText(path, "# edit " + i.ToString(CultureInfo.InvariantCulture));
        _ = SpinWait.SpinUntil(() => Volatile.Read(ref count) > 0, TimeSpan.FromSeconds(10));
        Thread.Sleep(600);

        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public void A_deletion_is_reported()
    {
        var path = _folder.File("doc.md", "# one");
        using var watcher = new FileWatcher();
        using var reported = new ManualResetEventSlim();
        watcher.Changed += (_, _) => reported.Set();
        watcher.Watch(path);

        File.Delete(path);

        Assert.True(reported.Wait(TimeSpan.FromSeconds(10)), "the deletion was not reported");
    }

    [Fact]
    public void A_save_by_replacing_the_file_is_reported()
    {
        var path = _folder.File("doc.md", "# one");
        var temp = _folder.File("doc.md.tmp", "# two");
        using var watcher = new FileWatcher();
        using var reported = new ManualResetEventSlim();
        watcher.Changed += (_, _) => reported.Set();
        watcher.Watch(path);

        File.Move(temp, path, overwrite: true);

        Assert.True(reported.Wait(TimeSpan.FromSeconds(10)), "the replacement was not reported");
    }

    [Fact]
    public void Nothing_is_reported_after_the_watcher_is_disposed()
    {
        var path = _folder.File("doc.md", "# one");
        var watcher = new FileWatcher();
        var count = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref count);
        watcher.Watch(path);
        File.WriteAllText(path, "# two");

        watcher.Dispose();
        Thread.Sleep(500);

        Assert.Equal(0, Volatile.Read(ref count));
    }
}
