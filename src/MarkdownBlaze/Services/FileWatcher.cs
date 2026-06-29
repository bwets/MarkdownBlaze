namespace MarkdownBlaze.Services;

/// <summary>Watches a single file and raises a debounced <see cref="Changed"/> event on edits.</summary>
public sealed class FileWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    public event Action? Changed;

    public void Watch(string path)
    {
        _watcher?.Dispose();
        _watcher = null;

        var dir = Path.GetDirectoryName(path);
        if (dir is null || !Directory.Exists(dir)) return;

        var watcher = new FileSystemWatcher(dir, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        void OnChanged(object? s, FileSystemEventArgs e) => Debounce();
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Renamed += (_, _) => Debounce();
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    private void Debounce()
    {
        _debounce?.Dispose();
        _debounce = new Timer(_ => Changed?.Invoke(), null, 200, Timeout.Infinite);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
