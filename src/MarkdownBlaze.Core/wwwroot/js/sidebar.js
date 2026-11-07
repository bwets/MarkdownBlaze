// The sidebar's width: how narrow it may be, what it opens at, and the splitter that drags it. The
// width lives in a CSS variable on <html> while it is dragged and is reported to .NET on release.
import { call } from './bridge.js';

const DEFAULT_WIDTH = 280;
const SPARE = 64;
const MAX_WIDTH = 700;
const FALLBACK_MIN = 150;

/// The narrowest the sidebar may be, given the natural width of the left toolbar's buttons (0 when
/// they cannot be measured): just wide enough that they, which sit over it, still fit — so the middle
/// toolbar beside them never drifts off the sidebar's edge. That is the buttons plus the header's 8px
/// left padding, which puts the last of them flush with the sidebar's edge at the minimum.
export function minWidth(natural) {
    return natural > 0 ? Math.ceil(natural) + 8 : FALLBACK_MIN;
}

/// The width the sidebar opens at until the reader drags it. Worked out rather than fixed, so it is
/// wider than the left toolbar whatever that toolbar grows to: the usual width, or the minimum plus
/// clear room to spare, whichever is more.
export function defaultWidth(min) {
    return Math.max(DEFAULT_WIDTH, min + SPARE);
}

/// A dragged width, held between the minimum and the maximum.
export function clampWidth(width, min) {
    return Math.min(MAX_WIDTH, Math.max(min, width));
}

function currentMin() {
    const left = document.querySelector('.toolbar-left');
    if (!left || !left.firstElementChild) return FALLBACK_MIN;
    const natural = left.lastElementChild.getBoundingClientRect().right - left.firstElementChild.getBoundingClientRect().left;
    return minWidth(natural);
}

// Sidebar resizer (drag updates --sidebar-w; the final width is reported to .NET on release).
export function initResizer(splitterId) {
    const splitter = document.getElementById(splitterId);
    if (!splitter || splitter._wired) return;
    splitter._wired = true;
    const root = document.documentElement;
    let resizing = false, startX = 0, startW = 0;
    const cur = () => parseInt(getComputedStyle(root).getPropertyValue('--sidebar-w')) || defaultWidth(currentMin());
    splitter.addEventListener('pointerdown', e => {
        resizing = true; startX = e.clientX; startW = cur();
        splitter.classList.add('dragging');
        try { splitter.setPointerCapture(e.pointerId); } catch (_) { /* the pointer is already gone; dragging still works without capture */ }
    });
    splitter.addEventListener('pointermove', e => {
        if (!resizing) return;
        const w = clampWidth(startW + (e.clientX - startX), currentMin());
        root.style.setProperty('--sidebar-w', w + 'px');
    });
    const end = e => {
        if (!resizing) return;
        resizing = false;
        splitter.classList.remove('dragging');
        try { splitter.releasePointerCapture(e.pointerId); } catch (_) { /* never captured, or already released */ }
        call('OnSidebarResized', cur());
    };
    splitter.addEventListener('pointerup', end);
    splitter.addEventListener('pointercancel', end);
}

/// Applies the remembered sidebar width; with none (null), the default.
export function setSidebarWidth(w) {
    // A width remembered from before the minimum existed is brought up to it.
    const min = currentMin();
    const width = w ? Math.max(min, w) : defaultWidth(min);
    document.documentElement.style.setProperty('--sidebar-w', width + 'px');
}
