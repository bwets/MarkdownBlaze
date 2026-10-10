// ---- Where you are ----------------------------------------------------------------------------
// The outline marks the heading of the section being read: the last heading to have passed the top
// of the pane. The mark is a data attribute rather than a class, because Blazor owns the rows'
// class and would be entitled to write it back.
import { pageParts } from './dom.js';
import { paginate, repaginate } from './pagination.js';

let positionFrame = 0;
// The heading the reader jumped to from the outline. It keeps the mark until they scroll for
// themselves: near the end of a document a heading cannot be brought to the top of the pane, and
// working the position out from the scroll offset would mark some other section than the one asked for.
let jumpedTo = null;

export function installPositionListeners(signal) {
    for (const type of ['wheel', 'keydown', 'pointerdown', 'touchstart'])
        window.addEventListener(type, e => { if (e.target.closest && e.target.closest('.content-area')) jumpedTo = null; }, { passive: true, capture: true, signal });
}

export function queuePosition() {
    if (!positionFrame) positionFrame = requestAnimationFrame(markPosition);
}

/// The heading of the section at `line`: the last one whose top has crossed it. `tops` are the
/// headings' tops in document order (null for a heading that is not on the page).
export function sectionAt(tops, line) {
    let here = -1;
    for (let i = 0; i < tops.length; i++) {
        if (tops[i] === null) continue;
        if (tops[i] <= line) here = i; else break;
    }
    return here;
}

function markPosition() {
    positionFrame = 0;
    const { flow, pane } = pageParts();
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
    const tops = rows.map(row => {
        const slug = row.dataset.slug;
        const heading = slug && flow.querySelector('[id="' + CSS.escape(slug) + '"]');
        return heading ? heading.getBoundingClientRect().top : null;
    });
    let here = rows[sectionAt(tops, line)] || null;
    if (jumpedTo) here = rows.find(row => row.dataset.slug === jumpedTo) || here;

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
let paneObserver = null;
let watchedPane = null;
let paneSize = '';
let docObserver = null;
let sidebarObserver = null;
export function watchPane() {
    const { pane } = pageParts();
    if (!pane || pane === watchedPane) return;
    if (paneObserver) paneObserver.disconnect();
    if (docObserver) docObserver.disconnect();
    watchedPane = pane;
    // A re-read file arrives as a brand new document element. Its page breaks are worked out here,
    // the moment it lands and before it is painted, instead of a frame later when .NET gets round
    // to asking for the render — so the sheets do not jump when another program saves the file.
    docObserver = new MutationObserver(() => paginate());
    docObserver.observe(pane, { childList: true });
    pane.addEventListener('scroll', queuePosition, { passive: true });
    // The outline's rows are rebuilt whenever the sidebar renders — a panel opened, another document
    // — and the new rows have to be marked afresh.
    const sidebar = document.querySelector('.sidebar');
    if (sidebar) {
        if (sidebarObserver) sidebarObserver.disconnect();
        sidebarObserver = new MutationObserver(queuePosition);
        sidebarObserver.observe(sidebar, { childList: true, subtree: true });
    }
    paneObserver = new ResizeObserver(() => {
        const size = pane.clientWidth + 'x' + pane.clientHeight;
        if (size === paneSize) return;
        paneSize = size;
        repaginate();
    });
    paneObserver.observe(pane);
}

/// Takes the observers off the pane; the interop calls this when the viewer goes.
export function unwatchPane() {
    for (const o of [paneObserver, docObserver, sidebarObserver]) if (o) o.disconnect();
    paneObserver = docObserver = sidebarObserver = null;
    watchedPane = null;
}

// Scrolls the sidebar tree so the given row sits at the top of the panel.
export function scrollTreeTo(containerId, rowId) {
    const container = document.getElementById(containerId);
    const row = document.getElementById(rowId);
    if (!container || !row) return;
    // Measured on screen rather than by offsetTop: the row sits inside nested panels, and offsets are
    // only relative to whichever ancestor happens to be positioned.
    container.scrollTop += row.getBoundingClientRect().top - container.getBoundingClientRect().top - 6;
}

// Reset the content viewport to the top (used when navigating to a new document).
export function scrollTop() {
    const el = document.querySelector('.content-area');
    if (el) el.scrollTop = 0;
}

export function scrollTo(id) {
    jumpedTo = id;
    queuePosition();
    const el = document.getElementById(id);
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
}
