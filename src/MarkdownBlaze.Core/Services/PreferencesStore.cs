using MarkdownBlaze.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarkdownBlaze.Services;

/// <summary>
/// Persists user preferences to the local app-data folder (shared with the desktop app).
/// <para>
/// <see cref="Save"/> does not write at once: it asks for a write a moment later, so a burst of changes
/// (every tick of the zoom wheel) costs one write. <see cref="Flush"/> writes now — the window calls it
/// as it closes — and so does <see cref="Dispose"/>.
/// </para>
/// </summary>
internal sealed class PreferencesStore : IDisposable
{
    private readonly string _filePath;
    private readonly ILogger _log;
    private readonly SaveDebouncer _saver;

    public PreferencesStore(ILogger<PreferencesStore> log)
        : this(AppPaths.DataFolder, log)
    { }

    /// <summary>The preferences live in <paramref name="directory"/> — the tests point it at a scratch folder.</summary>
    internal PreferencesStore(string directory, ILogger? log = null, TimeSpan? debounce = null)
    {
        _log = log ?? NullLogger.Instance;
        _ = Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "preferences.json");
        _saver = new SaveDebouncer(Write, debounce ?? SaveDebouncer.DefaultDelay);
        Current = JsonFile.Load<Preferences>(_filePath, _log) ?? new Preferences();

        // The enums are checked by their converters; the document theme is an open set of ids, so an id
        // from a file (an edited one, a newer version's) is checked against the themes this version has.
        Current.ContentTheme = ContentThemes.Resolve(Current.ContentTheme).Id;
    }

    public Preferences Current { get; }

    /// <summary>How many times the file has actually been written (the tests count them).</summary>
    internal int WriteCount { get; private set; }

    /// <summary>Asks for the preferences to be saved shortly; many requests in a row are one write.</summary>
    public void Save() => _saver.Request();

    /// <summary>Writes the preferences now, if a save is waiting.</summary>
    public void Flush() => _saver.Flush();
    public void Dispose() => _saver.Dispose();

    private void Write()
    {
        _ = JsonFile.Save(_filePath, Current, _log);
        WriteCount++;
    }
}
