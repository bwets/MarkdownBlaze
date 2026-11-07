// Link clicks inside the rendered document. The document is not trusted, so what a click may do is
// decided here and then once more in .NET (NavigationService.IsFollowable and ExternalLinks): a link
// either scrolls the page, goes to another document of the folder, goes to the operating system's
// handler for a web or mail address, or does nothing at all.
import { call, config } from './bridge.js';

const XLINK_NAMESPACE = 'http://www.w3.org/1999/xlink';

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

/// The address an anchor points at, whichever kind of anchor it is. An HTML anchor has `href`. An anchor
/// inside an SVG — the kind Mermaid makes for `click A "https://example.com"`, even in strict mode — has
/// `xlink:href` (SVG 1.1) or `href` (SVG 2), and on an SVGAElement the property `href` is not a string
/// but an animated one, whose `baseVal` is the address. All of them have to reach classifyLink: an SVG
/// anchor whose address is read as empty would be taken for an in-page anchor and left to navigate.
export function addressOf(a) {
    const plain = a.getAttribute('href');
    if (plain !== null) return plain;

    if (typeof a.getAttributeNS === 'function') {
        const xlink = a.getAttributeNS(XLINK_NAMESPACE, 'href');
        if (xlink !== null) return xlink;
    }

    const animated = a.href;
    return animated && typeof animated === 'object' && typeof animated.baseVal === 'string' ? animated.baseVal : null;
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

        const link = classifyLink(addressOf(a), config().linkSchemes);
        if (link.kind === 'page') return;
        e.preventDefault();
        if (link.kind === 'ext') call('OnLink', 'ext', link.url);
    }, { signal });
}

// ---- Where a link goes, before it is clicked ----------------------------------------------------------
//
// A browser shows the address of a link in a strip at the bottom of its window while the pointer is over
// it, because the text of a link says nothing about where it goes: [https://good.example](https://evil.invalid)
// is one line of Markdown. This app opens a link in the reader's browser, so the same strip is shown for a
// link that would do that — with the address from classifyLink, the one that would actually be opened, and
// never the text of the link or its `title`, which are the author's to write. The address is shown the way
// the URL parser writes it, so a host made of look-alike letters from another alphabet appears as the
// ASCII "xn--…" it really is. A link that stays in the app, scrolls the page or does nothing shows nothing.
// Keyboard focus shows it too, for the reader who does not use a pointer.

/// The address a click on `a` would open in the reader's browser, as it should be shown, or null when a
/// click would not open one.
export function externalAddressOf(a, schemes) {
    if (!a || a.hasAttribute('data-md-blocked') || a.getAttribute('data-nav')) return null;

    const link = classifyLink(addressOf(a), schemes);
    if (link.kind !== 'ext') return null;

    try {
        return new URL(link.url).href;
    } catch {
        return link.url; // not an address the URL parser accepts; .NET will not open it either, but say what it says
    }
}

let statusElement = null;

// Made when it is first needed, outside <app> (Blazor owns everything in there and would take it for a
// stranger), and made again if whatever holds it has been emptied.
function statusStrip() {
    if (!statusElement || !statusElement.isConnected) {
        statusElement = document.createElement('div');
        statusElement.className = 'link-status';
        statusElement.setAttribute('aria-hidden', 'true'); // the link itself is what a screen reader reads
        statusElement.hidden = true;
        document.body.append(statusElement);
    }
    return statusElement;
}

function showLinkStatus(target) {
    const anchor = target && target.closest ? target.closest('a') : null;
    const address = anchor ? externalAddressOf(anchor, config().linkSchemes) : null;
    const strip = statusStrip();
    strip.textContent = address ?? '';
    strip.hidden = address === null;
}

export function hideLinkStatus() {
    if (statusElement) { statusElement.textContent = ''; statusElement.hidden = true; }
}

export function installLinkStatus(signal) {
    document.addEventListener('mouseover', e => showLinkStatus(e.target), { signal });
    document.addEventListener('mouseout', e => { if (!e.relatedTarget) hideLinkStatus(); }, { signal }); // the pointer left the window
    document.addEventListener('focusin', e => showLinkStatus(e.target), { signal });
    document.addEventListener('focusout', hideLinkStatus, { signal });
    document.addEventListener('click', hideLinkStatus, { signal, capture: true });
}
