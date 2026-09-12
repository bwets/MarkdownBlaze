using System.Diagnostics;

namespace MarkdownBlaze.Services;

/// <summary>
/// Holds the viewer state: the back/forward stack, the current document (rendered HTML + headings +
/// title), the global history, file-watch reloads and link handling. Raises <see cref="Changed"/>
/// whenever the UI should refresh.
/// <para>
/// The back/forward stack is not shown anywhere — it exists only to drive the two arrows. The list
/// of it used to be a sidebar tab; the global history is the one worth browsing.
/// </para>
/// </summary>
public sealed class NavigationService : IDisposable
{
    private readonly MarkdownService _md;
    private readonly HistoryStore _history;
    private readonly FileWatcher _watcher = new();

    private readonly List<string> _session = [];
    private int _index = -1;

    public NavigationService(MarkdownService md, HistoryStore history)
    {
        _md = md;
        _history = history;
        _watcher.Changed += Reload;
        _history.Changed += OnHistoryChanged;
        JsBridge.Link += OnLink;
        JsBridge.FileDropped += OnFileDropped;
    }

    // History pruned in the background (missing files removed) → refresh the sidebar.
    private void OnHistoryChanged() => Changed?.Invoke();

    public string? CurrentPath { get; private set; }
    public string CurrentTitle { get; private set; } = "MarkdownBlaze";
    public string CurrentHtml { get; private set; } = "";
    public IReadOnlyList<Heading> CurrentHeadings { get; private set; } = [];
    public int RenderToken { get; private set; }

    public bool CanBack => _index > 0;
    public bool CanForward => _index >= 0 && _index < _session.Count - 1;

    public IReadOnlyList<HistoryEntry> GlobalHistory => _history.Entries;

    /// <summary>Removes a single file from the persisted global history.</summary>
    public void RemoveFromHistory(string path) => _history.Remove(path);

    public event Action? Changed;

    private bool _initialized;

    /// <summary>
    /// Opens whatever the app was started with. Called by the viewer when it is created — which
    /// happens again every time the reader comes back from Settings, so it runs once and only once:
    /// otherwise leaving Settings would re-open the startup document and throw away the one being
    /// read, along with its place on the page.
    /// </summary>
    public void Initialize()
    {
        if (_initialized) { Changed?.Invoke(); return; }
        _initialized = true;

        var path = _md.GetStartupFilePath();
        if (path is not null) Navigate(path);
        else Changed?.Invoke();

        // Clean stale (deleted/moved) files out of the global history off the startup path.
        _history.PruneMissingInBackground();
    }

    public void Navigate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var full = Path.GetFullPath(path);

        if (_index < _session.Count - 1)
            _session.RemoveRange(_index + 1, _session.Count - _index - 1);
        if (_session.Count == 0 || !PathEquals(_session[^1], full))
            _session.Add(full);
        _index = _session.Count - 1;
        SetCurrent(_session[_index]);
    }

    /// <summary>
    /// Renders Markdown dropped into the window that has no resolvable file path (WebView2 does not
    /// expose one). <see cref="CurrentPath"/> stays null, so it is not file-watched and "open
    /// containing folder" is inert; relative images/links cannot be resolved without a base folder.
    /// </summary>
    public void OpenContent(string fileName, string markdown)
    {
        var result = _md.RenderText(fileName, markdown);
        CurrentPath = null;
        CurrentTitle = result.Title;
        CurrentHtml = result.BodyHtml;
        CurrentHeadings = result.Headings;
        RenderToken++;
        Changed?.Invoke();
    }

    private void OnFileDropped(string kind, string a, string b)
    {
        switch (kind)
        {
            case "path": Navigate(a); break;      // engine gave a real path → open like any other file
            case "text": OpenContent(a, b); break; // only the file's contents are available
        }
    }

    public void Back() { if (CanBack) { _index--; SetCurrent(_session[_index]); } }
    public void Forward() { if (CanForward) { _index++; SetCurrent(_session[_index]); } }

    public void Reload()
    {
        if (CurrentPath is not null) { Render(CurrentPath); Changed?.Invoke(); }
    }

    private void SetCurrent(string path)
    {
        _watcher.Watch(path);
        Render(path);
        _history.Record(path, CurrentTitle);
        Changed?.Invoke();
    }

    private void Render(string path)
    {
        var result = _md.Render(path);
        CurrentPath = path;
        if (result is null)
        {
            CurrentTitle = Path.GetFileNameWithoutExtension(path);
            CurrentHtml = "<p><em>Unable to open this file.</em></p>";
            CurrentHeadings = [];
        }
        else
        {
            CurrentTitle = result.Title;
            CurrentHtml = result.BodyHtml;
            CurrentHeadings = result.Headings;
        }
        RenderToken++;
    }

    private void OnLink(string kind, string value)
    {
        switch (kind)
        {
            case "nav": Navigate(value); break;
            case "ext": OpenExternal(value); break;
        }
    }

    // ---- external actions (also used by the history context menu) --------------------------------

    public static void OpenExternal(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    public static void OpenInNewWindow(string path)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null)
                Process.Start(new ProcessStartInfo(exe, $"\"{path}\"") { UseShellExecute = false });
        }
        catch { /* best-effort */ }
    }

    public static void OpenContainingFolder(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            ProcessStartInfo info;
            if (OperatingSystem.IsWindows())
                info = new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"");
            else if (OperatingSystem.IsMacOS())
                info = new ProcessStartInfo("open", $"-R \"{path}\"");
            else
                info = new ProcessStartInfo("xdg-open", $"\"{Path.GetDirectoryName(path)}\"");
            info.UseShellExecute = true;
            Process.Start(info);
        }
        catch { /* best-effort */ }
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        JsBridge.Link -= OnLink;
        JsBridge.FileDropped -= OnFileDropped;
        _history.Changed -= OnHistoryChanged;
        _watcher.Dispose();
    }
}
