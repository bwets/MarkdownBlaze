---
title: Phishing
tags:
  - sample
  - security
  - test
---

# Phishing

A link says one thing and does another: its text is whatever the author wrote, and so is the tooltip. A reader
who clicks what a link *says* can end up somewhere else. A browser answers by showing the real address in a
strip at the bottom of the window while the pointer is over the link; MarkdownBlaze does the same, because it
opens a web link in the reader's browser.

The strip shows the address that the viewer would actually open — **never the text of the link, and never its
`title`** — written the way the address parser writes it, so that a host spelled with look-alike letters from
another alphabet shows as the plain ASCII name (`xn--…`) it really is. It appears when the pointer rests on the
link and when the link has the keyboard focus (<kbd>Tab</kbd> to it), at the bottom left of the window, and goes
when the pointer or the focus moves on. A link that stays in the app, scrolls the page or does nothing shows nothing.

Nothing here is dangerous to click: every address is `example.com` or in the reserved `.invalid` domain, which
cannot resolve. The point is what you can *see* before you click.

Each item is the payload, then what you should see.

## Link text that lies

[https://good.example/account](https://evil.invalid/login)

Payload: `[https://good.example/account](https://evil.invalid/login)`. **Expected:** with the pointer on the link,
the strip says `https://evil.invalid/login`. The text of the link says another thing; the strip is what to believe.

## A right-to-left override in the text

[annex&#x202E;fdp.exe](https://evil.invalid/annex.exe)

Payload: `[annex&#x202E;fdp.exe](https://evil.invalid/annex.exe)`. The text contains U+202E, "right-to-left
override", which makes what follows it display backwards — so the end of the link looks like `exe.pdf`, a document,
and not `.exe`, a program. **Expected:** the strip says `https://evil.invalid/annex.exe`, whatever the text looks like.

## A host with a look-alike letter

<a href="https://&#x430;pple.com/">https://apple.com</a>

Payload: `<a href="https://&#x430;pple.com/">https://apple.com</a>`. The first letter of the address is a Cyrillic "а",
which looks exactly like a Latin "a". (Written as HTML, because Markdown would already write the host in ASCII.)
**Expected:** the strip says `https://xn--pple-43d.com/` — the same host written in ASCII, which does not look like
apple.com at all.

## A title that lies

[Documentation](https://evil.invalid/docs "https://good.example/docs")

Payload: `[Documentation](https://evil.invalid/docs "https://good.example/docs")`. The `title` is what the window's
tooltip would show after a moment. **Expected:** the strip says `https://evil.invalid/docs` straight away; the
`title` is the author's text and is not what the strip trusts.

## Links that show nothing

These do not open an address in your browser, so the strip does not appear for them: [a link to a page of this
library](index), [a link to a heading on this page](#phishing), and
[a link that is inert](file:///C:/Windows/System32/calc.exe).

## What should still work

An honest link shows its address, and opens it: [example.com](https://example.com/), and an address for mail:
[write to someone](mailto:someone@example.com).
