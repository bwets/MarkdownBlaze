namespace MarkdownBlaze.Services;

/// <summary>
/// Watches a single file and raises a debounced <see cref="Changed"/> event on edits — including its
/// deletion or replacement, which is what an editor that saves by writing a temporary file and renaming
/// it does. The event comes from a timer thread, not the UI thread; whoever handles it has to hand the
/// work over (<see cref="NavigationService"/> does, through its <see cref="IUiDispatcher"/>).
/// </summary>
internal sealed class FileWatcher : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(200);
    private FileSystemWatcher? _watcher;

    /// <summary>
    /// One timer for the lifetime of the watcher, re-armed with Change(): disposing and re-creating it on
    /// every event, as this used to, could dispose a timer from one thread while it was firing on another.
    /// </summary>
    private readonly Timer _debounce;

    public FileWatcher()
    {
        _debounce = new Timer(_ => RaiseChanged(), state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler? Changed;

    public void Watch(string path)
    {
        _watcher?.Dispose();
        _watcher = null;

        var dir = Path.GetDirectoryName(path);
        if (dir is null || !Directory.Exists(dir)) return;

        var watcher = new FileSystemWatcher(dir, Path.GetFileName(path)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
        watcher.Changed += (_, _) => Debounce();
        watcher.Created += (_, _) => Debounce();
        // A real deletion — and the "delete the old file, then put the new one in place" half of an
        // atomic save. Both end in a re-read, which is where the difference is made.
        watcher.Deleted += (_, _) => Debounce();
        watcher.Renamed += (_, _) => Debounce();
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    /// <summary>Raises <see cref="Changed"/> now, from the calling thread (the tests drive the watcher with it).</summary>
    internal void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void Debounce()
    {
        try
        {
            _ = _debounce.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // A file-system event that was already on its way when the watcher was disposed: nothing is
            // listening any more, so there is nobody to tell.
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
    }
}
