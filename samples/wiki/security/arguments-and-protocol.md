---
title: Arguments and Protocol
tags:
  - sample
  - security
  - test
---

# Arguments and Protocol

MarkdownBlaze is the handler of two kinds of address, and both can be written by someone else. The first is the
`markdown://` link: another program (or a web page) can ask Windows to open `markdown://document/…`, and Windows starts
the viewer with that address as its command line. The second is the viewer's own `mdasset://` scheme, the one it
serves a document's pictures under — an address the page asks for, and one that only the viewer itself should be
able to write.

A document must not be able to use either against the viewer: not to make it open a program, not to hand it extra
switches, not to make it serve a file it did not offer. Every payload here is a link or a picture, and every one of
them has to end up **inert or removed**. Nothing is opened, nothing is registered and nothing changes when you click.

Each item is the payload, then what you should see.

## A markdown:// link to a program

[Open the calculator](markdown://C:/Windows/System32/calc.exe)

Payload: `[…](markdown://C:/Windows/System32/calc.exe)`. **Expected:** the text is there but it is not a link: no
pointer cursor, no underline, and a click does nothing. `markdown:` is not one of the schemes a link may use
(`http`, `https`, `mailto`, `tel`, `ftp`), and no program starts.

## A link whose address carries an extra switch

[Unregister the viewer](<markdown://document/#C:/x.md" --unregister ">)

Payload: `[…](<markdown://document/#C:/x.md" --unregister ">)` — an address with a quote and a command-line switch
inside it, the kind that turns into an extra argument when a launcher does not escape quotes. **Expected:** not a
link, as above; a click does nothing. (If such an address did reach the viewer's command line, the switch would
still be read as a file name: the viewer takes `--register`, `--unregister`, `--devtools` and `--log-level` only before
the first file name. The unit tests check that.)

## A picture with an address only the viewer may write

<img src="mdasset://doc/00000000000000000000000000000000/x.png" alt="a picture the viewer was never asked to show">

Payload: `<img src="mdasset://doc/00000000000000000000000000000000/x.png">`, with a token made of zeros — the address
of a picture of the document on screen, as an author might guess it. **Expected:** an empty image: no picture, no
broken-image box with a source. In a debug build, the developer tools' Network tab shows no request for an
`mdasset://` address. (The viewer would refuse the request anyway: it serves only the pictures of the render that
is on screen, and a token nobody issued matches nothing. This is the first of two layers.)

## A link to an address only the viewer may write

[A link to a picture of the viewer's](mdasset://doc/x/y)

Payload: `[…](mdasset://doc/x/y)`. **Expected:** not a link; a click does nothing. `mdasset:` is not a scheme a link
may use, and a link is not a picture in any case.

## What should still work

A picture that sits next to a document is shown, an address on the web opens in your browser, and a link to a
page of this library opens in the viewer: [the index of this folder](index), [example.com](https://example.com).
