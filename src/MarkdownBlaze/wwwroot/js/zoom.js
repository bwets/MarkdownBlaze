// ---- Document zoom ----------------------------------------------------------------------------
// Ctrl+wheel, Ctrl+plus/minus and pinch all zoom the *document*. The WebView's own zoom, which
// would scale the toolbar and sidebar with it, is switched off in Program.cs.
//
// The limits and the step come from .NET (config.zoom), where the toolbar's buttons use the same ones
// to grey out exactly when a further step would do nothing.
import { bridge, call, config } from './bridge.js';
import { pageParts } from './dom.js';

// How much the reader has zoomed the document, independent of the mode. In page view it multiplies
// the fit, so Ctrl+0 returns to a clean "full width" or "whole page".
let docZoom = 1;
let fitScale = 1;
let paged = false;

/// `zoom` held between `min` and `max`; anything that is not a number is 100%.
export function clampZoom(zoom, min, max) {
    return Math.min(max, Math.max(min, Number(zoom) || 1));
}

export function getDocZoom() { return docZoom; }

/// Sets the reader's zoom without telling anyone (the print layout puts it back afterwards).
export function restoreDocZoom(zoom) { docZoom = zoom; }

/// The page view tells the zoom what the sheet needs on top of the reader's own zoom: `scale` is
/// the fit, `isPaged` whether the document is laid onto sheets at all.
export function setFit(scale, isPaged) {
    fitScale = scale;
    paged = isPaged;
}

/// Whether the document is laid onto sheets at all — the pane's zoom is then the fit times the reader's.
export function setPaged(isPaged) { paged = isPaged; }

export function setDocumentZoom(zoom, remember) {
    const { min, max } = config().zoom;
    const next = clampZoom(zoom, min, max);
    const changed = Math.abs(next - docZoom) > 0.0001;
    docZoom = next;
    applyZoom();
    if (changed && remember !== false) call('OnDocumentZoomed', docZoom);
}

// The zoom is kept on the pane, as --doc-zoom, and app.css hands it to whichever document element
// the pane holds. It cannot live on the document itself: Blazor rebuilds that element every time
// the file is re-read, and a zoom set on the old one is gone with it — which is how an edit made
// in another program used to snap the page back to 100% while the toolbar still said otherwise.
export function applyZoom() {
    const { flow, pane } = pageParts();
    if (!pane) return;
    const zoom = paged ? fitScale * docZoom : docZoom;
    pane.style.setProperty('--doc-zoom', zoom);
    if (flow) flow.style.removeProperty('zoom'); // a measuring pass pins it at 1; hand it back
}

/// One step in or out, for the toolbar's zoom buttons — the same step the wheel and keys take.
export function zoomBy(factor) {
    setDocumentZoom(docZoom * factor);
}

export function zoomStep() { return config().zoom.step; }

/// The zoom keys: Ctrl+0 resets, Ctrl+plus zooms in, Ctrl+minus out. They are handled here rather
/// than round-tripping through .NET, since they only move the document. Returns whether the event
/// was one of them (and has been taken over).
export function handleZoomKey(e) {
    if (!e.ctrlKey || !['0', '+', '=', '-', '_'].includes(e.key)) return false;
    e.preventDefault();
    if (e.key === '0') setDocumentZoom(1);
    else zoomBy(e.key === '-' || e.key === '_' ? 1 / zoomStep() : zoomStep());
    return true;
}

// passive:false so the zoom gesture can be taken over before the WebView acts on it. A trackpad
// pinch arrives here too — Chromium reports it as a wheel event with ctrlKey set.
export function installWheelZoom(signal) {
    window.addEventListener('wheel', function (e) {
        if (!e.ctrlKey || !bridge.config) return;
        e.preventDefault();
        zoomBy(e.deltaY < 0 ? zoomStep() : 1 / zoomStep());
    }, { passive: false, signal });
}
