// ---- View modes -------------------------------------------------------------------------------
// Continuous is one endless column. Page lays the document onto sheets, the way it will print.
//
// The sheets are drawn by the flow itself, not by separate page elements: the document stays one
// element (Blazor owns it, and highlight.js, KaTeX and mermaid have already written into it), it is
// given the width of a sheet, and a repeating gradient paints the paper and the gaps behind it.
// A block that would straddle a gap is pushed to the next sheet with a margin.
//
// The margin matters: adding spacer elements would be the obvious way to do this, and it is the way
// that breaks the app. These are Blazor's children — inserting nodes among them leaves its render
// tree describing a DOM that no longer exists, and the next diff blanks the document. Same rule as
// the renderers: change what is there, never what is in the tree.
//
// The size of a sheet (A4, in mm), its margin and the on-screen gap come from .NET (config.page).
import { config } from './bridge.js';
import { isDarkColour, opaqueBackground, shift } from './colour.js';
import { pageParts } from './dom.js';
import { applyZoom, setFit, setPaged } from './zoom.js';

let view = { mode: 'continuous', fit: 'width' };

export function getView() { return { mode: view.mode, fit: view.fit }; }

/// Sets the mode without laying anything out; the caller paginates when it is ready.
export function setView(mode, fit) {
    view = { mode: mode === 'page' ? 'page' : 'continuous', fit: fit === 'page' ? 'page' : 'width' };
    setPaged(view.mode === 'page');
}

export const mm = v => Math.round(v * 96 / 25.4);

/// A sheet in pixels, from the page description .NET gave: `{ w, h, m, gap }`.
export function sheetGeometry(page) {
    return { w: mm(page.widthMm), h: mm(page.heightMm), m: mm(page.marginMm), gap: page.gapPx };
}

/// Walks the document's blocks and, when one would cross the bottom of its sheet, pushes it down to
/// the top of the next with a margin. Sheet k holds content between k*sheet + margin and
/// k*sheet + height - margin. A block is anything with `offsetTop`, `offsetHeight` and a `style`; the
/// browser's elements are, and so are the fakes the tests lay out. Returns the number of sheets.
export function layOut(blocks, { h, m, gap }) {
    const sheet = h + gap;              // one sheet plus the gap below it
    let page = 0;
    for (const block of blocks) {
        const contentEnd = page * sheet + h - m;
        const top = block.offsetTop;
        const bottom = top + block.offsetHeight;
        if (bottom <= contentEnd) continue;

        if (top <= page * sheet + m) {
            // Taller than a whole sheet (a long table or a big diagram): let it run across sheets
            // rather than leaving a page blank, and carry on from wherever it ended.
            page = Math.floor(bottom / sheet);
            continue;
        }

        // Carry the block to the top of the next sheet. The margin is corrected once after measuring
        // because it collapses with the previous block's bottom margin rather than adding to it.
        const wanted = (page + 1) * sheet + m;
        block.style.marginTop = (wanted - top) + 'px';
        const drift = wanted - block.offsetTop;
        if (Math.abs(drift) > 0.5) block.style.marginTop = (wanted - top + drift) + 'px';
        page++;
    }
    return page + 1;
}

/// How much padding the last sheet needs so the paper does not stop mid-page; 0 when it is full.
export function fillLastSheet(pages, { h, m, gap }, used) {
    const wanted = pages * (h + gap) - gap;
    return wanted > used ? wanted - used + m : 0;
}

/// Fit: the sheet either fills the pane's width, or sits in it whole. The reader's own zoom then
/// multiplies that, so Ctrl+0 comes back to the plain fit.
export function fitScale(fit, pane, { w, h }) {
    const room = 24;
    const scale = fit === 'page'
        ? Math.min((pane.clientHeight - room) / h, (pane.clientWidth - room) / w)
        : (pane.clientWidth - room) / w;
    return Math.max(0.2, Math.min(scale, 3));
}

export function setViewMode(mode, fit) {
    setView(mode, fit);
    paginate();
}

// Re-run whenever what is on the page, or the space it has, changes.
export function repaginate() { if (view.mode === 'page') paginate(); }

export function paginate() {
    const { flow, pane } = pageParts();
    if (!flow || !pane) return;

    // Always start from a clean flow: breaks and zoom from a previous pass would corrupt measuring.
    // The zoom is pinned at 1 on the element for the length of this pass (it outranks the pane's
    // --doc-zoom) and released by applyZoom at the end; nothing is painted in between.
    for (const block of flow.children) block.style.removeProperty('margin-top');
    flow.style.zoom = '1';
    flow.style.removeProperty('padding-bottom');

    // Read the document's own colours with page styling switched off. Our own paper is painted with
    // a gradient, and the `background` shorthand clears background-color — so sampling while `paged`
    // is on returns transparent, walks up, and picks up the desk we painted last time.
    pane.classList.remove('paged');
    const paper = opaqueBackground(flow);

    if (view.mode !== 'page') {
        setFit(1, false);
        applyZoom(); // continuous still honours the reader's own zoom
        return;
    }
    pane.classList.add('paged');

    const page = config().page;
    const geometry = sheetGeometry(page);
    // On the pane, like the zoom and for the same reason: a rebuilt document inherits its sheets.
    pane.style.setProperty('--page-w', geometry.w + 'px');
    pane.style.setProperty('--page-h', geometry.h + 'px');
    pane.style.setProperty('--page-margin', geometry.m + 'px');
    pane.style.setProperty('--page-gap', geometry.gap + 'px');
    // Paper and desk. A content theme paints the document, but in "follow app theme" the document
    // has no background of its own — so walk up until something opaque is found, or the paper would
    // come out transparent and the sheets would disappear into the desk. The desk then goes the
    // other way from the paper, so there is always an edge between the two.
    const dark = isDarkColour(paper);
    pane.style.setProperty('--page-paper', paper);
    pane.style.setProperty('--page-edge', dark ? shift(paper, 0.28) : shift(paper, -0.25));
    pane.style.setProperty('--page-desk', dark ? shift(paper, 0.14) : shift(paper, -0.45));

    const pages = layOut([...flow.children], geometry);

    // Fill out the last sheet so the paper does not stop mid-page.
    const used = flow.getBoundingClientRect().height; // zoom is pinned at 1 for this pass
    const padding = fillLastSheet(pages, geometry, used);
    if (padding > 0) flow.style.paddingBottom = padding + 'px';

    setFit(fitScale(view.fit, pane, geometry), true);
    applyZoom();
}
