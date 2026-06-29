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

    public HistoryStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkdownBlaze");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "history.json");
        _entries = Load();
    }

    /// <summary>Opened files, most-recent first.</summary>
    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public void Record(string path, string title)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var full = Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(title))
            title = Path.GetFileNameWithoutExtension(full);

        _entries.RemoveAll(e => string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase));
        _entries.Insert(0, new HistoryEntry(full, title, DateTimeOffset.UtcNow));
        if (_entries.Count > MaxEntries)
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);

        Save();
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
