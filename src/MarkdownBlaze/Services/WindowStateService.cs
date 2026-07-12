using System.Drawing;
using Photino.NET;

namespace MarkdownBlaze.Services;

/// <summary>
/// Restores the window's size, position and maximized state from <see cref="PreferencesStore"/> on
/// startup, and persists them as they change. Only the <em>restored</em> (non-maximized) bounds are
/// tracked, so un-maximizing returns to a sensible size while the maximized flag is stored
/// separately. First run (no saved bounds) starts maximized.
/// </summary>
public sealed class WindowStateService
{
    private const int MinWidth = 480;
    private const int MinHeight = 320;

    private readonly PreferencesStore _prefs;

    // Snapshot of the saved state, captured before any window event can mutate prefs. Photino fires
    // Size/Location/Restored events *during* window creation with the default bounds; without this
    // snapshot + the _ready gate below, those events would overwrite the values we mean to restore.
    private bool _savedMax;
    private int _savedW;
    private int _savedH;
    private int? _savedX;
    private int? _savedY;
    private bool _ready;
    private bool _fullscreen;
    private PhotinoWindow? _window;

    // Placement captured when entering fullscreen — Photino does not restore it on exit, so we do.
    private bool _preFullMax;
    private int _preFullW;
    private int _preFullH;
    private Point _preFullLoc;

    public WindowStateService(PreferencesStore prefs) => _prefs = prefs;

    /// <summary>
    /// Toggles borderless fullscreen (F11). Fullscreen is a transient view mode: its bounds are never
    /// tracked or saved, so the persisted size/position/maximized state is left untouched.
    /// </summary>
    public void ToggleFullScreen()
    {
        if (_window is null) return;

        if (!_fullscreen)
        {
            // Remember the current placement so we can restore it exactly when leaving fullscreen.
            _preFullMax = _window.Maximized;
            _preFullW = _window.Width;
            _preFullH = _window.Height;
            _preFullLoc = _window.Location;
            _fullscreen = true; // suspend tracking before the size changes
            _window.SetFullScreen(true);
        }
        else
        {
            // Leaving fullscreen: Photino keeps the fullscreen size, so restore the placement here.
            // Tracking stays suspended (_fullscreen) until the window is back to its prior bounds, so
            // the fullscreen dimensions are never written to preferences.
            _window.SetFullScreen(false);
            if (_preFullMax)
            {
                _window.SetMaximized(true);
            }
            else
            {
                _window.SetSize(_preFullW, _preFullH);
                _window.SetLocation(_preFullLoc);
            }

            _fullscreen = false;
        }
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
        window.SetMinSize(MinWidth, MinHeight);
        window.SetSize(_savedW, _savedH);
        if (_savedX is int x && _savedY is int y)
            window.SetLocation(new Point(x, y));
        else
            window.Centered = true;
        if (_savedMax)
            window.SetMaximized(true);

        // Once the native window exists: re-center if the saved spot is off-screen (e.g. a monitor was
        // disconnected), then enable live tracking. Creation-time events fire before this with _ready
        // still false, so they cannot clobber the snapshot.
        window.RegisterWindowCreatedHandler((_, _) =>
        {
            if (!_savedMax && _savedX is int sx && _savedY is int sy && !IsOnScreen(window, sx, sy, _savedW, _savedH))
                window.Center();
            _ready = true;
        });

        window.RegisterSizeChangedHandler((_, _) => TrackRestoredBounds(window));
        window.RegisterLocationChangedHandler((_, _) => TrackRestoredBounds(window));
        window.RegisterMaximizedHandler((_, _) =>
        {
            if (!_ready || _fullscreen) return;
            _prefs.Current.WindowMaximized = true;
            _prefs.Save();
        });
        window.RegisterRestoredHandler((_, _) =>
        {
            if (!_ready || _fullscreen) return;
            _prefs.Current.WindowMaximized = false;
            TrackRestoredBounds(window);
            _prefs.Save();
        });

        // Final save on close captures the latest size/position (size changes aren't saved per-pixel).
        window.RegisterWindowClosingHandler((_, _) =>
        {
            TrackRestoredBounds(window);
            _prefs.Save();
        });
    }

    /// <summary>Records the current bounds, but only while the window is in its normal (restored) state.</summary>
    private void TrackRestoredBounds(PhotinoWindow window)
    {
        if (!_ready || _fullscreen || window.Maximized || window.Minimized) return;

        var w = window.Width;
        var h = window.Height;
        if (w < MinWidth || h < MinHeight) return; // ignore transient / bogus sizes

        var loc = window.Location;
        var cur = _prefs.Current;
        cur.WindowWidth = w;
        cur.WindowHeight = h;
        cur.WindowX = loc.X;
        cur.WindowY = loc.Y;
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
                if (overlap.Width >= 100 && overlap.Height >= 40) return true;
            }

            return false;
        }
        catch
        {
            return true; // if monitors can't be queried, trust the saved position
        }
    }
}
