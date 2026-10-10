using MarkdownBlaze.Interop;
using MarkdownBlaze.Models;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Components.Viewer;

/// <summary>
/// What several parts of the viewer read: how the document is shown (zoom, view mode, document theme,
/// whether the sidebar is out), which popups are open, and the sidebar's rows. One instance per window,
/// registered as a scoped service; the components that draw it subscribe to <see cref="Changed"/>.
/// <para>
/// A part that wants something to change calls a method here (or a command, which calls it); nothing
/// reaches into another component's fields. What is remembered between launches is written to the
/// preferences as it changes. Like <see cref="NavigationService"/>, it is single-threaded: it is used on
/// the renderer's thread, and <see cref="Changed"/> is raised there.
/// </para>
/// </summary>
internal sealed class ViewerState
{
    private static readonly ILogger Logger = Log.For(nameof(ViewerState));
    private readonly PreferencesStore _prefs;

    public ViewerState(PreferencesStore prefs)
    {
        _prefs = prefs;
        var current = prefs.Current;
        SidebarPinned = current.SidebarPinned;
        SidebarTab = Math.Clamp(current.SidebarTab, 0, 1);
        ViewMode = current.ViewMode;
        PageFit = current.PageFit;
        ContentTheme = ContentThemes.Resolve(current.ContentTheme).Id;
        Zoom = Math.Clamp(current.DocumentZoom, ZoomLimits.Default.Min, ZoomLimits.Default.Max);
    }

    /// <summary>Raised whenever anything the viewer draws has changed; the components re-render.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the More menu asks for Settings; the viewer hands the request to the app.</summary>
    public event EventHandler? SettingsRequested;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Calls <paramref name="refresh"/> whenever the state changes, for a component that draws it. Dispose
    /// the result when the component goes.
    /// </summary>
    public IDisposable Watch(Func<Task> refresh)
    {
        void OnChanged(object? sender, EventArgs e)
        {
            Background.Run(refresh, Logger);
        }

        Changed += OnChanged;
        return new Subscription(() => Changed -= OnChanged);
    }

    private sealed class Subscription(Action cancel) : IDisposable
    {
        public void Dispose() => cancel();
    }

    public void RequestSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The way back to the renderer's thread, for what finishes elsewhere (the toast's timer, the page's callbacks).</summary>
    public void Attach(IUiDispatcher dispatcher) => Dispatcher = dispatcher;

    public IUiDispatcher Dispatcher { get; private set; } = ImmediateDispatcher.Instance;

    /// <summary>
    /// True while Settings is drawn over the viewer. The viewer stays alive underneath — it keeps its
    /// scroll position, zoom and tree — but it is not the one being used, so keyboard shortcuts leave it
    /// alone.
    /// </summary>
    public bool Suspended { get; set; }

    /// <summary>Whether the sidebar is out.</summary>
    public bool SidebarPinned { get; private set; }

    /// <summary>0 = Contents, 1 = History.</summary>
    public int SidebarTab { get; private set; }

    public void ToggleSidebar()
    {
        SidebarPinned = !SidebarPinned;
        _prefs.Current.SidebarPinned = SidebarPinned;
        _prefs.Save();
        NotifyChanged();
    }

    public void SelectTab(int index)
    {
        SidebarTab = Math.Clamp(index, 0, 1);
        _prefs.Current.SidebarTab = SidebarTab;
        _prefs.Save();
        NotifyChanged();
    }

    /// <summary>The Contents tab's rows (see <see cref="TreeRows"/>).</summary>
    public TreeRows Rows { get; private set; } = TreeRows.Empty;

    public void SetRows(TreeRows rows) => Rows = rows;

    // ---- how the document is shown ----------------------------------------------------------------

    /// <summary>Continuous or page.</summary>
    public ViewMode ViewMode { get; private set; }

    /// <summary>In page view, what a sheet is sized against.</summary>
    public PageFit PageFit { get; private set; }

    /// <summary>
    /// Whether this is the view a menu entry stands for. "Continuous" is the same whatever the fit, so the
    /// fit counts only for the paper views.
    /// </summary>
    public bool IsView(ViewMode mode, PageFit fit) =>
        ViewMode == mode && (mode == ViewMode.Continuous || PageFit == fit);

