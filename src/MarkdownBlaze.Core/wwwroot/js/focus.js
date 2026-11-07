// Keyboard focus for the popups the app draws itself: the context menu and the two dialogs.
//
// A popup that opens must take the focus into itself, keep it there while it is open (a dialog does
// not let Tab walk out into the page behind it; a menu is walked with the arrow keys), and give it
// back to what had it when the popup closes. The browser does none of that for a <div>.
//
// Imported by the FocusScope component, one scope per open popup. The scopes nest like the popups do:
// the innermost one gets the keys.

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), '
    + 'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const scopes = []; // { el, kind, opener, dismiss, onKey } — the open popups, innermost last

/// What can take the focus inside `el`, in tab order (menu items are reachable by script only).
export function focusables(el, selector = FOCUSABLE) {
    return [...el.querySelectorAll(selector)].filter(e => e.getClientRects().length > 0);
}

/// The element after (`step` 1) or before (`step` -1) `current` in `items`, wrapping round.
export function neighbour(items, current, step) {
    if (!items.length) return null;
    const at = items.indexOf(current);
    if (at < 0) return step > 0 ? items[0] : items[items.length - 1];
    return items[(at + step + items.length) % items.length];
}

function onKey(scope, e) {
    if (scope !== scopes[scopes.length - 1]) return;
    const { el, kind } = scope;

    if (kind === 'menu') {
        const items = focusables(el, '[role^="menuitem"]');
        const at = items.indexOf(document.activeElement);
        let next = null;
        if (e.key === 'ArrowDown') next = neighbour(items, items[at], 1);
        else if (e.key === 'ArrowUp') next = neighbour(items, items[at], -1);
        else if (e.key === 'Home') next = items[0];
        else if (e.key === 'End') next = items[items.length - 1];
        else if (e.key === 'Tab') {
            // Tab leaves a menu, and the menu closes with it.
            e.preventDefault();
            scope.dismiss?.invokeMethodAsync('Dismiss').catch(err => console.warn('dismiss', err));
            return;
        }
        if (next) { e.preventDefault(); next.focus(); }
        return;
    }

    // A dialog: Tab goes round inside it.
    if (e.key === 'Tab') {
        const items = focusables(el);
        if (!items.length) { e.preventDefault(); el.focus(); return; }
        const first = items[0], last = items[items.length - 1];
        if (e.shiftKey && (document.activeElement === first || document.activeElement === el)) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
        else if (!el.contains(document.activeElement)) { e.preventDefault(); first.focus(); }
    }
}

/// A popup has opened: remember what had the focus, take the focus into it, and handle its keys.
/// `kind` is 'menu' or 'dialog'; `dismiss` is a DotNetObjectReference with a `Dismiss` method, which
/// a menu uses to close itself when Tab leaves it.
export function enter(el, kind, dismiss) {
    const scope = { el, kind, opener: document.activeElement, dismiss, onKey: null };
    scope.onKey = e => onKey(scope, e);
    el.addEventListener('keydown', scope.onKey);
    scopes.push(scope);

    const first = kind === 'menu' ? focusables(el, '[role^="menuitem"]')[0] : focusables(el)[0];
    (first || el).focus({ preventScroll: true });
}

/// The popup has closed: hand the focus back to what had it before, unless the reader has since
/// put it somewhere else themselves.
export function leave(el) {
    const at = scopes.findIndex(s => s.el === el);
    if (at < 0) return;
    const [scope] = scopes.splice(at, 1);
    el.removeEventListener('keydown', scope.onKey);

    const active = document.activeElement;
    const lost = !active || active === document.body || el.contains(active) || !active.isConnected;
    const { opener } = scope;
    if (lost && opener && opener.isConnected && typeof opener.focus === 'function') opener.focus({ preventScroll: true });
}
