using System.Drawing;
using Microsoft.Extensions.Logging;
using Photino.NET;

namespace MarkdownBlaze.Services;

/// <summary>
/// Restores the window's size, position and maximized state from <see cref="PreferencesStore"/> on
/// startup, and persists them as they change. Only the <em>restored</em> (non-maximized) bounds are
/// tracked, so un-maximizing returns to a sensible size while the maximized flag is stored
/// separately. First run (no saved bounds) starts maximized.
/// </summary>
internal sealed partial class WindowStateService
{
    private static readonly ILogger Logger = Log.For(nameof(WindowStateService));

    private const int MinWidth = 480;
    private const int MinHeight = 320;

    /// <summary>
    /// How much of a saved window has to be on some monitor for the position to count as reachable: a
    /// strip of title bar to grab and drag back is enough, a corner of a pixel is not.
    /// </summary>
    private const int MinVisibleWidth = 100;
    private const int MinVisibleHeight = 40;

    private readonly PreferencesStore _prefs;
    private readonly HistoryStore _history;

    /// <summary>
    /// Snapshot of the saved state, captured before any window event can mutate prefs. Photino fires
    /// Size/Location/Restored events *during* window creation with the default bounds; without this
    /// snapshot + the _ready gate below, those events would overwrite the values we mean to restore.
    /// </summary>
    private bool _savedMax;
    private int _savedW;
    private int _savedH;
    private int? _savedX;
    private int? _savedY;
    private bool _ready;
    private PhotinoWindow? _window;

    public WindowStateService(PreferencesStore prefs, HistoryStore history)
    {
        _prefs = prefs;
        _history = history;
    }

    /// <summary>True while the window is borderless fullscreen, as reported by the OS.</summary>
    public bool IsFullScreen => _window?.WindowState == PhotinoWindowState.FullScreen;

    /// <summary>
    /// Toggles borderless fullscreen (F11). PhotinoX saves the window's placement (style, restored
    /// bounds and maximized flag) before entering and restores it on exit, so leaving fullscreen puts
    /// the window back exactly where it was — no bookkeeping needed on our side. Fullscreen is a
    /// transient view mode: <see cref="TrackRestoredBounds"/> only records the Normal state, so the
    /// fullscreen bounds are never written to preferences.
    /// </summary>
    public void ToggleFullScreen() => _ = _window?.SetFullScreen(!IsFullScreen);

    /// <summary>Leaves fullscreen if the window is in it; returns false when there was nothing to do.</summary>
    public bool ExitFullScreen()
    {
        if (!IsFullScreen) return false;
        _ = _window!.SetFullScreen(fullScreen: false);
        return true;
    }

    /// <summary>Wires the service to the main window. Call once, before <c>app.Run()</c>.</summary>
    public void Attach(PhotinoWindow window)
    {
        _window = window;
        var p = _prefs.Current;
        _savedMax = p.WindowMaximized;
        _savedW = Math.Max(p.WindowWidth, MinWidth);
        _savedH = Math.Max(p.WindowHeight, MinHeight);
        _savedX = p.WindowX;
        _savedY = p.WindowY;

        // Apply the saved bounds before the window is shown (pre-run setters are reliable in Photino).
        _ = window.SetMinSize(MinWidth, MinHeight);
        _ = window.SetSize(_savedW, _savedH);
        if (_savedX is int x && _savedY is int y)
            _ = window.SetLocation(new Point(x, y));
        else
            window.CenterOnInitialize = true;

        if (_savedMax)
            _ = window.SetMaximized(maximized: true);

        // Once the native window exists: re-center if the saved spot is off-screen (e.g. a monitor was
        // disconnected), then enable live tracking. Creation-time events fire before this with _ready
        // still false, so they cannot clobber the snapshot.
        _ = window.RegisterCreatedHandler((_, _) =>
        {
            if (!_savedMax && _savedX is int sx && _savedY is int sy && !IsOnScreen(window, sx, sy, _savedW, _savedH))
                _ = window.Center();

            _ready = true;
        });

        _ = window.RegisterSizeChangedHandler((_, _) => TrackRestoredBounds(window));
        _ = window.RegisterLocationChangedHandler((_, _) => TrackRestoredBounds(window));
        _ = window.RegisterMaximizedHandler((_, _) =>
        {
            if (!_ready) return;
            _prefs.Current.WindowMaximized = true;
            _prefs.Save();
        });
        _ = window.RegisterRestoredHandler((_, _) =>
        {
            if (!_ready) return;
            _prefs.Current.WindowMaximized = false;
            TrackRestoredBounds(window);
            _prefs.Save();
        });

        // Closing is where everything that was waiting for its debounced write is written: the final
        // bounds, the last zoom, the history. (Disposing the stores flushes them again, for a shutdown
        // that does not come through here.)
        _ = window.RegisterClosingHandler((_, _) =>
        {
            TrackRestoredBounds(window);
            _prefs.Flush();
            _history.Flush();
        });
    }

    /// <summary>Records the current bounds, but only while the window is in its normal (restored) state.</summary>
    private void TrackRestoredBounds(PhotinoWindow window)
    {
        // Maximized, minimized and fullscreen are all tracked natively, so one state check keeps every
        // transient set of bounds out of preferences.
        if (!_ready || window.WindowState != PhotinoWindowState.Normal) return;

        var w = window.Width;
        var h = window.Height;
        if (w < MinWidth || h < MinHeight) return; // ignore transient / bogus sizes

        var loc = window.Location;
        var cur = _prefs.Current;
        cur.WindowWidth = w;
        cur.WindowHeight = h;
        cur.WindowX = loc.X;
        cur.WindowY = loc.Y;

        // Saved as they change rather than only on close, so a crash does not lose where the window was;
        // the save is debounced, so dragging the window is one write when it stops.
        _prefs.Save();
    }

    /// <summary>
    /// True when a meaningful part of the saved rectangle overlaps a connected monitor — guards against
    /// restoring onto a monitor that has since been disconnected (which would open off-screen).
    /// </summary>
    private static bool IsOnScreen(PhotinoWindow window, int x, int y, int w, int h)
    {
        try
        {
            var rect = new Rectangle(x, y, Math.Max(w, 1), Math.Max(h, 1));
            foreach (var monitor in window.Monitors)
            {
                var overlap = Rectangle.Intersect(monitor.MonitorArea, rect);
                if (overlap.Width >= MinVisibleWidth && overlap.Height >= MinVisibleHeight) return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Decided: PhotinoX does not document what its monitor query throws, so the list is the
            // failures of the native layer being absent or refusing, and nothing wider. Anything else is
            // left to surface rather than be guessed at; add to the list only from a documented source.
            LogMonitorsUnavailable(Logger, ex);
            return true; // if monitors can't be queried, trust the saved position
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not query the monitors; trusting the saved window position")]
    private static partial void LogMonitorsUnavailable(ILogger log, Exception ex);
}
