// Link clicks inside the rendered document. The document is not trusted, so what a click may do is
// decided here and then once more in .NET (NavigationService.IsFollowable and ExternalLinks): a link
// either scrolls the page, goes to another document of the folder, goes to the operating system's
// handler for a web or mail address, or does nothing at all.
import { call, config } from './bridge.js';

/// What a link with this `href` should do, given the schemes that may be handed to the operating
/// system: `{ kind: 'page' }` (an in-page anchor; let the browser scroll), `{ kind: 'ext', url }`,
/// or `{ kind: 'none' }` (swallow the click).
export function classifyLink(href, schemes) {
    href = href || '';
    if (href === '' || href.charAt(0) === '#') return { kind: 'page' };

    // Protocol-relative (//host/…) → treat as https and open externally.
    if (href.slice(0, 2) === '//') return { kind: 'ext', url: 'https:' + href };

    // A URL with an allowed scheme (http:, https:, mailto:, tel:, ftp:) opens in the OS default
    // handler — browser, mail client, etc. — instead of navigating the WebView. Any other scheme
    // (javascript:, file:, data:, vbscript:, and a drive letter such as C: which looks like one) is
    // swallowed: the click does nothing.
    const scheme = /^([a-z][a-z0-9+.\-]*):/i.exec(href);
    if (scheme) {
        return schemes.includes(scheme[1].toLowerCase()) ? { kind: 'ext', url: href } : { kind: 'none' };
    }

    // Any remaining link is an unresolved relative/root path that isn't in-app navigation; don't let
    // the WebView navigate away from the app shell.
    return { kind: 'none' };
}

// Intercept link clicks inside the rendered markdown.
export function installLinkClicks(signal) {
    document.addEventListener('click', function (e) {
        const a = e.target.closest ? e.target.closest('a') : null;
        if (!a) return;
        // A link the renderer deliberately made inert (e.g. an absolute local path): do nothing at all.
        if (a.hasAttribute('data-md-blocked')) { e.preventDefault(); return; }
        // data-nav is written by the renderer alone (the sanitizer removes any an author wrote), and
        // .NET checks the target again before it opens anything.
        const nav = a.getAttribute('data-nav');
        if (nav) { e.preventDefault(); call('OnLink', 'nav', nav); return; }

        const link = classifyLink(a.getAttribute('href'), config().linkSchemes);
        if (link.kind === 'page') return;
        e.preventDefault();
        if (link.kind === 'ext') call('OnLink', 'ext', link.url);
    }, { signal });
}
