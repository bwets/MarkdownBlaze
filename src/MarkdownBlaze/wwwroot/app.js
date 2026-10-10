const ASM = 'MarkdownBlaze';

function isDark() {
    const el = document.documentElement;
    if (el.classList.contains('theme-dark')) return true;
    if (el.classList.contains('theme-light')) return false;
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
}

// Strip the delimiters Markdig wraps around math (\(…\), \[…\], $…$, $$…$$).
function mdStripMathDelims(s) {
    s = s.trim();
    if (s.startsWith('\\(') && s.endsWith('\\)')) return s.slice(2, -2);
    if (s.startsWith('\\[') && s.endsWith('\\]')) return s.slice(2, -2);
    if (s.startsWith('$$') && s.endsWith('$$')) return s.slice(2, -2);
    if (s.startsWith('$') && s.endsWith('$')) return s.slice(1, -1);
    return s;
}

// Render TeX math with KaTeX. Markdig emits inline math as <span class="math">, display math
// ($$…$$) as <div class="math">, and GitHub ```math fences as <pre><code class="language-math">.
// IMPORTANT: we render *into* existing elements and never remove/replace a node. The rendered
// markup lives inside Blazor's content <div>, and swapping a node out from under Blazor corrupts
// its render tree on the next navigation (the whole UI then freezes).
function mdRenderMath() {
    if (!window.katex) return;
    // GitHub ```math fences: render display math into the existing <pre> (keep the node; hljs skips
    // it because we clear its language-math <code> child first).
    document.querySelectorAll('pre > code.language-math').forEach(function (code) {
        const pre = code.parentElement;
        if (pre.querySelector('.katex')) return; // already rendered
        try {
            window.katex.render(code.textContent.replace(/\n$/, ''), pre,
                { displayMode: true, throwOnError: false });
            pre.classList.add('math-block');
        } catch (e) { console.error('katex', e); }
    });
    document.querySelectorAll('span.math, div.math').forEach(function (el) {
        if (el.querySelector('.katex')) return; // already rendered
        try {
            window.katex.render(mdStripMathDelims(el.textContent), el, {
                displayMode: el.tagName === 'DIV',
                throwOnError: false,
            });
        } catch (e) { console.error('katex', e); }
    });
}

// Lazy asset loading: the heavy render libraries are fetched only the first time a document needs
// them, keeping startup fast. Each URL loads at most once (the promise is cached).
const mdAssets = {};
function mdLoadScript(src) {
    return mdAssets[src] || (mdAssets[src] = new Promise(function (resolve, reject) {
        const s = document.createElement('script');
        s.src = src; s.async = true;
        s.onload = resolve; s.onerror = reject;
        document.head.appendChild(s);
    }));
}
function mdLoadCss(href) {
    if (mdAssets[href]) return;
    mdAssets[href] = true;
    const l = document.createElement('link');
    l.rel = 'stylesheet'; l.href = href;
    document.head.appendChild(l);
}

// True when the *document* reads as dark. A content theme (ctl/ctd on <html>) decides that on its
// own; without one the document follows the app theme.
function mdContentIsDark() {
    const el = document.documentElement;
    if (el.classList.contains('ctd')) return true;
    if (el.classList.contains('ctl')) return false;
    return isDark();
}

function mdMermaidTheme() { return mdContentIsDark() ? 'dark' : 'default'; }

// Diagrams are part of the document, so their labels are set in the document's typeface rather
// than mermaid's own default — otherwise every diagram brings a second font onto the page.
function mdDocumentFont() {
    const flow = document.querySelector('.content.markdown-body');
    return flow ? getComputedStyle(flow).fontFamily : undefined;
}

// Mermaid lays a diagram out from what it measures in the page, and two things about this page
// throw those measurements off:
//
//   - It decides that a label has reached its maximum width — and must wrap — by testing the
//     measured width for *equality* with that maximum. Measured through the drawing's transforms
//     on a display scaled to, say, 175%, 120 comes back as 120.0000076; the test fails, and the
//     label stays on one line and is clipped by its box.
//   - The document may be zoomed, and a measurement taken inside a zoomed element comes back
//     zoomed, while the drawing is laid out in unzoomed units.
//
// So while a diagram is being drawn, measurements of its labels are taken back to unzoomed pixels
// and rounded to a thousandth of one. Nothing else is touched, and the browser's own function is
// put back as soon as the last drawing in progress has finished.
let mdSteadyDepth = 0;
let mdTrueRect = null;
function mdWithSteadyMeasures(draw) {
    if (mdSteadyDepth++ === 0) {
        mdTrueRect = Element.prototype.getBoundingClientRect;
        const measure = mdTrueRect;
        const round = v => Math.round(v * 1000) / 1000;
        Element.prototype.getBoundingClientRect = function () {
            const r = measure.call(this);
            if (!this.closest || !this.closest('foreignObject')) return r;
            const zoom = this.currentCSSZoom || 1;
            return new DOMRect(r.x, r.y, round(r.width / zoom), round(r.height / zoom));
        };
    }
    const done = () => { if (--mdSteadyDepth === 0) Element.prototype.getBoundingClientRect = mdTrueRect; };
    let result;
    try { result = Promise.resolve(draw()); } catch (e) { done(); throw e; }
    return result.finally(done);
}

function mdRunMermaid(theme) {
    try {
        const fontFamily = mdDocumentFont();
        window.mermaid.initialize({
            startOnLoad: false,
            theme: theme || mdMermaidTheme(),
            fontFamily,
            themeVariables: { fontFamily },
        });
        return mdWithSteadyMeasures(() => window.mermaid.run());
    } catch (e) { console.error('mermaid', e); return Promise.resolve(); }
}

