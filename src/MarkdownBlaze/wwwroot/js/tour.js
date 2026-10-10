// ---- Guided tour ------------------------------------------------------------------------------
// A handful of tooltips, one at a time, each pointing at one part of the window. Offered on first
// run and from Settings; never forced — every step can skip the rest, and so can Esc, and the first
// step can put the whole thing off until the app is next started. The tooltip
// and the ring around its target are added to <body>, outside the element Blazor renders into.
//
// `target` may match several elements; the step points at all of them together. A step with
// `opens` is about something that is not normally on screen: the tour has it shown for that step
// and put away again afterwards.
import { call } from './bridge.js';

const STEPS = [
    { target: '.toolbar-left > *', place: 'bottom', title: 'Files',
      text: 'Open a file or a whole folder, reload the document, print it, or show it in your file manager.' },
    { target: '.print-bar', place: 'top', opens: 'print', title: 'Printing',
      text: 'Ctrl + P opens this bar first. Pick a look for this one print — the document changes as you choose — then press Print. Esc puts it away.' },
    { target: '#md-tree', place: 'right', title: 'Your folder',
      text: 'Every folder and document is a panel you can open and close. The document you are reading shows its headings — click one to jump to it.' },
    { target: '#tab-history', place: 'bottom', title: 'History',
      text: 'Everything you have opened, kept between launches. Contents, beside it, brings the folder back.' },
    { target: '.toolbar-middle > fluent-button', place: 'bottom', title: 'Sidebar and navigation',
      text: 'Hide or show the sidebar, and step back and forward through the documents you have read.' },
    { target: '.toolbar-right > fluent-button:first-child', place: 'bottom', title: 'View',
      text: 'Read as one continuous column, or as pages laid out the way they will print. F2 switches.' },
    { target: '.zoom-group', place: 'bottom', title: 'Zoom',
      text: 'Make the document larger or smaller without scaling the app. Ctrl + wheel does the same.' },
    { target: '.toolbar-right > fluent-button:nth-last-child(2)', place: 'bottom', title: 'Document theme',
      text: 'Give the document another look — paper, typeface and spacing. F9 steps through them.' },
    { target: '.toolbar-right > fluent-button:last-child', place: 'bottom', title: 'More',
      text: 'Keyboard shortcuts, what is new in this version, and Settings — where this tour can be started again.' },
];

let tour = null; // { steps, index, tip, ring, opened, canDefer, returnTo } while the tour is running

/// Shows or puts away what a step asked to have opened. Only the print bar, so far.
function openStepThing(what, open) {
    if (what !== 'print') return;
    call('OnTourPrintBar', open);
}

