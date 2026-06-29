const ASM = 'MarkdownBlaze';

function isDark() {
    const el = document.documentElement;
    if (el.classList.contains('theme-dark')) return true;
    if (el.classList.contains('theme-light')) return false;
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
}

// Run syntax highlighting + mermaid over the freshly rendered markdown.
window.mdInit = function () {
    try { if (window.hljs) window.hljs.highlightAll(); } catch (e) { console.error('hljs', e); }
    try {
        if (window.mermaid) {
            window.mermaid.initialize({ startOnLoad: false, theme: isDark() ? 'dark' : 'default' });
            window.mermaid.run();
        }
    } catch (e) { console.error('mermaid', e); }
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
    if (href === '' || href.charAt(0) === '#') return; // in-page anchor
    if (/^(https?:|mailto:)/i.test(href)) {
        e.preventDefault();
        DotNet.invokeMethodAsync(ASM, 'OnLink', 'ext', href);
    }
});

// Keyboard shortcuts -> .NET.
window.addEventListener('keydown', function (e) {
    let combo = '';
    if (e.altKey && e.key === 'ArrowLeft') combo = 'alt+left';
    else if (e.altKey && e.key === 'ArrowRight') combo = 'alt+right';
    else if (e.key === 'F5') combo = 'f5';
    else if (e.ctrlKey && (e.key === 'r' || e.key === 'R')) combo = 'ctrl+r';
    else if (e.ctrlKey && (e.key === 'p' || e.key === 'P')) combo = 'ctrl+p';
    else if (e.ctrlKey && (e.key === 'b' || e.key === 'B')) combo = 'ctrl+b';
    if (combo) { e.preventDefault(); DotNet.invokeMethodAsync(ASM, 'OnKey', combo); }
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
