// ---- Printing ---------------------------------------------------------------------------------
// Printing is driven from the print bar, which passes the content theme chosen for this print. The
// app chrome is dropped by the @media print rules; the app theme is forced light because it still
// colours the document when no content theme is active. Everything is restored afterwards from one
// snapshot of the <html> class list, which carries both the app theme and the content theme.
import { getView, paginate, setView } from './pagination.js';
import { redrawMermaid } from './render.js';
import { applyContentTheme, mermaidTheme } from './theme.js';
import { getDocZoom, restoreDocZoom } from './zoom.js';

let classBeforePrint = null;
let viewBeforePrint = null;

function snapshotForPrint() {
    if (classBeforePrint === null) classBeforePrint = document.documentElement.className;
}

// Page view draws its own sheets — paper, edges, shadow — and breaks the text with margins. On paper
// that is all in the way: the printer paginates by itself, so those sheets would be printed *onto*
// its pages, borders and all. Printing therefore falls back to the plain flow, at natural size, and
// page view is put back afterwards.
function enterPrintLayout() {
    if (viewBeforePrint) return;
    const view = getView();
    viewBeforePrint = { mode: view.mode, fit: view.fit, zoom: getDocZoom() };
    setView('continuous', view.fit);
    restoreDocZoom(1); // the reader's zoom is a screen comfort, not a paper size
    paginate();        // clears the sheets, the breaks and the zoom in one pass
}

function leavePrintLayout() {
    if (!viewBeforePrint) return;
    setView(viewBeforePrint.mode, viewBeforePrint.fit);
    restoreDocZoom(viewBeforePrint.zoom);
    viewBeforePrint = null;
    paginate();
}

function forceLightTheme() {
    snapshotForPrint();
    enterPrintLayout();
    const el = document.documentElement;
    el.classList.remove('theme-dark', 'theme-system');
    el.classList.add('theme-light');
}

function restoreThemeAfterPrint() {
    if (classBeforePrint === null && !viewBeforePrint) return;
    if (classBeforePrint !== null) {
        document.documentElement.className = classBeforePrint;
        classBeforePrint = null;
    }
    leavePrintLayout();
    return redrawMermaid(mermaidTheme());
}

/// Prints the document under `themeId` (an id from ContentThemes; 'auto' = no content theme).
export function print(themeId, themeIsDark) {
    snapshotForPrint();
    applyContentTheme(themeId, themeIsDark);
    forceLightTheme();
    // mermaid.run() is async, so wait for the re-drawn diagrams before opening the print dialog.
    return redrawMermaid(mermaidTheme()).then(function () {
        try { window.print(); } catch (e) { console.warn('print', e); }
        // print() returns once the dialog is dismissed, by which point afterprint has normally
        // restored everything; this is the safety net if the WebView never fires it.
        return restoreThemeAfterPrint();
    });
}

// Also covers a print started outside the app (the WebView's own shortcut/menu), where there is no
// chance to re-draw diagrams first — the CSS still switches to light.
export function installPrintHooks(signal) {
    window.addEventListener('beforeprint', forceLightTheme, { signal });
    window.addEventListener('afterprint', restoreThemeAfterPrint, { signal });
}
