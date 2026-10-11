---
title: UI Spoofing
tags:
  - sample
  - security
  - test
---

# UI Spoofing

The window the document is shown in is the viewer's own: a toolbar, a sidebar, menus, a request to rate the app.
A page that can borrow the *names* of those pieces — their CSS classes, their ids, their accessibility roles —
could cover the whole window with a layer that takes every click, or paint a convincing "enter your password"
bar that looks as if the app were asking. Nothing here needs script: a class name is enough.

The rule: **a document keeps the classes that its own formatting needs** (code blocks, callouts, task lists,
footnotes, math, diagrams, tags, wiki links) **and no others**, and it cannot take the ids or the roles the app uses.
The element stays; only the borrowed name goes.

**Expected, for the page as a whole:** nothing on it looks like part of the app, and — before you read any further —
the toolbar buttons and the sidebar still answer a click. Click the *Back* arrow, or open the *View* menu and close it
with <kbd>Esc</kbd>. If a click ever does nothing on this page, something above the toolbar is taking it, and that is a bug.

Each item is the payload, then what you should see.

## A layer over the whole window

<div class="menu-backdrop"></div>

Payload: `<div class="menu-backdrop"></div>`. In the viewer's stylesheet that class is a transparent layer fixed
over the window, the thing that sits behind an open menu and takes the click that closes it. **Expected:** it is an
ordinary empty block. The toolbar, the sidebar and the scroll bar all answer.

## A fake request for a password

<div class="rate-bar"><span>MarkdownBlaze needs you to sign in again. Please type your password into the box below.</span></div>

Payload: `<div class="rate-bar">…please type your password…</div>`. The class is the bar along the bottom of the
window that asks for a rating. **Expected:** the sentence is shown as a paragraph of this document, in the
document's own type, with no bar, no border and no buttons around it. (There is no box to type into: inputs are
removed. A request that looks like the app's own would be the thing to worry about.)

## A fake start-up screen

<div class="splash"><div class="splash-name">MarkdownBlaze is loading, please wait</div></div>

Payload: `<div class="splash"><div class="splash-name">…</div></div>`. The classes are the start-up screen's.
**Expected:** two lines of ordinary text; no full-window screen, no logo, no progress bar.

## A fake menu and a fake settings page

<div class="ctx ctx-right">Copy &middot; Open in a new window &middot; Delete this file</div>

<div class="settings-overlay">Settings: your licence has expired. Click here to renew.</div>

Payload: `<div class="ctx ctx-right">…</div>` (the context menu) and `<div class="settings-overlay">…</div>` (the
Settings page, which is drawn over the viewer). **Expected:** both are plain lines of text. Nothing floats, and
nothing looks like a menu or a page of the app.

## The same names through Markdown's own attributes

A Markdown extension lets a document put a class on a link or a heading in braces, and another turns a fenced
block into an element with whatever class its first word names. Both are the same thing as writing the class by hand.

[A link that tries to be the layer](https://example.com){.menu-backdrop}

### A heading that tries to be the rating bar {.rate-bar}

::: menu-backdrop
This paragraph is the content of a custom container whose name is `menu-backdrop`.
:::

Payload: `[…](https://example.com){.menu-backdrop}`, `### … {.rate-bar}` and a container opened with
`::: menu-backdrop`. **Expected:** the link is still a link to example.com (a click opens your browser), the heading
is still a heading, and the paragraph in the container is still there, as an ordinary block. No class
of the app's is on any of them, and the toolbar still answers.

## Names the app looks things up by

<div id="md-tree">A second "sidebar" with the id of the real one.</div>

<div id="md-splitter">A second "splitter" with the id of the real handle.</div>

Payload: `<div id="md-tree">` and `<div id="md-splitter">`. The app finds its sidebar and the handle you drag to
resize it by those ids, and its stylesheet styles them. **Expected:** the ids are gone from the two blocks, the
text is plain, and the real sidebar can still be scrolled and resized by dragging the handle between it and the page.
(A heading whose automatic id starts with `md-` or `tab-` — "MD notes", "Tab stops" — loses its id for the same
reason, and an in-page link to it does not scroll. That is the price; the page about [resource limits](resource-limits)
has no such heading.)

## A role that makes a screen reader announce a dialog

<div role="dialog" aria-modal="true" aria-label="MarkdownBlaze security warning">Your computer is at risk. Call 555-0100.</div>

Payload: `<div role="dialog" aria-modal="true" aria-label="…">`. **Expected:** the sentence is shown as a paragraph.
A screen reader reads it as text, not as a dialog that has just opened (the role, the modal flag and the
label are all removed). Only roles that describe content — `note`, `figure`, `list`, `table`, `heading`,
the `doc-…` family — survive.

## What should still work

These keep their classes and their look: a callout, a task list, a footnote, math, and a code block.

!!! note "A callout"
    Callouts have classes of their own (`admonition`, `admonition-note`, …) and keep them.

- [x] a task list
- [ ] keeps its classes too

A footnote[^1] and a formula $E = mc^2$.

```csharp
var highlighted = true; // the language class stays, and so does the colour
```

[^1]: Footnotes have classes too.