// Re-draws every diagram in another theme. Mermaid bakes its colours into the SVG it renders (and
// replaces the diagram source with it), so the source stashed by mdInit has to be put back first.
function mdRedrawMermaid(theme) {
    const nodes = document.querySelectorAll('pre.mermaid[data-md-src]');
    if (!nodes.length || !window.mermaid) return Promise.resolve();
    nodes.forEach(function (el) {
        el.textContent = el.dataset.mdSrc;
        el.removeAttribute('data-processed');
    });
    return mdRunMermaid(theme);
}

// Render the freshly injected markdown: highlight code, render math, draw mermaid diagrams — each
// only if present, and each pulling in its library on demand.
window.mdInit = function () {
    if (document.querySelector('.math, pre > code.language-math')) {
        mdLoadCss('lib/katex/katex.min.css');
        mdLoadScript('lib/katex/katex.min.js').then(mdRenderMath).catch(e => console.error('katex load', e));
    }
    if (document.querySelector('pre code:not(.language-mermaid):not(.language-math)')) {
        mdLoadScript('lib/highlight.full.min.js')
            .then(() => { try { window.hljs.highlightAll(); } catch (e) { console.error('hljs', e); } })
            .catch(e => console.error('hljs load', e));
    }
    const diagrams = document.querySelectorAll('pre.mermaid');
    if (diagrams.length) {
        // Stash each diagram's source before mermaid overwrites it with the rendered SVG, so it can be
        // re-drawn in another theme later (printing forces the light one).
        diagrams.forEach(function (el) {
            if (el.dataset.mdSrc === undefined) el.dataset.mdSrc = el.textContent;
        });
        mdLoadScript('lib/mermaid.min.js').then(() => mdRunMermaid()).catch(e => console.error('mermaid load', e));
    }
    // Page breaks depend on the finished height of the document, so re-run once the lazy renderers
    // have had their turn as well as now.
    mdWatchPane();
    window.mdRepaginate();
    setTimeout(() => window.mdRepaginate(), 400);
    mdQueuePosition();
};

window.mdSetTheme = function (mode) {
    const el = document.documentElement;
    el.classList.remove('theme-dark', 'theme-light', 'theme-system');
    el.classList.add('theme-' + (mode || 'system').toLowerCase());
    // The document follows the app theme unless a content theme overrides it — so this changes the
    // paper too, and page view has to be rebuilt for the same reasons as a content theme change.
    mdPaginate();
    return mdRedrawMermaid(mdMermaidTheme()).then(() => mdPaginate());
};

// ---- Content themes ---------------------------------------------------------------------------
// A content theme is a `ct-<id>` class on <html> (see lib/content-themes.css), paired with a
// `ctl`/`ctd` marker so the highlight.js palette and the mermaid theme follow the document rather
// than the app chrome. Id 'auto' means "no content theme" — the document follows the app theme.
function mdApplyContentTheme(id, isDark) {
    const el = document.documentElement;
    for (const cls of [...el.classList]) if (cls.startsWith('ct-')) el.classList.remove(cls);
    el.classList.remove('ctl', 'ctd');
    if (id && id !== 'auto') el.classList.add('ct-' + id, isDark ? 'ctd' : 'ctl');
}

window.mdSetContentTheme = function (id, isDark) {
    mdApplyContentTheme(id, isDark);
    // Page view has to be rebuilt, not just repainted: the paper colour is sampled from the document,
    // and the theme's typography changes how much text fits on a sheet. Without this the sheets keep
    // the previous theme's paper — light text on white after leaving a dark theme — until something
    // else happens to repaginate, which is why a refresh appeared to "fix" it.
    mdPaginate();
    // Diagrams bake their colours into the rendered SVG, and re-drawing them changes their height,
    // so the breaks are worked out again once they have settled.
    return mdRedrawMermaid(mdMermaidTheme()).then(() => mdPaginate());
};

// What the print dialog and the printed page say about this document. The title names the print job
// and pre-fills the "Save as PDF" filename; the address is printed in the page footer, so it carries
// the document rather than a bare markdown://document/.
window.mdSetDocumentTitle = function (name, fragment) {
    document.title = name ? name + ' — MarkdownBlaze' : 'MarkdownBlaze';
    try { history.replaceState(null, '', fragment || '#'); } catch (e) { /* address bar is cosmetic */ }
};

