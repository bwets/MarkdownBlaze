---
title: Page Hijack
tags:
  - sample
  - security
  - test
---

# Page Hijack

Markup that cannot run script can still do harm: redirect the window, load someone else's page into
it, lay a fake interface over the real one, or pretend to be one of the app's own controls. Everything
below must be removed or made inert. **Expected for the page as a whole:** it stays where it is, the
toolbar and sidebar are untouched and clickable, and the only thing in this list that does anything is
the last item.

Each item is the payload, then what you should see.

## Redirect and base address

<meta http-equiv="refresh" content="0; url=https://example.com">

`<meta http-equiv="refresh">` — **Expected:** the page does not navigate. You are still reading this.

<base href="https://example.com/">

`<base href="https://example.com/">` — **Expected:** nothing changes. Relative links on this page, like
[the index of this folder](index), still open here and not at example.com.

## Forms, frames and objects

<form action="https://example.com/collect" method="post"><input name="secret"><button>Send</button></form>

`<form>` — **Expected:** no form, no text box, no button. Nothing can be submitted from a document.

<iframe src="https://example.com"></iframe>

`<iframe>` — **Expected:** no frame; no page from example.com is shown inside the document.

<object data="https://example.com"></object>

`<object>` — **Expected:** nothing is embedded.

## Styles and layout

<style>body { display: none } .app-header { display: none }</style>

`<style>` that hides the page and the toolbar — **Expected:** the page and the toolbar are visible.

<div style="position:fixed; inset:0; background:red; z-index:99999">A fixed red overlay that covers everything</div>

`style="position:fixed"` — **Expected:** the text of that overlay is shown here as ordinary text, but
nothing covers the window and there is no red: inline styles are removed.

<link rel="stylesheet" href="https://example.com/evil.css">

`<link rel=stylesheet>` — **Expected:** nothing is loaded from example.com.

## Pretending to be the app

<a data-nav="C:\Windows\win.ini" href="#">This looks like a link to another page</a>

A forged `data-nav` — the attribute the viewer itself uses to mark an in-app link, here pointing at a
system file. **Expected:** the text is there, but it does not navigate. The viewer removes every
`data-nav` a document carries and puts the attribute only on the links it rewrote itself, and even then
it follows one only to a document that exists *and* is a Markdown file. (The same goes for a forged
`data-nav` that points at a real Markdown file: it is not a link either.)

<div id="md-tree">A forged sidebar</div>

A forged `<div id="md-tree">` — the id of the real sidebar. **Expected:** the real sidebar is
unaffected, and the sidebar keeps working.

## The one thing that is allowed

<img src="https://example.com/pixel.png" alt="a remote picture (broken when offline)">

An image from the web. **Expected:** it loads, or shows its alt text when you are offline. This is the
accepted behaviour: a document may show a picture from the web, and the viewer then fetches it the way a
browser would — which tells that server that your address looked at the page. Pictures that sit on
your own disk never leave the machine. See the privacy policy.
