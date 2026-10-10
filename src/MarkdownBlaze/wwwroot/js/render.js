// Everything that turns the freshly injected markup into what the reader sees: syntax highlighting
// (highlight.js), math (KaTeX) and diagrams (Mermaid). Each library is fetched only the first time a
// document needs it, so none of them is in the way of startup.
import { mermaidTheme } from './theme.js';

// Strip the delimiters Markdig wraps around math (\(…\), \[…\], $…$, $$…$$).
export function stripMathDelims(s) {
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
export function renderMath() {
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
            window.katex.render(stripMathDelims(el.textContent), el, {
                displayMode: el.tagName === 'DIV',
                throwOnError: false,
            });
        } catch (e) { console.error('katex', e); }
    });
}

// Lazy asset loading: the heavy render libraries are fetched only the first time a document needs
// them, keeping startup fast. Each URL loads at most once (the promise is cached).
const assets = new Map();
export function loadScript(src) {
    if (!assets.has(src)) {
        assets.set(src, new Promise(function (resolve, reject) {
            const s = document.createElement('script');
            s.src = src; s.async = true;
            s.onload = resolve; s.onerror = reject;
            document.head.appendChild(s);
        }));
    }
    return assets.get(src);
}
export function loadCss(href) {
    if (assets.has(href)) return;
    assets.set(href, true);
    const l = document.createElement('link');
    l.rel = 'stylesheet'; l.href = href;
    document.head.appendChild(l);
}

// Diagrams are part of the document, so their labels are set in the document's typeface rather
// than mermaid's own default — otherwise every diagram brings a second font onto the page.
function documentFont() {
    const flow = document.querySelector('.content.markdown-body');
    return flow ? getComputedStyle(flow).fontFamily : undefined;
}

// ---- Steady measurements for Mermaid -----------------------------------------------------------
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
//
// This replaces a function of the page's own DOM for the length of a drawing, so it is kept to this
// one place, and it cannot be left in place by accident: a drawing that never finishes (the library
// hangs, a promise is dropped) has the browser's function put back by a timer all the same.
// Decided to keep it: Mermaid's `htmlLabels: false` would make the patch unnecessary, but it draws
// every label as SVG text, which changes the layout of every diagram (edge-label backgrounds stop
// lining up with their text and the nodes change size).
const STEADY_TIMEOUT_MS = 15000;
let steadyDepth = 0;
let trueRect = null;
let steadyTimer = 0;

const round = v => Math.round(v * 1000) / 1000;

function steadyRect(measure) {
    return function () {
        const r = measure.call(this);
        if (!this.closest || !this.closest('foreignObject')) return r;
        const zoom = this.currentCSSZoom || 1;
        return new DOMRect(r.x, r.y, round(r.width / zoom), round(r.height / zoom));
    };
}

/// Puts the browser's own function back, whatever is still drawing. Safe to call at any time.
export function restoreMeasures() {
    clearTimeout(steadyTimer);
    steadyDepth = 0;
    if (trueRect) {
        Element.prototype.getBoundingClientRect = trueRect;
        trueRect = null;
    }
}

export function withSteadyMeasures(draw, timeoutMs = STEADY_TIMEOUT_MS) {
    if (steadyDepth++ === 0) {
        trueRect = Element.prototype.getBoundingClientRect;
        Element.prototype.getBoundingClientRect = steadyRect(trueRect);
    }
    clearTimeout(steadyTimer);
    steadyTimer = setTimeout(restoreMeasures, timeoutMs);
    const done = () => { if (steadyDepth > 0 && --steadyDepth === 0) restoreMeasures(); };
    let result;
    try { result = Promise.resolve(draw()); } catch (e) { done(); throw e; }
    return result.finally(done);
}

export function runMermaid(theme) {
    try {
        const fontFamily = documentFont();
        window.mermaid.initialize({
            startOnLoad: false,
            theme: theme || mermaidTheme(),
            fontFamily,
            themeVariables: { fontFamily },
        });
        return withSteadyMeasures(() => window.mermaid.run());
    } catch (e) { console.error('mermaid', e); return Promise.resolve(); }
}

// Re-draws every diagram in another theme. Mermaid bakes its colours into the SVG it renders (and
// replaces the diagram source with it), so the source stashed by renderDocument has to be put back first.
export function redrawMermaid(theme) {
    const nodes = document.querySelectorAll('pre.mermaid[data-md-src]');
    if (!nodes.length || !window.mermaid) return Promise.resolve();
    nodes.forEach(function (el) {
        el.textContent = el.dataset.mdSrc;
        el.removeAttribute('data-processed');
    });
    return runMermaid(theme);
}

// Render the freshly injected markdown: highlight code, render math, draw mermaid diagrams — each
// only if present, and each pulling in its library on demand. Resolves when the last of them is done
// (a library that cannot be loaded is logged and does not hold the others up).
export function renderDocument() {
    const jobs = [];
    if (document.querySelector('.math, pre > code.language-math')) {
        loadCss('vendor/katex/katex.min.css');
        jobs.push(loadScript('vendor/katex/katex.min.js').then(renderMath).catch(e => console.error('katex load', e)));
    }
    if (document.querySelector('pre code:not(.language-mermaid):not(.language-math)')) {
        jobs.push(loadScript('vendor/highlight/highlight.min.js')
            .then(() => { try { window.hljs.highlightAll(); } catch (e) { console.error('hljs', e); } })
            .catch(e => console.error('hljs load', e)));
    }
    const diagrams = document.querySelectorAll('pre.mermaid');
    if (diagrams.length) {
        // Stash each diagram's source before mermaid overwrites it with the rendered SVG, so it can be
        // re-drawn in another theme later (printing forces the light one).
        diagrams.forEach(function (el) {
            if (el.dataset.mdSrc === undefined) el.dataset.mdSrc = el.textContent;
        });
        jobs.push(loadScript('vendor/mermaid/mermaid.min.js').then(() => runMermaid()).catch(e => console.error('mermaid load', e)));
    }
    return Promise.all(jobs);
}
