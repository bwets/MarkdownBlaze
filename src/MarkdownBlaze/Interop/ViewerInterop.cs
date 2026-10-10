using MarkdownBlaze.Models;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace MarkdownBlaze.Interop;

/// <summary>
/// The viewer's connection to the JavaScript in the page (<c>wwwroot/js/main.js</c> and the modules
/// behind it), in both directions.
/// <para>
/// <b>Calls into the page</b> are the typed methods below; they import the module on first use and hand
/// it a <see cref="DotNetObjectReference{TValue}"/> to this object and the <see cref="ViewerConfig"/>
/// (<see langword="init"/>). <b>Calls back from the page</b> are the <c>[JSInvokable]</c> instance methods, which
/// only the holder of that reference can make: there is no assembly-wide entry point for a script
/// in a document to find (SEC-7). Each is passed on to the <see cref="IPageEvents"/> the viewer connected.
/// </para>
/// <para>
/// The object owns the module and the reference and disposes both.
/// </para>
/// </summary>
internal sealed partial class ViewerInterop(IJSRuntime js, ILogger<ViewerInterop> log) : IAsyncDisposable, IDisposable
{
    private IReadOnlyList<string> _shortcuts = [];
    private IPageEvents? _events;
    private Task<IJSObjectReference>? _module;
    private DotNetObjectReference<ViewerInterop>? _self;

    /// <summary>
    /// Says who answers the page and which key combinations it should report. Called by the viewer before
    /// anything else uses the page; the configuration is read when the module is first imported.
    /// </summary>
    public void Connect(IPageEvents events, IEnumerable<string> shortcuts)
    {
        _events = events;
        _shortcuts = [.. shortcuts];
    }

    // ---- into the page --------------------------------------------------------------------------

    public Task SetThemeAsync(string mode) => CallAsync("setTheme", mode);

    /// <summary>No remembered width → null, and the page picks the default against the left toolbar.</summary>
    public Task SetSidebarWidthAsync(int? width) => CallAsync("setSidebarWidth", width);
    public Task InitResizerAsync(string splitterId) => CallAsync("initResizer", splitterId);
    public Task SetContentThemeAsync(string id, bool isDark) => CallAsync("setContentTheme", id, isDark);
    public Task SetDocumentZoomAsync(double zoom, bool remember) => CallAsync("setDocumentZoom", zoom, remember);
    public Task ZoomByAsync(double factor) => CallAsync("zoomBy", factor);
    public Task SetViewModeAsync(ViewMode mode, PageFit fit) => CallAsync("setViewMode", ViewModes.Name(mode), ViewModes.Name(fit));

    /// <summary>Starts the guided tour; <paramref name="canDefer"/> says whether its first step offers Later.</summary>
    public Task StartTourAsync(bool canDefer) => CallAsync("startTour", canDefer);

    /// <summary>A document has just been put on the page: lay it out and render its code, math and diagrams.</summary>
    public Task RenderDocumentAsync() => CallAsync("renderDocument");
    public Task SetDocumentTitleAsync(string title, string? fragment) => CallAsync("setDocumentTitle", title, fragment);
    public Task ScrollToAsync(string id) => CallAsync("scrollTo", id);
    public Task ScrollTopAsync() => CallAsync("scrollTop");
    public Task ScrollTreeToAsync(string containerId, string rowId) => CallAsync("scrollTreeTo", containerId, rowId);
    public Task PrintAsync(string themeId, bool isDark) => CallAsync("print", themeId, isDark);
    public Task CopyAsync(string text) => CallAsync("copy", text);

    private async Task CallAsync(string function, params object?[] args)
    {
        var module = await ModuleAsync().ConfigureAwait(false);
        await module.InvokeVoidAsync(function, args).ConfigureAwait(false);
    }

    private Task<IJSObjectReference> ModuleAsync() => _module ??= LoadAsync();

    private async Task<IJSObjectReference> LoadAsync()
    {
        var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/main.js").ConfigureAwait(false);
        _self = DotNetObjectReference.Create(this);
        await module.InvokeVoidAsync("init", _self, ViewerConfig.Create(_shortcuts)).ConfigureAwait(false);
        return module;
    }

    // ---- back from the page ---------------------------------------------------------------------

    /// <summary>A link was clicked: <paramref name="kind"/> is "nav" or "ext", <paramref name="value"/> the target.</summary>
    [JSInvokable]
    public void OnLink(string kind, string value) => _events?.OnLink(kind, value);

    /// <summary>A shortcut was pressed.</summary>
    [JSInvokable]
    public Task OnKey(string combo) => _events?.OnKeyAsync(combo) ?? Task.CompletedTask;

    /// <summary>The sidebar was dragged to a new width.</summary>
    [JSInvokable]
    public void OnSidebarResized(int width) => _events?.OnSidebarResized(width);

    /// <summary>The document was zoomed.</summary>
    [JSInvokable]
    public void OnDocumentZoomed(double zoom) => _events?.OnDocumentZoomed(zoom);

    /// <summary>A file was dropped onto the window.</summary>
    [JSInvokable]
    public void OnFileDrop(string kind, string a, string b) => _events?.OnFileDrop(kind, a, b);

    /// <summary>The guided tour ended.</summary>
    [JSInvokable]
    public void OnTourEnded(bool deferred) => _events?.OnTourEnded(deferred);

    /// <summary>The tour asks for the print bar to be shown or put away.</summary>
    [JSInvokable]
    public Task OnTourPrintBar(bool open) => _events?.OnTourPrintBarAsync(open) ?? Task.CompletedTask;

    // ---- disposal -------------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        var module = _module;
        _module = null;
        if (module is not null)
        {
            try
            {
                var loaded = await module.ConfigureAwait(false);
                await loaded.InvokeVoidAsync("dispose").ConfigureAwait(false);
                await loaded.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException
                                           or InvalidOperationException or TaskCanceledException)
            {
                // The window is already closing and the page is gone; there is nothing left to tell.
                LogPageGone(log, ex);
            }
        }

        ReleaseReference();
    }

    /// <summary>The synchronous way out, for a container that disposes its services without waiting: lets go of the reference.</summary>
    public void Dispose() => ReleaseReference();

    private void ReleaseReference()
    {
        _self?.Dispose();
        _self = null;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The page was already gone when the viewer's interop was disposed")]
    private static partial void LogPageGone(ILogger log, Exception ex);
}
