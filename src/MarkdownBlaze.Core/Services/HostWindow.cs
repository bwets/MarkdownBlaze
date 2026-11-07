namespace MarkdownBlaze.Services;

/// <summary>
/// What passes between a host that has a system window around the page (Android) and the viewer in it: how much of
/// the page the system's own bars cover, whether the app is drawn dark, and the system's Back button. One object, for the
/// same reason as <see cref="HostInbox"/>: the host is not on the renderer's thread and not necessarily
/// started after the viewer. The desktop host does not register it, and the viewer works without.
/// </summary>
internal sealed class HostWindow
{
    private readonly Lock _lock = new();
    private Func<Task<bool>>? _back;

    /// <summary>Raised, from any thread, when the system bars or the display cutout changed what covers the page.</summary>
    public event EventHandler? InsetsChanged;

    /// <summary>Raised, from the renderer's thread, when the app turned light or dark.</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>What covers the edges of the page now.</summary>
    public WindowInsets Insets { get; private set; } = WindowInsets.None;

    /// <summary>Whether the app is drawn dark; the host colours the system bars' icons to suit.</summary>
    public bool Dark { get; private set; }

    /// <summary>The host's window changed what covers the page.</summary>
    /// <param name="insets">The size of each edge, in the page's own (CSS) pixels.</param>
    public void SetInsets(WindowInsets insets)
    {
        Insets = insets;
        InsetsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The page reports whether the app is dark now.</summary>
    /// <param name="dark">True when it is.</param>
    public void SetDark(bool dark)
    {
        if (Dark == dark) return;
        Dark = dark;
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The viewer says how it answers Back: it takes the press (and returns true) or leaves it to the system.</summary>
    /// <param name="handler">Null when the viewer goes away.</param>
    public void SetBackHandler(Func<Task<bool>>? handler)
    {
        lock (_lock) _back = handler;
    }

    /// <summary>
    /// The system's Back was pressed. The viewer closes the topmost popup, bar or drawer, or goes back in the
    /// history; false means it had nothing to do, and the host does what Back does by default (leaves the app).
    /// </summary>
    public Task<bool> HandleBackAsync()
    {
        Func<Task<bool>>? handler;
        lock (_lock) handler = _back;
        return handler is null ? Task.FromResult(false) : handler();
    }
}