/// The box around everything a step points at, or null when none of it is on screen (the sidebar
/// may be hidden, for one).
function targetRect(step) {
    let box = null;
    for (const el of document.querySelectorAll(step.target)) {
        const r = el.getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue;
        box = box ? { left: Math.min(box.left, r.left), top: Math.min(box.top, r.top),
                      right: Math.max(box.right, r.right), bottom: Math.max(box.bottom, r.bottom) }
                  : { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
    }
    return box;
}

/// `canDefer`: whether the first step offers Later. .NET stops allowing it after a couple of times.
export function startTour(canDefer) {
    if (tour) return;
    const steps = STEPS.filter(step => step.opens || targetRect(step));
    if (!steps.length) return;

    // The tooltip takes the keyboard focus for as long as it is up (its Next button is focused at
    // every step), so remember where the focus was and give it back at the end.
    const returnTo = document.activeElement;

    const tip = document.createElement('div');
    tip.className = 'tour-tip';
    tip.setAttribute('role', 'dialog');
    tip.setAttribute('aria-label', 'Guided tour');
    tip.innerHTML = '<div class="tour-arrow"></div><div class="tour-title"></div><div class="tour-text"></div>' +
        '<div class="tour-foot"><span class="tour-count"></span>' +
        '<button type="button" class="tour-skip">Skip tour</button>' +
        '<button type="button" class="tour-back">Back</button>' +
        '<button type="button" class="tour-next">Next</button></div>';
    const ring = document.createElement('div');
    ring.className = 'tour-ring';
    document.body.append(ring, tip);

    tip.querySelector('.tour-skip').addEventListener('click', () => endTour(false));
    // On the first step there is nothing to go back to, and the button offers Later instead.
    tip.querySelector('.tour-back').addEventListener('click', () => {
        if (tour && tour.index === 0) { if (tour.canDefer) endTour(true); }
        else go(-1);
    });
    tip.querySelector('.tour-next').addEventListener('click', () => go(1));

    tour = { steps, index: 0, tip, ring, opened: null, canDefer: !!canDefer, returnTo };
    window.addEventListener('resize', onResize);
    show();
}
function onResize() { show(); }

function go(step) {
    if (!tour) return;
    const next = tour.index + step;
    if (next >= tour.steps.length) { endTour(false); return; }
    tour.index = Math.max(0, next);
    show();
}

/// Lays the tooltip and the ring against the current step's target.
function show(waited) {
    if (!tour) return;
    const { steps, index, tip, ring } = tour;
    const step = steps[index];

    // Put away whatever an earlier step had opened, and have this step's own thing shown. That goes
    // through .NET and a render, so the target is not there yet: look again shortly, and give up on
    // the step — not on the tour — if it never turns up.
    if (tour.opened && tour.opened !== step.opens) { openStepThing(tour.opened, false); tour.opened = null; }
    if (step.opens && !tour.opened) { tour.opened = step.opens; openStepThing(step.opens, true); }
    const box = targetRect(step);
    if (!box) {
        const tries = typeof waited === 'number' ? waited : 0;
        if (step.opens && tries < 40) { tip.style.visibility = ring.style.visibility = 'hidden'; setTimeout(() => show(tries + 1), 50); }
        else go(1); // its target is not on screen
        return;
    }
    tip.style.visibility = ring.style.visibility = 'visible';

    tip.querySelector('.tour-title').textContent = step.title;
    tip.querySelector('.tour-text').textContent = step.text;
    tip.querySelector('.tour-count').textContent = (index + 1) + ' of ' + steps.length;
    const back = tip.querySelector('.tour-back');
    back.textContent = index === 0 ? 'Later' : 'Back';
    back.title = index === 0 ? 'Show the tour the next time the app starts' : '';
    back.style.visibility = index === 0 && !tour.canDefer ? 'hidden' : 'visible';
    tip.querySelector('.tour-skip').style.visibility = index === steps.length - 1 ? 'hidden' : 'visible';
    tip.querySelector('.tour-next').textContent = index === steps.length - 1 ? 'Done' : 'Next';

    const pad = 4, gap = 12, edge = 8;
    ring.style.left = (box.left - pad) + 'px';
    ring.style.top = (box.top - pad) + 'px';
    ring.style.width = (box.right - box.left + 2 * pad) + 'px';
    ring.style.height = (box.bottom - box.top + 2 * pad) + 'px';

    const w = tip.offsetWidth, h = tip.offsetHeight;
    const arrow = tip.querySelector('.tour-arrow');
    const clamp = (v, lo, hi) => Math.max(lo, Math.min(v, hi));
    // Above or beside the target when asked and there is room; otherwise underneath it.
    if (step.place === 'top' && box.top - pad - gap - h >= edge) {
        const centre = (box.left + box.right) / 2;
        const left = clamp(centre - w / 2, edge, window.innerWidth - w - edge);
        tip.style.left = left + 'px';
        tip.style.top = (box.top - pad - gap - h) + 'px';
        arrow.style.top = (h - 6) + 'px';
        arrow.style.left = clamp(centre - left - 6, 14, w - 26) + 'px';
    } else if (step.place === 'right' && box.right + gap + w + edge <= window.innerWidth) {
        const top = clamp(box.top + 8, edge, window.innerHeight - h - edge);
        tip.style.left = (box.right + pad + gap) + 'px';
        tip.style.top = top + 'px';
        arrow.style.left = '-6px';
        arrow.style.top = clamp(box.top + 28 - top, 14, h - 26) + 'px';
    } else {
        const centre = (box.left + box.right) / 2;
        const left = clamp(centre - w / 2, edge, window.innerWidth - w - edge);
        tip.style.left = left + 'px';
        tip.style.top = (box.bottom + pad + gap) + 'px';
        arrow.style.top = '-6px';
        arrow.style.left = clamp(centre - left - 6, 14, w - 26) + 'px';
    }
    tip.querySelector('.tour-next').focus({ preventScroll: true });
}

/// Ends the tour. Finished or skipped, .NET is told the same thing — it has been seen; `deferred`
/// says it was put off instead, to be offered again at the next launch.
export function endTour(deferred) {
    if (!tour) return;
    if (tour.opened) openStepThing(tour.opened, false);
    const { returnTo } = tour;
    tour.tip.remove();
    tour.ring.remove();
    tour = null;
    window.removeEventListener('resize', onResize);
    // The tooltip had the focus; with it gone the focus would fall to the page. Back to where it was.
    if (returnTo && returnTo.isConnected && typeof returnTo.focus === 'function') returnTo.focus({ preventScroll: true });
    call('OnTourEnded', deferred === true);
}

/// While the tour is up, Esc leaves it and the arrows walk it; nothing else is taken over. Returns
/// whether the key was one of those (and has been dealt with).
export function handleTourKey(e) {
    if (!tour || e.ctrlKey || e.altKey) return false;
    if (e.key === 'Escape') { e.preventDefault(); endTour(false); return true; }
    if (e.key === 'ArrowRight') { e.preventDefault(); go(1); return true; }
    if (e.key === 'ArrowLeft') { e.preventDefault(); go(-1); return true; }
    return false;
}
