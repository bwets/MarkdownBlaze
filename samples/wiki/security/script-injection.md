---
title: Script Injection
tags:
  - sample
  - security
  - test
---

# Script Injection

Markdown lets raw HTML through, so a document can try to run script in the page. Every payload below
only ever calls `alert('…')`. **A dialog box at any point is a failure.** The rule: dangerous markup is
dropped or shown as plain text, and nothing runs. Harmless markup beside it (the links, the collapsible
section) stays.

Each item is the payload as the document carries it, then what you should see.

## A script element

<script>alert('script element')</script>

Payload: `<script>…</script>` with an `alert`. **Expected:** nothing at all is shown above this
paragraph, and no dialog appears — the element is removed together with its content.

## An image that fails to load

<img src="x" onerror="alert('img onerror')">

Payload: `<img src="x" onerror="alert('…')">`. **Expected:** no dialog. The image has nothing to load
and shows nothing (or a broken-image mark); the `onerror` attribute is stripped, so there is no handler to run.

## An SVG with a load handler

<svg onload="alert('svg onload')"></svg>

Payload: `<svg onload="alert('…')">`. **Expected:** no dialog. Raw `<svg>` is not allowed in a
document at all, so nothing is drawn (diagrams are drawn after sanitising, from `mermaid` blocks, and
are not affected).

## A collapsible section with a toggle handler

<details ontoggle="alert('details ontoggle')">
<summary>Open this section</summary>

The section itself is harmless and stays: it opens and closes like any other.

</details>

Payload: `<details ontoggle="alert('…')">`. **Expected:** the section above is there and opens when
clicked, and *no dialog appears when it does* — only the handler is removed.

## An input that focuses itself

<input autofocus onfocus="alert('input onfocus')">

Payload: `<input autofocus onfocus="alert('…')">`. **Expected:** no text box is shown and no dialog
appears. Only a disabled checkbox (a task-list item) survives of all the input elements.

## A link with a handler for the middle button

<a onauxclick="alert('onauxclick')" target="_blank" href="https://example.com">Middle-click this link</a>

Payload: `<a onauxclick="alert('…')" target="_blank" href="https://example.com">`. **Expected:** the
text above is a plain link to `https://example.com`. A click opens your browser there; a middle click
does nothing and shows no dialog; no second window is opened by the app.

## `javascript:` addresses in disguise

Each of these is an `<a>` whose address is `javascript:` written so that a naive filter would miss it.
**Expected:** the text is there, but it is not a link — no pointer cursor, no underline, and a click
does nothing.

<a href="&#106;avascript:alert('entity')">written with a character entity</a>

<a href="java&#9;script:alert('tab')">with a tab inside the word</a>

<a href="JaVaScRiPt:alert('case')">in mixed case</a>

<a href=" javascript:alert('space')">with a space in front</a>

<a href="javascript:alert('plain')">plain</a>

## What should still work

These are ordinary and must be unaffected: a [link to the project's sample library](../index), a
[link that opens in the browser](https://example.com), `inline code`, and a task list:

- [x] sanitised
- [ ] not yet
