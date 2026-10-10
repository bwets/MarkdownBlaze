// The app theme and the document's theme, as the classes on <html> that the stylesheets key on.
// Redrawing what depends on them (diagrams, page sheets) is main.js's job; this module only knows the
// classes.

export function isDark() {
    const el = document.documentElement;
    if (el.classList.contains('theme-dark')) return true;
    if (el.classList.contains('theme-light')) return false;
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
}

// True when the *document* reads as dark. A content theme (ctl/ctd on <html>) decides that on its
// own; without one the document follows the app theme.
export function contentIsDark() {
    const el = document.documentElement;
    if (el.classList.contains('ctd')) return true;
    if (el.classList.contains('ctl')) return false;
    return isDark();
}

export function mermaidTheme() { return contentIsDark() ? 'dark' : 'default'; }

export function setTheme(mode) {
    const el = document.documentElement;
    el.classList.remove('theme-dark', 'theme-light', 'theme-system');
    el.classList.add('theme-' + (mode || 'system').toLowerCase());
}

// ---- Content themes ---------------------------------------------------------------------------
// A content theme is a `ct-<id>` class on <html> (see css/content-themes.css), paired with a
// `ctl`/`ctd` marker so the highlight.js palette and the mermaid theme follow the document rather
// than the app chrome. Id 'auto' means "no content theme" — the document follows the app theme.
export function applyContentTheme(id, dark) {
    const el = document.documentElement;
    for (const cls of [...el.classList]) if (cls.startsWith('ct-')) el.classList.remove(cls);
    el.classList.remove('ctl', 'ctd');
    if (id && id !== 'auto') el.classList.add('ct-' + id, dark ? 'ctd' : 'ctl');
}
