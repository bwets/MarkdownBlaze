// Input the page turns into requests to .NET: keyboard shortcuts, the mouse's side buttons, and
// files dropped onto the window. Each of them is only a request — .NET decides what happens, and checks
// whatever it was told (a dropped path, for one) before it acts on it.
import { call, config } from './bridge.js';
import { handleTourKey } from './tour.js';
import { handleZoomKey } from './zoom.js';

// ---- Keyboard shortcuts -------------------------------------------------------------------------
// Which keys mean something is decided in .NET, where the command registry is also what the shortcuts
// overlay is drawn from: config.shortcuts is the list of key combinations the registry knows, and
// only those are taken over and reported. Everything else is left to the WebView.

/// The name a combination uses for a key: lower case, arrows without "Arrow".
export function keyName(key) {
    if (key.length === 1) return key.toLowerCase();
    return key.replace(/^Arrow/, '').toLowerCase();
}

/// The combination a key event stands for, as the registry spells it ("ctrl+shift+o", "alt+left",
/// "f2", "shift+f2", "?", "escape"), or '' when it is not one of `known`.
/// A modifier that makes no difference is overlooked, as the shortcuts have always done: Shift+F5 is
/// F5, and the key that is not a letter or a digit (F-keys, Esc, "?") is itself under any modifiers.
export function comboFor(e, known) {
    const key = keyName(e.key);
    const mods = (e.ctrlKey ? 'ctrl+' : '') + (e.altKey ? 'alt+' : '');
    const candidates = [mods + (e.shiftKey ? 'shift+' : '') + key, mods + key];
    if (!/^[a-z0-9]$/.test(key)) candidates.push(key);
    return candidates.find(c => known.has(c)) || '';
}

/// Whether an element takes typed text: a text box, a text area, a select, or anything marked
/// contenteditable. A button, a checkbox or a range do not — a key press on them is not typing.
export function isEditable(el) {
    if (!el || el.nodeType !== 1) return false;
    if (el.isContentEditable) return true;
    const tag = el.tagName;
    if (tag === 'TEXTAREA' || tag === 'SELECT') return true;
    if (tag === 'INPUT') return !/^(button|checkbox|radio|submit|reset|image|file|range|color)$/i.test(el.type || '');
    return false;
}

/// Whether this key press is someone typing a character into a field. Such a key is the field's, not a
/// shortcut's: "?" must put a question mark in a text box, not open the help. Keys that cannot type
/// (F-keys, Esc) and anything with Ctrl or Alt held are still shortcuts there. The target is looked up
/// through the shadow roots of the components that wrap their input (Fluent's), which hide it from `target`.
export function isTyping(e) {
    if (e.ctrlKey || e.altKey || e.metaKey || typeof e.key !== 'string' || e.key.length !== 1) return false;
    const path = typeof e.composedPath === 'function' ? e.composedPath() : [];
    return isEditable(path[0] || e.target);
}

export function installKeys(signal) {
    const known = new Set(config().shortcuts);
    window.addEventListener('keydown', function (e) {
        // The tour comes first, then the zoom keys, which only move the document and so are not
        // worth a round trip.
        if (handleTourKey(e) || handleZoomKey(e)) return;
        if (isTyping(e)) return;
        const combo = comboFor(e, known);
        if (combo) { e.preventDefault(); call('OnKey', combo); }
    }, { signal });
}

// ---- Mouse side buttons -------------------------------------------------------------------------
// Button 3 = Back, button 4 = Forward -> history navigation. Act on mouseup, but suppress the
// WebView's own default on mousedown/auxclick so it doesn't try to navigate on its own.
export function installMouseButtons(signal) {
    window.addEventListener('mousedown', function (e) {
        if (e.button === 3 || e.button === 4) e.preventDefault();
    }, { signal });
    window.addEventListener('auxclick', function (e) {
        if (e.button === 3 || e.button === 4) e.preventDefault();
    }, { signal });
    window.addEventListener('mouseup', function (e) {
        if (e.button === 3) { e.preventDefault(); call('OnKey', 'alt+left'); }
        else if (e.button === 4) { e.preventDefault(); call('OnKey', 'alt+right'); }
    }, { signal });
}

// ---- File drag & drop -----------------------------------------------------------------------
// WebView2 does not expose the real path of a dropped file, so we recover a path only where the
// engine offers one (text/uri-list / file:// — WebKitGTK on Linux does; a File.path from
// Electron-style hosts) and otherwise fall back to reading the file's text and rendering that.
export function dropPath(dt, file) {
    if (file && file.path) return file.path;
    let uri = '';
    try { uri = dt.getData('text/uri-list') || dt.getData('text/plain') || ''; } catch (e) { console.debug('drop uri', e); }
    uri = (uri.split('\n').find(l => l && l.charAt(0) !== '#') || '').trim();
    if (uri.slice(0, 7) === 'file://') {
        try { return decodeURIComponent(new URL(uri).pathname).replace(/^\/([A-Za-z]:)/, '$1'); }
        catch (e) { console.debug('drop path', e); }
    }
    return '';
}

function hasFiles(e) {
    return !!(e.dataTransfer && Array.from(e.dataTransfer.types || []).indexOf('Files') !== -1);
}

/// Whether a dropped file's name has one of the extensions the app opens (config.extensions, which
/// is the list .NET opens by).
export function isSupportedName(name, extensions) {
    const lower = name.toLowerCase();
    return extensions.some(ext => lower.endsWith(ext.toLowerCase()));
}

export function installDragDrop(signal) {
    ['dragenter', 'dragover'].forEach(function (ev) {
        window.addEventListener(ev, function (e) {
            if (!hasFiles(e)) return;
            e.preventDefault();
            e.dataTransfer.dropEffect = 'copy';
            document.body.classList.add('drag-over');
        }, { signal });
    });
    ['dragleave', 'dragend'].forEach(function (ev) {
        window.addEventListener(ev, function (e) {
            if (e.relatedTarget === null) document.body.classList.remove('drag-over');
        }, { signal });
    });
    window.addEventListener('drop', function (e) {
        if (!hasFiles(e)) return;
        e.preventDefault();
        document.body.classList.remove('drag-over');
        const dt = e.dataTransfer;
        const files = Array.from(dt.files || []);
        const extensions = config().extensions;
        const file = files.find(f => isSupportedName(f.name, extensions));
        if (!file) return; // only the document types the app opens are accepted

        const path = dropPath(dt, file);
        if (path) { call('OnFileDrop', 'path', path, ''); return; }

        const reader = new FileReader();
        reader.onload = function () {
            call('OnFileDrop', 'text', file.name, String(reader.result || ''));
        };
        reader.readAsText(file);
    }, { signal });
}
