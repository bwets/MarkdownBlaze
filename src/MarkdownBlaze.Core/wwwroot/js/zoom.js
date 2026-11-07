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

/// The zoom a pinch gives: the reader's zoom when the second finger went down, times how much further apart
/// (or closer) the fingers have moved since, held between the limits. A pinch that started with the fingers
/// on one spot (no distance to compare) changes nothing.
export function pinchZoom(startZoom, startDistance, distance, min, max) {
    if (!(startDistance > 0) || !(distance >= 0)) return clampZoom(startZoom, min, max);
    return clampZoom(startZoom * distance / startDistance, min, max);
}

/// Where the pane should be scrolled so that the point that was under the middle of the fingers when the pinch began
/// (`from`, measured from the pane's edge) is under their middle now (`to`) once the zoom has changed by `ratio`: a
/// scroll offset is a distance in zoomed pixels, so the point's place in the document scales with the zoom.
export function scrollAfterZoom(scroll, from, to, ratio) {
    return (scroll + from) * ratio - to;
}

// Pinch, with two fingers on the document. The WebView's own pinch is off (the viewport forbids scaling and Program /
// WebViewPolicy switch the WebView's zoom off), and the pane's `touch-action` leaves the browser only the panning,
// so the fingers arrive here as pointer events. The page, the toolbar and the sidebar keep their size: only the
// document zooms, through the same path as the wheel and the buttons.
export function installPinchZoom(signal) {
    const touches = new Map(); // pointerId -> { x, y }, the fingers on the document
    let pinch = null;          // { zoom, distance, left, top, x, y, pane } while two are down: where it began

    const spread = () => {
        const [a, b] = [...touches.values()];
        return { distance: Math.hypot(a.x - b.x, a.y - b.y), x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
    };

    const end = e => {
        if (!touches.delete(e.pointerId) || !pinch) return;
        // One finger left, or none: the pinch is over, and the toolbar's percentage hears about it once.
        pinch = null;
        call('OnDocumentZoomed', docZoom);
    };

    document.addEventListener('pointerdown', e => {
        if (e.pointerType !== 'touch') return;
        const pane = e.target.closest ? e.target.closest('.content-area') : null;
        if (!pane || !bridge.config) return;
        touches.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (touches.size !== 2) return;
        const { distance, x, y } = spread();
        const rect = pane.getBoundingClientRect();
        pinch = { zoom: docZoom, distance, left: pane.scrollLeft, top: pane.scrollTop, x: x - rect.left, y: y - rect.top, pane };
    }, { signal });

    document.addEventListener('pointermove', e => {
        const finger = touches.get(e.pointerId);
        if (!finger) return;
        finger.x = e.clientX;
        finger.y = e.clientY;
        if (!pinch || touches.size !== 2) return;

        const { min, max } = config().zoom;
        const now = spread();
        const rect = pinch.pane.getBoundingClientRect();
        const next = pinchZoom(pinch.zoom, pinch.distance, now.distance, min, max);
        const before = docZoom;
        setDocumentZoom(next, false);
        if (docZoom !== before) {
            const ratio = docZoom / pinch.zoom;
            pinch.pane.scrollLeft = scrollAfterZoom(pinch.left, pinch.x, now.x - rect.left, ratio);
            pinch.pane.scrollTop = scrollAfterZoom(pinch.top, pinch.y, now.y - rect.top, ratio);
        }
        e.preventDefault();
    }, { signal });

    document.addEventListener('pointerup', end, { signal });
    document.addEventListener('pointercancel', end, { signal });
}
