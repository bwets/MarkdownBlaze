namespace MarkdownBlaze.Services;

/// <summary>
/// Turns a burst of "please save" requests into one write: the write happens a moment after the last
/// request, or at once on <see cref="Flush"/>. Zooming with the wheel changes a preference on every
/// tick; without this each tick rewrote the file.
/// <para>
/// Whatever is still waiting when the app closes must not be lost, so the owner calls
/// <see cref="Flush"/> when the window closes, and <see cref="Dispose"/> flushes once more.
/// </para>
/// </summary>
internal sealed class SaveDebouncer : IDisposable
{
    /// <summary>How long after the last request the write happens.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(500);
    private readonly Action _write;
    private readonly TimeSpan _delay;
    private readonly Timer _timer;
    private readonly Lock _lock = new();
    private bool _dirty;
    private bool _disposed;

    public SaveDebouncer(Action write, TimeSpan delay)
    {
        _write = write;
        _delay = delay;
        _timer = new Timer(_ => Flush(), state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Asks for a write; a request that comes in before the previous one has been served replaces it.</summary>
    public void Request()
    {
        lock (_lock)
        {
            _dirty = true;
            // After disposal there is no timer to wait for: write now, so a late request is not lost.
            if (!_disposed)
            {
                _ = _timer.Change(_delay, Timeout.InfiniteTimeSpan);
                return;
            }
        }

        Flush();
    }

    /// <summary>Writes now if a write is waiting; does nothing otherwise.</summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (!_dirty) return;
            _dirty = false;
            if (!_disposed) _ = _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _write();
        }
    }

    public void Dispose()
    {
        Flush();
        lock (_lock)
        {
            _disposed = true;
            _timer.Dispose();
        }
    }
}
