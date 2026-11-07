using MarkdownBlaze.Interop;
using MarkdownBlaze.Models;
using MarkdownBlaze.Platform;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Components.Viewer;

/// <summary>
/// What the viewer does, as methods: open a file, change the zoom or the document's look, print, put a
/// popup away. The toolbar, the menus and the keyboard all end up here — the first two through
/// <see cref="ViewerMenus"/>, the keyboard and the toolbar's buttons through <see cref="Commands"/> —
/// so there is one way to do each thing. The state they change is <see cref="ViewerState"/>.
/// <para>
/// Everything after an <see langword="await"/> here goes on changing state, so those awaits keep the renderer's
/// context (<c>ConfigureAwait(true)</c>) rather than resuming on a thread-pool thread.
/// </para>
/// </summary>
internal sealed class ViewerActions(
    ViewerState state,
    NavigationService nav,
    FileTreeService tree,
    PreferencesStore prefs,
    IFilePicker dialogs,
    IWindowControl window,
    MarkdownService markdown,
    IShellLauncher shell,
    ViewerInterop interop,
    IFolderAccess? folders = null)
{
    // ---- files ------------------------------------------------------------------------------------

    /// <summary>
    /// Opening something is what sets the folder the tree shows: the file's own folder, or the folder
    /// itself. Following a link or a history entry deliberately leaves the root where it is.
    /// </summary>
    public async Task OpenFileAsync()
    {
        var startDir = tree.Root ?? (nav.CurrentPath is not null ? Path.GetDirectoryName(nav.CurrentPath) : null);
        var path = await dialogs.PickFileAsync(startDir).ConfigureAwait(true);
        if (path is null) return;

        // The file dialog's "All files" filter lets the reader pick any file, and a pick is a choice: it
        // is opened whatever it is called, which a link or a drop would not be.
        var opening = nav.NavigateAsync(path, anyType: true);
        tree.SetRootFromFile(path);
        await opening.ConfigureAwait(true);
    }

    public async Task PickFolderAsync()
    {
        var folder = await dialogs.PickFolderAsync(tree.Root).ConfigureAwait(true);
        if (folder is null) return;

        tree.SetRoot(folder);
        // A folder is opened by opening the document it leads with — its index/main/readme, or the
        // first document in alphabetical order.
        var entry = MarkdownService.FindEntryFile(folder);
        if (entry is not null) await nav.NavigateAsync(entry).ConfigureAwait(true);
    }

    // ---- documents that arrive from outside, and the folders behind them -----------------------------
    // Only a host with folders to grant (Android) has any of this: the desktop never posts a document, and
    // folders is null there.

    /// <summary>
    /// Shows a document the host put in the inbox: a file (with the folder it is in as the tree, or no tree for a
    /// file that came alone) or text that has no file. A file that came alone brings the offer of its folder, once it is
    /// on the page: the bar belongs to the document, and a document that is not there yet cannot have one.
    /// </summary>
    /// <param name="document">What the host was given.</param>
    public async Task OpenIncomingAsync(IncomingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        state.SetFolderOffer(offer: null);
        state.SetMissing(path: null); // a document the reader just asked for outranks the one that was not there
        if (document.Path is null)
        {
            await nav.OpenContentAsync(document.Name ?? "Shared text.md", document.Text ?? "").ConfigureAwait(true);
            return;
        }

        // A pick or an intent names the document: it is opened whatever it is called, as a pick in the file dialog is.
        var opening = nav.NavigateAsync(document.Path, anyType: true);
        if (document.Root is not null) tree.SetRoot(document.Root);
        else tree.ClearRoot();
        await opening.ConfigureAwait(true);

        if (document.FolderLabel is not null && folders is not null)
            state.SetFolderOffer(new FolderBarOffer(Path.GetFullPath(document.Path), document.FolderLabel));
    }

    /// <summary>
    /// <em>Allow</em> on the grant bar: the host asks the reader for the folder, and the document is opened again where the
    /// folder's copy has it, so that the tree, the links and the pictures are there. A folder that turns out not to
    /// hold the document is still kept; the document stays as it was, and the reader is told why.
    /// </summary>
    public async Task AllowFolderAsync()
    {
        if (state.FolderOffer is not { } offer || folders is null) return;

        var result = await folders.GrantAsync(offer.DocumentPath).ConfigureAwait(true);
        switch (result.Status)
        {
            case FolderGrantStatus.Granted:
                state.SetFolderOffer(offer: null);
                tree.SetRoot(result.Root);
                await nav.NavigateAsync(result.DocumentPath!, anyType: true).ConfigureAwait(true);
                // The single-file copy is replaced by the one in the folder; leaving its row would list the document twice.
                nav.RemoveFromHistory(offer.DocumentPath);
                break;
            case FolderGrantStatus.GrantedWithoutDocument:
                state.SetFolderOffer(offer: null);
                await state.ShowToastAsync("That folder does not contain this file, so it stays a single file.").ConfigureAwait(true);
                break;
            case FolderGrantStatus.Failed:
                await state.ShowToastAsync("Could not get access to that folder.").ConfigureAwait(true);
                break;
            case FolderGrantStatus.Cancelled:
                break;
        }
    }

    /// <summary><em>Not now</em> on the grant bar. It is offered again only when the document is opened again.</summary>
    public void DeclineFolder() => state.SetFolderOffer(offer: null);

    /// <summary>
    /// The app is back in front (D5: there is no way to watch a document the host cannot see change, so it looks when the
    /// reader returns). The folder of the open document is brought up to date; the tree is read again if files came or went,
    /// and the document is read again if its own file changed, keeping the place and zoom a reload keeps.
    /// </summary>
    public async Task RefreshAfterResumeAsync()
    {
        if (folders is null || nav.CurrentPath is not { } path) return;

        var result = await folders.SyncAsync(path).ConfigureAwait(true);
        if (result.TreeChanged) tree.Refresh();
        if (result.Changed.Any(changed => string.Equals(changed, path, StringComparison.OrdinalIgnoreCase)))
            await nav.ReloadAsync().ConfigureAwait(true);
    }

    public void OpenContainingFolder()
    {
        if (nav.CurrentPath is not null) shell.RevealInFileManager(nav.CurrentPath);
    }

    public Task ScrollToAsync(string id) => string.IsNullOrEmpty(id) ? Task.CompletedTask : interop.ScrollToAsync(id);
    public Task CopyNameAsync(string path) => interop.CopyAsync(Path.GetFileName(path));

    /// <summary>
    /// The toolbar's -/+/reset act on the same zoom the wheel and Ctrl+/- drive; the page does the zooming
    /// and reports the result (<see cref="ViewerState.SetZoom"/>), so the label follows whichever of them was used.
    /// </summary>
    public Task ZoomByAsync(double factor) => interop.ZoomByAsync(factor);
    public Task ResetZoomAsync() => interop.SetDocumentZoomAsync(1.0, remember: true);

    // ---- document theme ---------------------------------------------------------------------------

    private async Task ApplyContentThemeAsync(string id)
    {
        var theme = ContentThemes.Resolve(id);
        await interop.SetContentThemeAsync(theme.Id, theme.IsDark).ConfigureAwait(true);
    }

    /// <summary>The theme of the document as the reader keeps it, applied to the page again (after a print preview).</summary>
    public Task ApplyRememberedContentThemeAsync() => ApplyContentThemeAsync(prefs.Current.ContentTheme);

    public async Task SetContentThemeAsync(string id)
    {
        state.SetContentTheme(id);
        await ApplyContentThemeAsync(state.ContentTheme).ConfigureAwait(true);
    }

    /// <summary>Steps to the next or previous theme in the list, wrapping at either end (F9).</summary>
    public async Task CycleContentThemeAsync(int step)
    {
        var themes = ContentThemes.All;
        var index = Math.Max(0, ContentThemes.IndexOf(state.ContentTheme));
        var next = themes[(index + step + themes.Count) % themes.Count];
        await SetContentThemeAsync(next.Id).ConfigureAwait(true);
        await state.ShowToastAsync(next.Label).ConfigureAwait(true);
    }

    // ---- view mode --------------------------------------------------------------------------------
    // Continuous is one endless column; page lays the document onto sheets the way it will print,
    // sized either to the width of the pane or to show a whole sheet at once.

    /// <summary>
    /// The three ways of looking at a document, in the order F2 steps through them: reading, then
    /// the two paper views.
    /// </summary>
    private static readonly (ViewMode Mode, PageFit Fit)[] ViewCycle =
    [
        (ViewMode.Continuous, PageFit.Width),
        (ViewMode.Page, PageFit.Width),
        (ViewMode.Page, PageFit.Page),
    ];

    public static string ViewLabel(ViewMode mode, PageFit fit)
    {
        if (mode != ViewMode.Page) return "Continuous";
        return fit == PageFit.Page ? "Pages — whole page" : "Pages — full width";
    }

    public async Task SetViewAsync(ViewMode mode, PageFit fit)
    {
        state.SetView(mode, fit);
        await interop.SetViewModeAsync(state.ViewMode, state.PageFit).ConfigureAwait(true);
    }

    public async Task CycleViewAsync(int step)
    {
        var index = Array.FindIndex(ViewCycle, v => state.IsView(v.Mode, v.Fit));
        var next = ((index < 0 ? 0 : index) + step + ViewCycle.Length) % ViewCycle.Length;
        await SetViewAsync(ViewCycle[next].Mode, ViewCycle[next].Fit).ConfigureAwait(true);
        await state.ShowToastAsync(ViewLabel(ViewCycle[next].Mode, ViewCycle[next].Fit)).ConfigureAwait(true);
    }

    // ---- printing ---------------------------------------------------------------------------------
    // Ctrl+P does not print straight away: it opens the print bar so the content theme for this one
    // print can be picked (previewed live in the document), and a second Ctrl+P sends it.

    public void OpenPrintBar() => state.OpenPrintBar();

    public Task PrintShortcutAsync()
    {
        if (state.PrintBarOpen) return DoPrintAsync();
        state.OpenPrintBar();
        return Task.CompletedTask;
    }

    /// <summary>Applies a theme to the document while the bar is open, as a preview of the printout.</summary>
    public async Task PreviewPrintThemeAsync(string id)
    {
        state.PrintTheme = id;
        state.NotifyChanged();
        await ApplyContentThemeAsync(id).ConfigureAwait(true);
    }

    public async Task DoPrintAsync()
    {
        var theme = ContentThemes.Resolve(state.PrintTheme);
        state.ClosePrintBar(); // the bar must be gone before the dialog opens
        await interop.PrintAsync(theme.Id, theme.IsDark).ConfigureAwait(true);
        await ApplyRememberedContentThemeAsync().ConfigureAwait(true); // the override lasted for this print only
    }

    public async Task CancelPrintBarAsync()
    {
        state.ClosePrintBar();
        await ApplyRememberedContentThemeAsync().ConfigureAwait(true);
    }

    // ---- popups and bars --------------------------------------------------------------------------

    public void ToggleHelp() => state.SetShortcutsOpen(!state.ShortcutsOpen);
    public void OpenWhatsNew() => state.OpenWhatsNew(markdown.RenderWhatsNew() ?? "<p><em>Nothing to show.</em></p>");

    /// <summary>Esc: puts away whatever is open, and with nothing open leaves fullscreen, as everywhere else.</summary>
    public Task DismissAsync()
    {
        if (state.AnyPopupOpen)
        {
            state.CloseAllPopups();
            return Task.CompletedTask;
        }

        if (state.DrawerOpen)
        {
            state.CloseDrawer();
            return Task.CompletedTask;
        }

        if (state.PrintBarOpen) return CancelPrintBarAsync();

        _ = window.ExitFullScreen();
        return Task.CompletedTask;
    }

    /// <summary>
    /// The system's Back button (Android). It puts away the topmost thing that is open — the guided tour, a menu or
    /// a dialog, the drawer, the print bar — and with nothing open goes back in the history. False means there was
    /// nothing to do, and the host does what Back does by default: leaves the app.
    /// </summary>
    public async Task<bool> BackAsync()
    {
        if (await interop.DismissTourAsync().ConfigureAwait(true)) return true;

        if (state.AnyPopupOpen)
        {
            state.CloseAllPopups();
            return true;
        }

        if (state.DrawerOpen)
        {
            state.CloseDrawer();
            return true;
        }

        if (state.PrintBarOpen)
        {
            await CancelPrintBarAsync().ConfigureAwait(true);
            return true;
        }

        if (!nav.CanBack) return false;

        await nav.BackAsync().ConfigureAwait(true);
        return true;
    }

    public void ToggleFullScreen() => window.ToggleFullScreen();

    public void RateApp()
    {
        state.AnswerRating();
        _ = shell.OpenStoreReview(RatingPrompt.StoreReviewUri);
    }

    public void RemoveMissingFromHistory()
    {
        if (state.MissingPath is not null) nav.RemoveFromHistory(state.MissingPath);
        state.SetMissing(path: null);
    }

    /// <summary>A history row (or a link) led to a file that is not there: say so, and offer to drop the row.</summary>
    public Task ShowMissingAsync(string path)
    {
        // Only a history row can be removed, so only one is offered; a stale link just gets the toast.
        var inHistory = nav.GlobalHistory.Any(h => string.Equals(h.Path, path, StringComparison.OrdinalIgnoreCase));
        state.SetMissing(inHistory ? path : null);
        return state.ShowToastAsync("File not found: " + Path.GetFileName(path));
    }

    // ---- the rating request -----------------------------------------------------------------------
    // Documents are counted by the navigation service, when the document on the page changes — a
    // navigation event, not a render of the page. All that is left here is to show the request once it
    // is due.

    public void ShowRateBarIfDue()
    {
        if (RatingPrompt.IsDue(prefs.Current)) state.ShowRateBar();
    }

    /// <summary>
    /// How often the tour may be put off with <em>Later</em>. After that it is still offered once more, but
    /// only to be taken or skipped — an offer that can be postponed for ever is a nag. The count itself is
    /// <see cref="Preferences.TourDeferrals"/>.
    /// </summary>
    internal const int MaxTourDeferrals = 2;

    /// <summary>The tour is over for good, or put off until the next launch.</summary>
    public void EndTour(bool deferred)
    {
        if (deferred) prefs.Current.TourDeferrals++;
        else prefs.Current.TourSeen = true;
        prefs.Save();
    }

    public void SetSidebarWidth(int width)
    {
        prefs.Current.SidebarWidth = width;
        prefs.Save();
    }

    /// <summary>Starts the guided tour; the first step offers Later until it has been put off a couple of times.</summary>
    public Task StartTourAsync() => interop.StartTourAsync(prefs.Current.TourDeferrals < MaxTourDeferrals);
}