window.mdScrollTo = function (id) {
    mdJumpedTo = id;
    mdQueuePosition();
    const el = document.getElementById(id);
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

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
// the renderers above: change what is there, never what is in the tree.
const MD_PAGE = { width: 210, height: 297, margin: 15, gap: 16 }; // A4 in mm, plus the on-screen gap
let mdView = { mode: 'continuous', fit: 'width' };

// How much the reader has zoomed the document, independent of the mode. In page view it multiplies
// the fit, so Ctrl+0 returns to a clean "full width" or "whole page".
let mdDocZoom = 1;
let mdFitScale = 1;
const MD_ZOOM_MIN = 0.4, MD_ZOOM_MAX = 4;

const mdMm = mm => Math.round(mm * 96 / 25.4);

/// The nearest background colour that actually paints something, starting at the element itself.
function mdOpaqueBackground(el) {
    for (let node = el; node; node = node.parentElement) {
        const colour = getComputedStyle(node).backgroundColor;
        const parts = colour.match(/[\d.]+/g);
        if (parts && (parts.length < 4 || parseFloat(parts[3]) > 0)) return colour;
    }
    return '#ffffff';
}

function mdIsDark(colour) {
    const p = (colour.match(/[\d.]+/g) || []).map(Number);
    if (p.length < 3) return false;
    return 0.2126 * p[0] + 0.7152 * p[1] + 0.0722 * p[2] < 128;
}

/// Moves a colour towards black or white by a fixed step — used to set the desk apart from the paper.
function mdShift(colour, amount) {
    const p = (colour.match(/[\d.]+/g) || []).map(Number);
    if (p.length < 3) return colour;
    const to = amount > 0 ? 255 : 0;
    const t = Math.abs(amount);
    const mix = c => Math.round(c + (to - c) * t);
    return `rgb(${mix(p[0])}, ${mix(p[1])}, ${mix(p[2])})`;
}

function mdPageParts() {
    const flow = document.querySelector('.content.markdown-body');
    const pane = flow && flow.closest('.content-area');
    return { flow, pane };
}

window.mdSetViewMode = function (mode, fit) {
    mdView = { mode: mode === 'page' ? 'page' : 'continuous', fit: fit === 'page' ? 'page' : 'width' };
    mdPaginate();
};

// ---- Document zoom ----------------------------------------------------------------------------
// Ctrl+wheel, Ctrl+plus/minus and pinch all zoom the *document*. The WebView's own zoom, which
// would scale the toolbar and sidebar with it, is switched off in Program.cs.
window.mdSetDocumentZoom = function (zoom, remember) {
    const next = Math.min(MD_ZOOM_MAX, Math.max(MD_ZOOM_MIN, Number(zoom) || 1));
    const changed = Math.abs(next - mdDocZoom) > 0.0001;
    mdDocZoom = next;
    mdApplyZoom();
    if (changed && remember !== false) {
        try { DotNet.invokeMethodAsync(ASM, 'OnDocumentZoomed', mdDocZoom); } catch (e) { /* not ready yet */ }
    }
};

// The zoom is kept on the pane, as --doc-zoom, and app.css hands it to whichever document element
// the pane holds. It cannot live on the document itself: Blazor rebuilds that element every time
// the file is re-read, and a zoom set on the old one is gone with it — which is how an edit made
// in another program used to snap the page back to 100% while the toolbar still said otherwise.
function mdApplyZoom() {
    const { flow, pane } = mdPageParts();
    if (!pane) return;
    const zoom = mdView.mode === 'page' ? mdFitScale * mdDocZoom : mdDocZoom;
    pane.style.setProperty('--doc-zoom', zoom);
    if (flow) flow.style.removeProperty('zoom'); // a measuring pass pins it at 1; hand it back
}

function mdNudgeZoom(factor) {
    window.mdSetDocumentZoom(mdDocZoom * factor);
}

/// One step in or out, for the toolbar's zoom buttons — the same step the wheel and keys take.
window.mdZoomBy = function (factor) { mdNudgeZoom(factor); };

// passive:false so the zoom gesture can be taken over before the WebView acts on it. A trackpad
// pinch arrives here too — Chromium reports it as a wheel event with ctrlKey set.
window.addEventListener('wheel', function (e) {
    if (!e.ctrlKey) return;
    e.preventDefault();
    mdNudgeZoom(e.deltaY < 0 ? 1.1 : 1 / 1.1);
}, { passive: false });

// Re-run whenever what is on the page, or the space it has, changes.
window.mdRepaginate = function () { if (mdView.mode === 'page') mdPaginate(); };

function mdPaginate() {
    const { flow, pane } = mdPageParts();
    if (!flow || !pane) return;

    // Always start from a clean flow: breaks and zoom from a previous pass would corrupt measuring.
    // The zoom is pinned at 1 on the element for the length of this pass (it outranks the pane's
    // --doc-zoom) and released by mdApplyZoom at the end; nothing is painted in between.
    for (const block of flow.children) block.style.removeProperty('margin-top');
    flow.style.zoom = '1';
    flow.style.removeProperty('padding-bottom');

    // Read the document's own colours with page styling switched off. Our own paper is painted with
    // a gradient, and the `background` shorthand clears background-color — so sampling while `paged`
    // is on returns transparent, walks up, and picks up the desk we painted last time.
    pane.classList.remove('paged');
    const paper = mdOpaqueBackground(flow);

    if (mdView.mode !== 'page') {
        mdFitScale = 1;
        mdApplyZoom(); // continuous still honours the reader's own zoom
        return;
    }
    pane.classList.add('paged');

    const w = mdMm(MD_PAGE.width), h = mdMm(MD_PAGE.height), m = mdMm(MD_PAGE.margin), gap = MD_PAGE.gap;
    const sheet = h + gap;              // one sheet plus the gap below it
    // On the pane, like the zoom and for the same reason: a rebuilt document inherits its sheets.
    pane.style.setProperty('--page-w', w + 'px');
    pane.style.setProperty('--page-h', h + 'px');
    pane.style.setProperty('--page-margin', m + 'px');
    pane.style.setProperty('--page-gap', gap + 'px');
    // Paper and desk. A content theme paints the document, but in "follow app theme" the document
    // has no background of its own — so walk up until something opaque is found, or the paper would
    // come out transparent and the sheets would disappear into the desk. The desk then goes the
    // other way from the paper, so there is always an edge between the two.
    const dark = mdIsDark(paper);
    pane.style.setProperty('--page-paper', paper);
    pane.style.setProperty('--page-edge', dark ? mdShift(paper, 0.28) : mdShift(paper, -0.25));
    pane.style.setProperty('--page-desk', dark ? mdShift(paper, 0.14) : mdShift(paper, -0.45));

    // Sheet k holds content between k*sheet + margin and k*sheet + height - margin. Walk the blocks
    // and, when one would cross the bottom of its sheet, push it down to the top of the next.
    let page = 0;
    const blocks = [...flow.children];
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

    // Fill out the last sheet so the paper does not stop mid-page.
    const pages = page + 1;
    const used = flow.getBoundingClientRect().height; // zoom is pinned at 1 for this pass
    const wanted = pages * sheet - gap;
    if (wanted > used) flow.style.paddingBottom = (wanted - used + m) + 'px';

    // Fit: the sheet either fills the pane's width, or sits in it whole. The reader's own zoom then
    // multiplies that, so Ctrl+0 comes back to the plain fit.
    const room = 24;
    const scale = mdView.fit === 'page'
        ? Math.min((pane.clientHeight - room) / h, (pane.clientWidth - room) / w)
        : (pane.clientWidth - room) / w;
    mdFitScale = Math.max(0.2, Math.min(scale, 3));
    mdApplyZoom();
}

// ---- Where you are ----------------------------------------------------------------------------
// The outline marks the heading of the section being read: the last heading to have passed the top
// of the pane. The mark is a data attribute rather than a class, because Blazor owns the rows'
// class and would be entitled to write it back.
let mdPositionFrame = 0;
// The heading the reader jumped to from the outline. It keeps the mark until they scroll for
// themselves: near the end of a document a heading cannot be brought to the top of the pane, and
// working the position out from the scroll offset would mark some other section than the one asked for.
let mdJumpedTo = null;
for (const type of ['wheel', 'keydown', 'pointerdown', 'touchstart'])
    window.addEventListener(type, e => { if (e.target.closest && e.target.closest('.content-area')) mdJumpedTo = null; }, { passive: true, capture: true });

function mdQueuePosition() {
    if (!mdPositionFrame) mdPositionFrame = requestAnimationFrame(mdMarkPosition);
}

function mdMarkPosition() {
    mdPositionFrame = 0;
    const { flow, pane } = mdPageParts();
    const rows = [...document.querySelectorAll('#md-tree .outline-row[data-slug]')];
    if (!flow || !pane || !rows.length) return;

    // A heading counts as passed once it crosses a line a little below the top of the pane. Over the
    // last screenful of scrolling that line slides down to the bottom of the pane: the headings at
    // the end of a document can never reach the top, and this way each still gets its turn.
    const lead = 96;
    const most = pane.scrollHeight - pane.clientHeight;
    const tail = Math.min(pane.clientHeight, most);
    const progress = tail > 0 ? Math.max(0, (pane.scrollTop - (most - tail)) / tail) : 0;
    const line = pane.getBoundingClientRect().top + lead + progress * (pane.clientHeight - lead);
    let here = null;
    for (const row of rows) {
        const slug = row.dataset.slug;
        const heading = slug && flow.querySelector('[id="' + CSS.escape(slug) + '"]');
        if (!heading) continue;
        if (heading.getBoundingClientRect().top <= line) here = row; else break;
    }
    if (mdJumpedTo) here = rows.find(row => row.dataset.slug === mdJumpedTo) || here;

    for (const row of rows) {
        if (row === here) {
            if (row.hasAttribute('data-here')) continue;
            row.setAttribute('data-here', '');
            row.scrollIntoView({ block: 'nearest' }); // keep it in sight in a long outline
        } else row.removeAttribute('data-here');
    }
}

// The pane changes size when the sidebar is dragged or the window is resized. Only a *different*
// size is worth acting on: zooming the document can itself nudge the pane, and repaginating on that
// would chase its own tail.
// Re-attached rather than set up once: leaving Settings builds a new pane, and an observer left on
// the old one watches a node that is no longer in the document.
let mdPaneObserver = null;
let mdWatchedPane = null;
let mdPaneSize = '';
let mdDocObserver = null;
let mdSidebarObserver = null;
function mdWatchPane() {
    const { pane } = mdPageParts();
    if (!pane || pane === mdWatchedPane) return;
    if (mdPaneObserver) mdPaneObserver.disconnect();
    if (mdDocObserver) mdDocObserver.disconnect();
    mdWatchedPane = pane;
    // A re-read file arrives as a brand new document element. Its page breaks are worked out here,
    // the moment it lands and before it is painted, instead of a frame later when .NET gets round
    // to calling mdInit — so the sheets do not jump when another program saves the file.
    mdDocObserver = new MutationObserver(() => mdPaginate());
    mdDocObserver.observe(pane, { childList: true });
    pane.addEventListener('scroll', mdQueuePosition, { passive: true });
    // The outline's rows are rebuilt whenever the sidebar renders — a panel opened, another document
    // — and the new rows have to be marked afresh.
    const sidebar = document.querySelector('.sidebar');
    if (sidebar) {
        if (mdSidebarObserver) mdSidebarObserver.disconnect();
        mdSidebarObserver = new MutationObserver(mdQueuePosition);
        mdSidebarObserver.observe(sidebar, { childList: true, subtree: true });
    }
    mdPaneObserver = new ResizeObserver(() => {
        const size = pane.clientWidth + 'x' + pane.clientHeight;
        if (size === mdPaneSize) return;
        mdPaneSize = size;
        window.mdRepaginate();
    });
    mdPaneObserver.observe(pane);
}

// Scrolls the sidebar tree so the given row sits at the top of the panel.
window.mdScrollTreeTo = function (containerId, rowId) {
    const container = document.getElementById(containerId);
    const row = document.getElementById(rowId);
    if (!container || !row) return;
    // Measured on screen rather than by offsetTop: the row sits inside nested panels, and offsets are
    // only relative to whichever ancestor happens to be positioned.
    container.scrollTop += row.getBoundingClientRect().top - container.getBoundingClientRect().top - 6;
};

// Reset the content viewport to the top (used when navigating to a new document).
window.mdScrollTop = function () {
    const el = document.querySelector('.content-area');
    if (el) el.scrollTop = 0;
};

// ---- Printing ---------------------------------------------------------------------------------
// Printing is driven from the print bar, which passes the content theme chosen for this print. The
// app chrome is dropped by the @media print rules; the app theme is forced light because it still
// colours the document when no content theme is active. Everything is restored afterwards from one
// snapshot of the <html> class list, which carries both the app theme and the content theme.
let mdClassBeforePrint = null;
let mdViewBeforePrint = null;

function mdSnapshotForPrint() {
    if (mdClassBeforePrint === null) mdClassBeforePrint = document.documentElement.className;
}

// Page view draws its own sheets — paper, edges, shadow — and breaks the text with margins. On paper
// that is all in the way: the printer paginates by itself, so those sheets would be printed *onto*
// its pages, borders and all. Printing therefore falls back to the plain flow, at natural size, and
// page view is put back afterwards.
function mdEnterPrintLayout() {
    if (mdViewBeforePrint) return;
    mdViewBeforePrint = { mode: mdView.mode, fit: mdView.fit, zoom: mdDocZoom };
    mdView = { mode: 'continuous', fit: mdView.fit };
    mdDocZoom = 1; // the reader's zoom is a screen comfort, not a paper size
    mdPaginate();  // clears the sheets, the breaks and the zoom in one pass
}

function mdLeavePrintLayout() {
    if (!mdViewBeforePrint) return;
    mdView = { mode: mdViewBeforePrint.mode, fit: mdViewBeforePrint.fit };
    mdDocZoom = mdViewBeforePrint.zoom;
    mdViewBeforePrint = null;
    mdPaginate();
}

function mdForceLightTheme() {
    mdSnapshotForPrint();
    mdEnterPrintLayout();
    const el = document.documentElement;
    el.classList.remove('theme-dark', 'theme-system');
    el.classList.add('theme-light');
}

function mdRestoreThemeAfterPrint() {
    if (mdClassBeforePrint === null && !mdViewBeforePrint) return;
    if (mdClassBeforePrint !== null) {
        document.documentElement.className = mdClassBeforePrint;
        mdClassBeforePrint = null;
    }
    mdLeavePrintLayout();
    return mdRedrawMermaid(mdMermaidTheme());
}

/// Prints the document under `themeId` (an id from ContentThemes; 'auto' = no content theme).
window.mdPrint = function (themeId, themeIsDark) {
    mdSnapshotForPrint();
    mdApplyContentTheme(themeId, themeIsDark);
    mdForceLightTheme();
    // mermaid.run() is async, so wait for the re-drawn diagrams before opening the print dialog.
    return mdRedrawMermaid(mdMermaidTheme()).then(function () {
        try { window.print(); } catch (e) { }
        // print() returns once the dialog is dismissed, by which point afterprint has normally
        // restored everything; this is the safety net if the WebView never fires it.
        return mdRestoreThemeAfterPrint();
    });
};

// Also covers a print started outside the app (the WebView's own shortcut/menu), where there is no
// chance to re-draw diagrams first — the CSS still switches to light.
window.addEventListener('beforeprint', mdForceLightTheme);
window.addEventListener('afterprint', mdRestoreThemeAfterPrint);

window.mdCopy = function (text) {
    try { navigator.clipboard.writeText(text); } catch (e) { }
};

// Intercept link clicks inside the rendered markdown.
document.addEventListener('click', function (e) {
    const a = e.target.closest ? e.target.closest('a') : null;
    if (!a) return;
    const nav = a.getAttribute('data-nav');
    if (nav) { e.preventDefault(); DotNet.invokeMethodAsync(ASM, 'OnLink', 'nav', nav); return; }
    const href = a.getAttribute('href') || '';
    if (href === '' || href.charAt(0) === '#') return; // in-page anchor: let the browser scroll

    // Protocol-relative (//host/…) → treat as https and open externally.
    if (href.slice(0, 2) === '//') {
        e.preventDefault();
        DotNet.invokeMethodAsync(ASM, 'OnLink', 'ext', 'https:' + href);
        return;
    }
    // Anything with a URL scheme (http:, https:, mailto:, tel:, ftp:, …) opens in the OS default
    // handler — browser, mail client, etc. — instead of navigating the WebView.
    if (/^[a-z][a-z0-9+.\-]*:/i.test(href)) {
        e.preventDefault();
        DotNet.invokeMethodAsync(ASM, 'OnLink', 'ext', href);
        return;
    }
    // Any remaining link is an unresolved relative/root path that isn't in-app navigation; don't let
    // the WebView navigate away from the app shell.
    e.preventDefault();
});

// Keyboard shortcuts -> .NET.
// ---- Guided tour ------------------------------------------------------------------------------
// A handful of tooltips, one at a time, each pointing at one part of the window. Offered on first
// run and from Settings; never forced — every step can skip the rest, and so can Esc, and the first
// step can put the whole thing off until the app is next started. The tooltip
// and the ring around its target are added to <body>, outside the element Blazor renders into.
//
// `target` may match several elements; the step points at all of them together. A step with
// `opens` is about something that is not normally on screen: the tour has it shown for that step
// and put away again afterwards.
const MD_TOUR = [
    { target: '.toolbar-left > *', place: 'bottom', title: 'Files',
      text: 'Open a file or a whole folder, reload the document, print it, or show it in your file manager.' },
    { target: '.print-bar', place: 'top', opens: 'print', title: 'Printing',
      text: 'Ctrl + P opens this bar first. Pick a look for this one print — the document changes as you choose — then press Print. Esc puts it away.' },
    { target: '#md-tree', place: 'right', title: 'Your folder',
      text: 'Every folder and document is a panel you can open and close. The document you are reading shows its headings — click one to jump to it.' },
    { target: '#tab-history', place: 'bottom', title: 'History',
      text: 'Everything you have opened, kept between launches. Contents, beside it, brings the folder back.' },
    { target: '.toolbar-middle > fluent-button', place: 'bottom', title: 'Sidebar and navigation',
      text: 'Hide or show the sidebar, and step back and forward through the documents you have read.' },
    { target: '.toolbar-right > fluent-button:first-child', place: 'bottom', title: 'View',
      text: 'Read as one continuous column, or as pages laid out the way they will print. F2 switches.' },
    { target: '.zoom-group', place: 'bottom', title: 'Zoom',
      text: 'Make the document larger or smaller without scaling the app. Ctrl + wheel does the same.' },
    { target: '.toolbar-right > fluent-button:nth-last-child(2)', place: 'bottom', title: 'Document theme',
      text: 'Give the document another look — paper, typeface and spacing. F9 steps through them.' },
    { target: '.toolbar-right > fluent-button:last-child', place: 'bottom', title: 'More',
      text: 'Keyboard shortcuts, what is new in this version, and Settings — where this tour can be started again.' },
];

let mdTour = null; // { steps, index, tip, ring, opened, canDefer } while the tour is running

/// Shows or puts away what a step asked to have opened. Only the print bar, so far.
function mdTourOpen(what, open) {
    if (what !== 'print') return;
    try { DotNet.invokeMethodAsync(ASM, 'OnTourPrintBar', open); } catch (e) { /* not ready yet */ }
}

/// The box around everything a step points at, or null when none of it is on screen (the sidebar
/// may be hidden, for one).
function mdTourRect(step) {
    let box = null;
    for (const el of document.querySelectorAll(step.target)) {
        const r = el.getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue;
        box = box ? { left: Math.min(box.left, r.left), top: Math.min(box.top, r.top),
                      right: Math.max(box.right, r.right), bottom: Math.max(box.bottom, r.bottom) }
                  : { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
    }
    return box;
}

/// `canDefer`: whether the first step offers Later. .NET stops allowing it after a couple of times.
window.mdStartTour = function (canDefer) {
    if (mdTour) return;
    const steps = MD_TOUR.filter(step => step.opens || mdTourRect(step));
    if (!steps.length) return;

    const tip = document.createElement('div');
    tip.className = 'tour-tip';
    tip.setAttribute('role', 'dialog');
    tip.setAttribute('aria-label', 'Guided tour');
    tip.innerHTML = '<div class="tour-arrow"></div><div class="tour-title"></div><div class="tour-text"></div>' +
        '<div class="tour-foot"><span class="tour-count"></span>' +
        '<button type="button" class="tour-skip">Skip tour</button>' +
        '<button type="button" class="tour-back">Back</button>' +
        '<button type="button" class="tour-next">Next</button></div>';
    const ring = document.createElement('div');
    ring.className = 'tour-ring';
    document.body.append(ring, tip);

    tip.querySelector('.tour-skip').addEventListener('click', () => mdEndTour(false));
    // On the first step there is nothing to go back to, and the button offers Later instead.
    tip.querySelector('.tour-back').addEventListener('click', () => {
        if (mdTour && mdTour.index === 0) { if (mdTour.canDefer) mdEndTour(true); }
        else mdTourGo(-1);
    });
    tip.querySelector('.tour-next').addEventListener('click', () => mdTourGo(1));

    mdTour = { steps, index: 0, tip, ring, opened: null, canDefer: !!canDefer };
    window.addEventListener('resize', mdTourResize);
    mdTourShow();
};
function mdTourResize() { mdTourShow(); }

function mdTourGo(step) {
    if (!mdTour) return;
    const next = mdTour.index + step;
    if (next >= mdTour.steps.length) { mdEndTour(false); return; }
    mdTour.index = Math.max(0, next);
    mdTourShow();
}

/// Lays the tooltip and the ring against the current step's target.
function mdTourShow(waited) {
    if (!mdTour) return;
    const { steps, index, tip, ring } = mdTour;
    const step = steps[index];

    // Put away whatever an earlier step had opened, and have this step's own thing shown. That goes
    // through .NET and a render, so the target is not there yet: look again shortly, and give up on
    // the step — not on the tour — if it never turns up.
    if (mdTour.opened && mdTour.opened !== step.opens) { mdTourOpen(mdTour.opened, false); mdTour.opened = null; }
    if (step.opens && !mdTour.opened) { mdTour.opened = step.opens; mdTourOpen(step.opens, true); }
    const box = mdTourRect(step);
    if (!box) {
        const tries = typeof waited === 'number' ? waited : 0;
        if (step.opens && tries < 40) { tip.style.visibility = ring.style.visibility = 'hidden'; setTimeout(() => mdTourShow(tries + 1), 50); }
        else mdTourGo(1); // its target is not on screen
        return;
    }
    tip.style.visibility = ring.style.visibility = 'visible';

    tip.querySelector('.tour-title').textContent = step.title;
    tip.querySelector('.tour-text').textContent = step.text;
    tip.querySelector('.tour-count').textContent = (index + 1) + ' of ' + steps.length;
    const back = tip.querySelector('.tour-back');
    back.textContent = index === 0 ? 'Later' : 'Back';
    back.title = index === 0 ? 'Show the tour the next time the app starts' : '';
    back.style.visibility = index === 0 && !mdTour.canDefer ? 'hidden' : 'visible';
    tip.querySelector('.tour-skip').style.visibility = index === steps.length - 1 ? 'hidden' : 'visible';
    tip.querySelector('.tour-next').textContent = index === steps.length - 1 ? 'Done' : 'Next';

    const pad = 4, gap = 12, edge = 8;
    ring.style.left = (box.left - pad) + 'px';
    ring.style.top = (box.top - pad) + 'px';
    ring.style.width = (box.right - box.left + 2 * pad) + 'px';
    ring.style.height = (box.bottom - box.top + 2 * pad) + 'px';

    const w = tip.offsetWidth, h = tip.offsetHeight;
    const arrow = tip.querySelector('.tour-arrow');
    const clamp = (v, lo, hi) => Math.max(lo, Math.min(v, hi));
    // Above or beside the target when asked and there is room; otherwise underneath it.
    if (step.place === 'top' && box.top - pad - gap - h >= edge) {
        const centre = (box.left + box.right) / 2;
        const left = clamp(centre - w / 2, edge, window.innerWidth - w - edge);
        tip.style.left = left + 'px';
        tip.style.top = (box.top - pad - gap - h) + 'px';
        arrow.style.top = (h - 6) + 'px';
        arrow.style.left = clamp(centre - left - 6, 14, w - 26) + 'px';
    } else if (step.place === 'right' && box.right + gap + w + edge <= window.innerWidth) {
        const top = clamp(box.top + 8, edge, window.innerHeight - h - edge);
        tip.style.left = (box.right + pad + gap) + 'px';
        tip.style.top = top + 'px';
        arrow.style.left = '-6px';
        arrow.style.top = clamp(box.top + 28 - top, 14, h - 26) + 'px';
    } else {
        const centre = (box.left + box.right) / 2;
        const left = clamp(centre - w / 2, edge, window.innerWidth - w - edge);
        tip.style.left = left + 'px';
        tip.style.top = (box.bottom + pad + gap) + 'px';
        arrow.style.top = '-6px';
        arrow.style.left = clamp(centre - left - 6, 14, w - 26) + 'px';
    }
    tip.querySelector('.tour-next').focus({ preventScroll: true });
}

/// Ends the tour. Finished or skipped, .NET is told the same thing — it has been seen; `deferred`
/// says it was put off instead, to be offered again at the next launch.
function mdEndTour(deferred) {
    if (!mdTour) return;
    if (mdTour.opened) mdTourOpen(mdTour.opened, false);
    mdTour.tip.remove();
    mdTour.ring.remove();
    mdTour = null;
    window.removeEventListener('resize', mdTourResize);
    try { DotNet.invokeMethodAsync(ASM, 'OnTourEnded', deferred === true); } catch (e) { /* not ready yet */ }
}

window.addEventListener('keydown', function (e) {
    // While the tour is up, Esc leaves it and the arrows walk it; nothing else is taken over.
    if (mdTour && !e.ctrlKey && !e.altKey) {
        if (e.key === 'Escape') { e.preventDefault(); mdEndTour(false); return; }
        if (e.key === 'ArrowRight') { e.preventDefault(); mdTourGo(1); return; }
        if (e.key === 'ArrowLeft') { e.preventDefault(); mdTourGo(-1); return; }
    }
    let combo = '';
    // Zoom keys are handled here rather than round-tripping: they only move the document.
    if (e.ctrlKey && ['0', '+', '=', '-', '_'].includes(e.key)) {
        e.preventDefault();
        if (e.key === '0') window.mdSetDocumentZoom(1);
        else mdNudgeZoom(e.key === '-' || e.key === '_' ? 1 / 1.1 : 1.1);
        return;
    }
    if (e.altKey && e.key === 'ArrowLeft') combo = 'alt+left';
    else if (e.altKey && e.key === 'ArrowRight') combo = 'alt+right';
    else if (e.key === 'F1') combo = 'f1';
    else if (e.key === 'F5') combo = 'f5';
    else if (e.key === 'F2') combo = e.shiftKey ? 'shift+f2' : 'f2';
    else if (e.key === 'F9') combo = e.shiftKey ? 'shift+f9' : 'f9';
    else if (e.key === 'F11') combo = 'f11';
    else if (e.key === '?') combo = 'help';
    else if (e.key === 'Escape') combo = 'escape';
    else if (e.ctrlKey && (e.key === 'r' || e.key === 'R')) combo = 'ctrl+r';
    else if (e.ctrlKey && (e.key === 'p' || e.key === 'P')) combo = 'ctrl+p';
    // Shift first: with Shift held the key reads as 'O', which the plain Ctrl+O test also matches.
    else if (e.ctrlKey && e.shiftKey && (e.key === 'o' || e.key === 'O')) combo = 'ctrl+shift+o';
    else if (e.ctrlKey && (e.key === 'o' || e.key === 'O')) combo = 'ctrl+o';
    else if (e.ctrlKey && (e.key === 'b' || e.key === 'B')) combo = 'ctrl+b';
    if (combo) { e.preventDefault(); DotNet.invokeMethodAsync(ASM, 'OnKey', combo); }
});

// Mouse side buttons (button 3 = Back, button 4 = Forward) -> history navigation.
// Act on mouseup, but suppress the WebView's own default on mousedown/auxclick so it doesn't
// try to navigate on its own.
window.addEventListener('mousedown', function (e) {
    if (e.button === 3 || e.button === 4) e.preventDefault();
});
window.addEventListener('auxclick', function (e) {
    if (e.button === 3 || e.button === 4) e.preventDefault();
});
window.addEventListener('mouseup', function (e) {
    if (e.button === 3) { e.preventDefault(); DotNet.invokeMethodAsync(ASM, 'OnKey', 'alt+left'); }
    else if (e.button === 4) { e.preventDefault(); DotNet.invokeMethodAsync(ASM, 'OnKey', 'alt+right'); }
});

// ---- File drag & drop -----------------------------------------------------------------------
// WebView2 does not expose the real path of a dropped file, so we recover a path only where the
// engine offers one (text/uri-list / file:// — WebKitGTK on Linux does; a File.path from
// Electron-style hosts) and otherwise fall back to reading the file's text and rendering that.
function mdDropPath(dt, file) {
    if (file && file.path) return file.path;
    let uri = '';
    try { uri = dt.getData('text/uri-list') || dt.getData('text/plain') || ''; } catch (_) { }
    uri = (uri.split('\n').find(l => l && l.charAt(0) !== '#') || '').trim();
    if (uri.slice(0, 7) === 'file://') {
        try { return decodeURIComponent(new URL(uri).pathname).replace(/^\/([A-Za-z]:)/, '$1'); }
        catch (_) { }
    }
    return '';
}

function mdHasFiles(e) {
    return !!(e.dataTransfer && Array.from(e.dataTransfer.types || []).indexOf('Files') !== -1);
}

['dragenter', 'dragover'].forEach(function (ev) {
    window.addEventListener(ev, function (e) {
        if (!mdHasFiles(e)) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
        document.body.classList.add('drag-over');
    });
});
['dragleave', 'dragend'].forEach(function (ev) {
    window.addEventListener(ev, function (e) {
        if (e.relatedTarget === null) document.body.classList.remove('drag-over');
    });
});
window.addEventListener('drop', function (e) {
    if (!mdHasFiles(e)) return;
    e.preventDefault();
    document.body.classList.remove('drag-over');
    const dt = e.dataTransfer;
    const files = Array.from(dt.files || []);
    const file = files.find(function (f) { return /\.(md|markdown|mdx|txt)$/i.test(f.name); });
    if (!file) return; // only .md / .markdown / .mdx / .txt are accepted

    const path = mdDropPath(dt, file);
    if (path) { DotNet.invokeMethodAsync(ASM, 'OnFileDrop', 'path', path, ''); return; }

    const reader = new FileReader();
    reader.onload = function () {
        DotNet.invokeMethodAsync(ASM, 'OnFileDrop', 'text', file.name, String(reader.result || ''));
    };
    reader.readAsText(file);
});

// The narrowest the sidebar may be: just wide enough that the left toolbar's buttons, which sit
// over it, still fit — so the middle toolbar beside them never drifts off the sidebar's edge.
// That is the buttons plus the header's 8px left padding, which puts the last of them flush with
// the sidebar's edge at the minimum.
function mdSidebarMin() {
    const left = document.querySelector('.toolbar-left');
    if (!left || !left.firstElementChild) return 150;
    const natural = left.lastElementChild.getBoundingClientRect().right - left.firstElementChild.getBoundingClientRect().left;
    return natural > 0 ? Math.ceil(natural) + 8 : 150;
}

// The width the sidebar opens at until the reader drags it. Worked out rather than fixed, so it is
// wider than the left toolbar whatever that toolbar grows to: the usual width, or the minimum plus
// clear room to spare, whichever is more.
const MD_SIDEBAR_DEFAULT = 280;
const MD_SIDEBAR_SPARE = 64;
function mdSidebarDefault() {
    return Math.max(MD_SIDEBAR_DEFAULT, mdSidebarMin() + MD_SIDEBAR_SPARE);
}

// Sidebar resizer (drag updates --sidebar-w; the final width is reported to .NET on release).
window.mdInitResizer = function (splitterId) {
    const splitter = document.getElementById(splitterId);
    if (!splitter || splitter._wired) return;
    splitter._wired = true;
    const root = document.documentElement;
    let resizing = false, startX = 0, startW = 0;
    const cur = () => parseInt(getComputedStyle(root).getPropertyValue('--sidebar-w')) || mdSidebarDefault();
    splitter.addEventListener('pointerdown', e => {
        resizing = true; startX = e.clientX; startW = cur();
        splitter.classList.add('dragging');
        try { splitter.setPointerCapture(e.pointerId); } catch (_) { }
    });
    splitter.addEventListener('pointermove', e => {
        if (!resizing) return;
        const w = Math.min(700, Math.max(mdSidebarMin(), startW + (e.clientX - startX)));
        root.style.setProperty('--sidebar-w', w + 'px');
    });
    const end = e => {
        if (!resizing) return;
        resizing = false;
        splitter.classList.remove('dragging');
        try { splitter.releasePointerCapture(e.pointerId); } catch (_) { }
        DotNet.invokeMethodAsync(ASM, 'OnSidebarResized', cur());
    };
    splitter.addEventListener('pointerup', end);
    splitter.addEventListener('pointercancel', end);
};

/// Applies the remembered sidebar width; with none (null), the default.
window.mdSetSidebarWidth = function (w) {
    // A width remembered from before the minimum existed is brought up to it.
    const width = w ? Math.max(mdSidebarMin(), w) : mdSidebarDefault();
    document.documentElement.style.setProperty('--sidebar-w', width + 'px');
};
