// The shape of the screen the page is on: how narrow it is, whether there is a keyboard, which edges the system's
// own bars cover, and whether the app is dark. The stylesheet changes the layout by itself, from the same queries
// (css/app.css, one @media block for each); what is here is what .NET has to be told, because it builds the menus
// and the tooltips, and what the host's window needs, because it colours the system bars.
import { call } from './bridge.js';
import { isDark } from './theme.js';

/// A narrow window: a phone held upright, a desktop window dragged narrow, or a phone on its side (short, and not
/// as wide as a desktop window: a sidebar beside the document would leave no room for the document). The sidebar
/// becomes a drawer and the toolbar keeps its essentials. A tablet, however it is held, is wide enough and tall
/// enough to keep the desktop layout. css/app.css has a copy of this query, which a test keeps in step.
export const NARROW_QUERY = '(max-width: 720px), (max-height: 500px) and (max-width: 1023px)';

/// No keyboard and no mouse to name keys for: the device's main input is a finger. A tablet with a mouse or
/// a keyboard attached stops matching, and the keys are named again. css/app.css has a copy of this query too.
export const NO_KEYBOARD_QUERY = '(hover: none) and (pointer: coarse)';

/// Whether the host says the screen is a touch screen (`<html data-touch>`, in the Android host's page). A desktop
/// host does not: a Windows machine with a touch screen reports a coarse pointer too, and its window is to stay
/// as it was, keys and all.
export const isTouchHost = () => document.documentElement.hasAttribute('data-touch');

export const isNarrow = () => window.matchMedia(NARROW_QUERY).matches;
export const hasNoKeyboard = () => isTouchHost() && window.matchMedia(NO_KEYBOARD_QUERY).matches;

/// Tells .NET what the screen is like now, and again whenever it changes (a turn of the device, a window
/// resized across the breakpoint, a mouse plugged in).
export function installLayout(signal) {
    const report = () => call('OnLayout', isNarrow(), hasNoKeyboard());
    for (const query of [NARROW_QUERY, NO_KEYBOARD_QUERY]) {
        window.matchMedia(query).addEventListener('change', report, { signal });
    }
    // "Follow the system" turns dark by itself.
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', reportAppearance, { signal });
    report();
    reportAppearance();
}

/// The host colours the system bars' icons to suit the app, so it is told which it is. Called when the
/// theme is set, and when the system's own changes.
export function reportAppearance() { call('OnAppearance', isDark()); }

/// A CSS length for an inset the host sent: a whole or fractional number of pixels, never less than none and
/// never a number that is not one.
export function insetPx(value) {
    const px = Number(value);
    return Number.isFinite(px) && px > 0 ? Math.round(px * 100) / 100 + 'px' : '0px';
}

/// How much of each edge the system covers (the status bar, the gesture bar, a cutout), as `--safe-*` on <html>,
/// which the stylesheet keeps the toolbar, the sidebar and the bars out of. The page cannot learn this
/// itself in every WebView, so the host says (and a host with nothing to say leaves the stylesheet's `env()`).
export function setInsets(top, right, bottom, left) {
    const style = document.documentElement.style;
    style.setProperty('--safe-top', insetPx(top));
    style.setProperty('--safe-right', insetPx(right));
    style.setProperty('--safe-bottom', insetPx(bottom));
    style.setProperty('--safe-left', insetPx(left));
}
