using MarkdownBlaze.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarkdownBlaze.Services;

/// <summary>
/// Persists the global list of opened files to the local app-data folder.
/// <para>
/// Like <see cref="PreferencesStore"/> it writes a moment after a change rather than at once, and writes
/// the file atomically; <see cref="Flush"/> (window closing) and <see cref="Dispose"/> write what is waiting.
/// </para>
/// </summary>
internal sealed class HistoryStore : IDisposable
{
    private const int MaxEntries = 200;

    private readonly string _filePath;
    private readonly List<HistoryEntry> _entries;
    private readonly ILogger _log;
    private readonly SaveDebouncer _saver;

    /// <summary>Guards <c>_entries</c> (a background prune can touch it).</summary>
    private readonly Lock _lock = new();

    /// <summary>
    /// The list as the readers see it: replaced, never changed, each time <c>_entries</c> changes — so
    /// reading it needs no lock and no copy, however often a render asks.
    /// </summary>
    private volatile IReadOnlyList<HistoryEntry> _snapshot;

    /// <summary>Raised (possibly from a background thread) when the entry list changes out-of-band.</summary>
    public event EventHandler? Changed;

    public HistoryStore(ILogger<HistoryStore> log)
        : this(AppPaths.DataFolder, log)
    { }

    /// <summary>The history lives in <paramref name="directory"/> — the tests point it at a scratch folder.</summary>
    internal HistoryStore(string directory, ILogger? log = null, TimeSpan? debounce = null)
    {
        _log = log ?? NullLogger.Instance;
        _ = Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "history.json");
        _saver = new SaveDebouncer(Write, debounce ?? SaveDebouncer.DefaultDelay);
        _entries = JsonFile.Load<List<HistoryEntry>>(_filePath, _log) ?? [];
        _snapshot = [.. _entries];
    }

    /// <summary>
    /// Opened files, most-recent first. An immutable snapshot, replaced whenever the history changes:
    /// callers can enumerate it safely and cheaply, and it never changes underneath them.
    /// </summary>
    public IReadOnlyList<HistoryEntry> Entries => _snapshot;

    /// <summary>How many times the file has actually been written (the tests count them).</summary>
    internal int WriteCount { get; private set; }

    public void Record(string path, string title)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var full = Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(title))
            title = Path.GetFileNameWithoutExtension(full);

        lock (_lock)
        {
            // A file already in the list keeps its place. The history is a record of what you have
            // opened, and a list that reshuffles itself every time you click a row is one you cannot
            // navigate: the entry you just used jumps away from where you found it.
            var existing = _entries.FindIndex(e => string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                _entries[existing] = _entries[existing] with { Title = title, LastOpenedUtc = DateTimeOffset.UtcNow };
            else
                _entries.Insert(0, new HistoryEntry(full, title, DateTimeOffset.UtcNow));

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);

            Publish();
        }
    }

    /// <summary>
    /// Drops history entries whose file no longer exists, on a background thread so the (potentially
    /// slow, e.g. network paths) existence checks never delay startup. Raises <see cref="Changed"/> if
    /// anything was removed.
    /// </summary>
    public void PruneMissingInBackground()
    {
        // File.Exists answers false for anything it cannot reach instead of throwing, so there is no
        // failure to catch here.
        var prune = Task.Run(() =>
        {
            var snapshot = _snapshot;
            var missing = new HashSet<string>(
                snapshot.Where(e => !File.Exists(e.Path)).Select(e => e.Path),
                StringComparer.OrdinalIgnoreCase);
            if (missing.Count == 0) return;

            bool removed;
            lock (_lock)
            {
                var before = _entries.Count;
                _ = _entries.RemoveAll(e => missing.Contains(e.Path));
                removed = _entries.Count != before;
                if (removed) Publish();
            }

            if (removed) Changed?.Invoke(this, EventArgs.Empty);
        });
        Background.Observe(prune, _log);
    }

    /// <summary>Removes a single history entry by path.</summary>
    public void Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var full = Path.GetFullPath(path);

        bool removed;
        lock (_lock)
        {
            removed = _entries.RemoveAll(e => string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Publish();
        }

        if (removed) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes all history entries and persists the empty list.</summary>
    public void Clear()
    {
        bool had;
        lock (_lock)
        {
            had = _entries.Count > 0;
            _entries.Clear();
            Publish();
        }

        if (had) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes the history now, if a save is waiting.</summary>
    public void Flush() => _saver.Flush();
    public void Dispose() => _saver.Dispose();

    /// <summary>Publishes the new snapshot and asks for a save. Call with <c>_lock</c> held.</summary>
    private void Publish()
    {
        _snapshot = [.. _entries];
        _saver.Request();
    }

    private void Write()
    {
        _ = JsonFile.Save(_filePath, _snapshot, _log);
        WriteCount++;
    }
}