    public void SetView(ViewMode mode, PageFit fit)
    {
        ViewMode = mode;
        PageFit = fit;
        _prefs.Current.ViewMode = ViewMode;
        _prefs.Current.PageFit = PageFit;
        _prefs.Save();
        NotifyChanged();
    }

    /// <summary>The id of the document theme (see <see cref="ContentThemes"/>).</summary>
    public string ContentTheme { get; private set; }

    public bool IsContentTheme(string id) => string.Equals(ContentTheme, id, StringComparison.Ordinal);

    public void SetContentTheme(string id)
    {
        ContentTheme = ContentThemes.Resolve(id).Id;
        _prefs.Current.ContentTheme = ContentTheme;
        _prefs.Save();
        NotifyChanged();
    }

    /// <summary>
    /// The document's zoom, 1 = natural size. It is the page that zooms (wheel, keys, buttons) and reports
    /// the result; the toolbar shows it, and the limits it greys out at are the ones the page was given.
    /// </summary>
    public double Zoom { get; private set; }

    public void SetZoom(double zoom)
    {
        Zoom = zoom;
        _prefs.Current.DocumentZoom = zoom;
        _prefs.Save();
        NotifyChanged();
    }

    // ---- print bar --------------------------------------------------------------------------------

    public bool PrintBarOpen { get; private set; }

    /// <summary>The document theme picked in the print bar for this print.</summary>
    public string PrintTheme { get; set; } = ContentThemes.Auto;

    public void OpenPrintBar()
    {
        PrintTheme = _prefs.Current.ContentTheme;
        PrintBarOpen = true;
        NotifyChanged();
    }

    public void ClosePrintBar()
    {
        PrintBarOpen = false;
        NotifyChanged();
    }

    // ---- popups -----------------------------------------------------------------------------------

    public ContextMenuModel? Menu { get; private set; }
    public string? WhatsNewHtml { get; private set; }
    public bool ShortcutsOpen { get; private set; }
    public bool AnyPopupOpen => Menu is not null || WhatsNewHtml is not null || ShortcutsOpen;

    public void OpenMenu(ContextMenuModel menu)
    {
        Menu = menu;
        NotifyChanged();
    }

    public void CloseMenu()
    {
        Menu = null;
        NotifyChanged();
    }

    public void OpenWhatsNew(string html)
    {
        WhatsNewHtml = html;
        NotifyChanged();
    }

    public void CloseWhatsNew()
    {
        WhatsNewHtml = null;
        NotifyChanged();
    }

    public void SetShortcutsOpen(bool open)
    {
        ShortcutsOpen = open;
        NotifyChanged();
    }

    public void CloseAllPopups()
    {
        Menu = null;
        WhatsNewHtml = null;
        ShortcutsOpen = false;
        NotifyChanged();
    }

    // ---- bars -------------------------------------------------------------------------------------

    public bool RateBarOpen { get; private set; }

    /// <summary>The history entry whose file turned out not to exist, while the bar that offers to drop it is up.</summary>
    public string? MissingPath { get; private set; }

    public void ShowRateBar()
    {
        if (RateBarOpen) return;
        RateBarOpen = true;
        NotifyChanged();
    }

    /// <summary>Rated or waved away, the request has been answered and is not made again.</summary>
    public void AnswerRating()
    {
        RateBarOpen = false;
        _prefs.Current.RatingAnswered = RatingPrompt.Campaign;
        _prefs.Save();
        NotifyChanged();
    }

    public void SetMissing(string? path)
    {
        MissingPath = path;
        NotifyChanged();
    }

    // ---- toast ------------------------------------------------------------------------------------

    public string? Toast { get; private set; }

    /// <summary>Changes with every message, so the toast's element is replaced and its animation starts over.</summary>
    public int ToastToken { get; private set; }

    /// <summary>Names what you just switched to, briefly. A newer message replaces an older one.</summary>
    public async Task ShowToastAsync(string message)
    {
        var token = ++ToastToken;
        Toast = message;
        NotifyChanged();

        await Task.Delay(1400).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() =>
        {
            if (token != ToastToken) return; // something newer is on screen
            Toast = null;
            NotifyChanged();
        })
            .ConfigureAwait(true);
    }
}
