using MarkdownBlaze.Components.Viewer;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Interop;

/// <summary>
/// Answers the page: what <see cref="ViewerInterop"/> receives from the JavaScript is turned into a
/// command, a navigation or a change of state here. The page shows an untrusted document, so everything
/// it reports is a request — a link or a dropped path goes to <see cref="NavigationService"/>, which
/// checks it before it opens anything, and a key is only ever one of the combinations the command
/// registry gave the page to report.
/// <para>
/// The callbacks arrive from the JavaScript interop, on whatever thread that is; they change state on the
/// renderer's, like everything else in the viewer.
/// </para>
/// </summary>
internal sealed class PageEvents(
    NavigationService nav,
    ViewerState state,
    ViewerActions actions,
    Commands commands,
    ILogger<PageEvents> log) : IPageEvents
{
    public void OnLink(string kind, string value) => nav.HandleLink(kind, value);
    public void OnFileDrop(string kind, string a, string b) => nav.HandleFileDrop(kind, a, b);

    public Task OnKeyAsync(string combo) => state.Dispatcher.InvokeAsync(() =>
    {
        // Settings is in front: its page has no shortcuts, and the viewer under it must not act.
        if (state.Suspended) return Task.CompletedTask;
        return commands.TryRunCombo(combo, out var running) ? running : Task.CompletedTask;
    });

    public void OnSidebarResized(int width) => Post(() => actions.SetSidebarWidth(width));

    /// <summary>The toolbar shows the percentage, however it was changed.</summary>
    public void OnDocumentZoomed(double zoom) => Post(() => state.SetZoom(zoom));

    /// <summary>
    /// Finished or skipped, the tour has had its turn; only Settings brings it back. Put off, it is
    /// offered again at the next launch.
    /// </summary>
    public void OnTourEnded(bool deferred) => Post(() => actions.EndTour(deferred));

    /// <summary>
    /// The tour shows the print bar for the length of one step, then puts everything back the way
    /// Esc would: bar closed, the document in its own theme again.
    /// </summary>
    public Task OnTourPrintBarAsync(bool open) => state.Dispatcher.InvokeAsync(() =>
    {
        if (open)
        {
            actions.OpenPrintBar();
            return Task.CompletedTask;
        }

        return state.PrintBarOpen ? actions.CancelPrintBarAsync() : Task.CompletedTask;
    });

    private void Post(Action work) => Background.Observe(state.Dispatcher.InvokeAsync(work), log);
}
