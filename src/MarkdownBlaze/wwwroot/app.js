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

function mdRunMermaid() {
    try {
        window.mermaid.initialize({ startOnLoad: false, theme: isDark() ? 'dark' : 'default' });
        window.mermaid.run();
    } catch (e) { console.error('mermaid', e); }
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
    if (document.querySelector('pre.mermaid')) {
        mdLoadScript('lib/mermaid.min.js').then(mdRunMermaid).catch(e => console.error('mermaid load', e));
    }
};

window.mdSetTheme = function (mode) {
    const el = document.documentElement;
    el.classList.remove('theme-dark', 'theme-light', 'theme-system');
    el.classList.add('theme-' + (mode || 'system').toLowerCase());
};

window.mdScrollTo = function (id) {
    const el = document.getElementById(id);
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

// Reset the content viewport to the top (used when navigating to a new document).
window.mdScrollTop = function () {
    const el = document.querySelector('.content-area');
    if (el) el.scrollTop = 0;
};

window.mdPrint = function () { try { window.print(); } catch (e) { } };

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
window.addEventListener('keydown', function (e) {
    let combo = '';
    if (e.altKey && e.key === 'ArrowLeft') combo = 'alt+left';
    else if (e.altKey && e.key === 'ArrowRight') combo = 'alt+right';
    else if (e.key === 'F1') combo = 'f1';
    else if (e.key === 'F5') combo = 'f5';
    else if (e.key === 'F11') combo = 'f11';
    else if (e.key === '?') combo = 'help';
    else if (e.key === 'Escape') combo = 'escape';
    else if (e.ctrlKey && (e.key === 'r' || e.key === 'R')) combo = 'ctrl+r';
    else if (e.ctrlKey && (e.key === 'p' || e.key === 'P')) combo = 'ctrl+p';
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

// Sidebar resizer (drag updates --sidebar-w; the final width is reported to .NET on release).
window.mdInitResizer = function (splitterId) {
    const splitter = document.getElementById(splitterId);
    if (!splitter || splitter._wired) return;
    splitter._wired = true;
    const root = document.documentElement;
    let resizing = false, startX = 0, startW = 0;
    const cur = () => parseInt(getComputedStyle(root).getPropertyValue('--sidebar-w')) || 280;
    splitter.addEventListener('pointerdown', e => {
        resizing = true; startX = e.clientX; startW = cur();
        splitter.classList.add('dragging');
        try { splitter.setPointerCapture(e.pointerId); } catch (_) { }
    });
    splitter.addEventListener('pointermove', e => {
        if (!resizing) return;
        const w = Math.min(700, Math.max(150, startW + (e.clientX - startX)));
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

window.mdSetSidebarWidth = function (w) {
    document.documentElement.style.setProperty('--sidebar-w', w + 'px');
};
