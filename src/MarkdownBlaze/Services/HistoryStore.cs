using System.Text.Json;

namespace MarkdownBlaze.Services;

public sealed record HistoryEntry(string Path, string Title, DateTimeOffset LastOpenedUtc);

/// <summary>Persists the global list of opened files to the local app-data folder.</summary>
public sealed class HistoryStore
{
    private const int MaxEntries = 200;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly List<HistoryEntry> _entries;
    private readonly object _lock = new(); // guards _entries (a background prune can touch it)

    /// <summary>Raised (possibly from a background thread) when the entry list changes out-of-band.</summary>
    public event Action? Changed;

    public HistoryStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkdownBlaze");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "history.json");
        _entries = Load();
    }

    /// <summary>Opened files, most-recent first. Returns a snapshot so callers can enumerate safely.</summary>
    public IReadOnlyList<HistoryEntry> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

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
            Save();
        }
    }

    /// <summary>
    /// Drops history entries whose file no longer exists, on a background thread so the (potentially
    /// slow, e.g. network paths) existence checks never delay startup. Raises <see cref="Changed"/> if
    /// anything was removed.
    /// </summary>
    public void PruneMissingInBackground()
    {
        Task.Run(() =>
        {
            try
            {
                List<HistoryEntry> snapshot;
                lock (_lock) snapshot = new List<HistoryEntry>(_entries);

                var missing = new HashSet<string>(
                    snapshot.Where(e => !File.Exists(e.Path)).Select(e => e.Path),
                    StringComparer.OrdinalIgnoreCase);
                if (missing.Count == 0) return;

                bool removed;
                lock (_lock)
                {
                    var before = _entries.Count;
                    _entries.RemoveAll(e => missing.Contains(e.Path));
                    removed = _entries.Count != before;
                    if (removed) Save();
                }

                if (removed) Changed?.Invoke();
            }
            catch { /* best-effort cleanup */ }
        });
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
            if (removed) Save();
        }

        if (removed) Changed?.Invoke();
    }

    /// <summary>Removes all history entries and persists the empty list.</summary>
    public void Clear()
    {
        bool had;
        lock (_lock)
        {
            had = _entries.Count > 0;
            _entries.Clear();
            Save();
        }

        if (had) Changed?.Invoke();
    }

    private List<HistoryEntry> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var list = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_filePath));
                if (list is not null) return list;
            }
        }
        catch { /* corrupt history is non-fatal */ }
        return [];
    }

    private void Save()
    {
        try { File.WriteAllText(_filePath, JsonSerializer.Serialize(_entries, JsonOptions)); }
        catch { /* best-effort */ }
    }
}
