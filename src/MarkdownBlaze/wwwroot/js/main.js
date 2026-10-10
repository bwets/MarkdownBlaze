// The page's side of MarkdownBlaze, as one ES module (ARCH-2, SEC-7).
//
// Blazor imports this module and calls the functions it exports; there is nothing on `window`, and
// nothing runs until `init`. The viewer's interop object calls `init(dotNetRef, config)` once:
//   - `dotNetRef` is a DotNetObjectReference to that object. Everything the page tells .NET — a link
//     click, a shortcut, a dropped file — is a call to one of its instance methods, so a script that
//     slipped into the document has no assembly-wide entry point to call.
//   - `config` is what .NET knows and the page must not repeat (see bridge.js and ViewerConfig.cs).
//
// This file only wires the other modules together, and holds the few operations that need two of
// them: changing a theme re-lays-out the page sheets and re-draws the diagrams.
import { bridge } from './bridge.js';
import { installDragDrop, installKeys, installMouseButtons } from './input.js';
import { installLinkClicks } from './links.js';
import { paginate, repaginate, setViewMode } from './pagination.js';
import { installPositionListeners, queuePosition, scrollTo, scrollTop, scrollTreeTo, unwatchPane, watchPane } from './position.js';
import { installPrintHooks, print } from './print.js';
import { redrawMermaid, renderDocument as renderMarkup } from './render.js';
import { initResizer, setSidebarWidth } from './sidebar.js';
import { applyContentTheme, mermaidTheme, setTheme as setThemeClass } from './theme.js';
import { endTour, startTour } from './tour.js';
import { installWheelZoom, setDocumentZoom, zoomBy } from './zoom.js';

let listeners = null; // AbortController for everything `init` installed

/// Hands the page its reference to .NET and its configuration, and starts listening. Calling it again
/// (a second viewer in the same page) replaces the first.
export function init(dotNetRef, config) {
    dispose();
    bridge.ref = dotNetRef;
    bridge.config = config;

    listeners = new AbortController();
    const { signal } = listeners;
    installKeys(signal);
    installMouseButtons(signal);
    installDragDrop(signal);
    installLinkClicks(signal);
    installWheelZoom(signal);
    installPositionListeners(signal);
    installPrintHooks(signal);
}

/// Stops listening and lets go of .NET. The interop object calls this when the viewer is disposed.
export function dispose() {
    if (listeners) listeners.abort();
    listeners = null;
    endTour(false);
    unwatchPane();
    bridge.ref = null;
    bridge.config = null;
}

// ---- Themes -------------------------------------------------------------------------------------

export function setTheme(mode) {
    setThemeClass(mode);
    // The document follows the app theme unless a content theme overrides it — so this changes the
    // paper too, and page view has to be rebuilt for the same reasons as a content theme change.
    paginate();
    return redrawMermaid(mermaidTheme()).then(() => paginate());
}

export function setContentTheme(id, isDark) {
    applyContentTheme(id, isDark);
    // Page view has to be rebuilt, not just repainted: the paper colour is sampled from the document,
    // and the theme's typography changes how much text fits on a sheet. Without this the sheets keep
    // the previous theme's paper — light text on white after leaving a dark theme — until something
    // else happens to repaginate, which is why a refresh appeared to "fix" it.
    paginate();
    // Diagrams bake their colours into the rendered SVG, and re-drawing them changes their height,
    // so the breaks are worked out again once they have settled.
    return redrawMermaid(mermaidTheme()).then(() => paginate());
}

// ---- The document -------------------------------------------------------------------------------

/// Called after a document has been put on the page. The page is laid out and the outline marked at
/// once; then, once the lazy renderers have had their turn (code, math, diagrams — each of which
/// changes the height of the document) and the fonts are in, the sheets are worked out once more.
/// Returns at once: the .NET side has nothing to wait for.
export function renderDocument() {
    watchPane();
    repaginate();
    queuePosition();
    renderMarkup()
        .then(() => document.fonts.ready)
        .then(() => { repaginate(); queuePosition(); })
        .catch(e => console.error('render', e));
}

/// What the print dialog and the printed page say about this document. The title names the print job
/// and pre-fills the "Save as PDF" filename; the address is printed in the page footer, so it carries
/// the document rather than a bare markdown://document/.
export function setDocumentTitle(name, fragment) {
    document.title = name ? name + ' — MarkdownBlaze' : 'MarkdownBlaze';
    try { history.replaceState(null, '', fragment || '#'); } catch (e) { /* address bar is cosmetic */ }
}

export function copy(text) {
    // writeText returns a promise: a refusal (no focus, no permission) arrives there, not as an exception.
    try { navigator.clipboard.writeText(text).catch(e => console.warn('copy', e)); } catch (e) { console.warn('copy', e); }
}

// What .NET calls directly.
export {
    initResizer,
    print,
    scrollTo,
    scrollTop,
    scrollTreeTo,
    setDocumentZoom,
    setSidebarWidth,
    setViewMode,
    startTour,
    zoomBy,
};
