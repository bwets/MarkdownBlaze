---
title: File Access
tags:
  - sample
  - security
  - test
---

# File Access

To show a picture that sits next to a document, the viewer reads the file and hands it to the page.
A hostile document can point an image at something that is not a picture — a private key, a system
file, a network share — hoping to get its contents into the page (and from there, out). Only image
files may be read; share and device paths are never touched, not even to see whether they exist.

**Expected, for every image below:** the viewer shows the grey, dashed *Image not displayed*
placeholder, and **no content of any file** ends up in the page. In a debug build, *View source* on the document element shows
no `mdasset://` address for any of them (that is the address the viewer gives the pictures it *does* show) and no
`base64` data but the placeholder itself.

Each payload is an image, `![alt](path)`, followed by what is expected.

## Private files

![a private key](../../../../.ssh/id_rsa)

`../../../../.ssh/id_rsa` — a path that climbs out of the folder to a key file. Not an image type, so
refused: the placeholder.

![the system's win.ini](C:/Windows/win.ini)

`C:/Windows/win.ini` — an absolute path to an existing text file. Refused: the placeholder.

![the password file](/etc/passwd)

`/etc/passwd` — the same on Unix. Refused: the placeholder, and no file content in the page.

## Shares and devices

![a pixel on another machine](//attacker.invalid/share/pixel.png)

`//attacker.invalid/share/pixel.png` — a share on another machine. Even though it looks like an image,
the viewer must not ask Windows to open it (that would send the reader's credentials to the server):
the placeholder, and no network traffic.

![a device path](\\\\?\\C:\\x.png)

`\\?\C:\x.png` — a device path. Refused: the placeholder.

![a device name](CON.png)

`CON.png` — a reserved device name, which Windows opens as the console and which can hang a program
waiting for input. Refused: the placeholder.

## A file that is not an image

![another document](../link-test.md)

`../link-test.md` — a real file, one folder up, but a document and not a picture. Refused: the
placeholder, and none of its text is shown.

## What should still work

An image that sits next to the document, and is a picture, is shown. This page has none to offer
(the bundled sample pages carry no pictures), so this is covered by the unit tests; a Markdown file of
your own with `![logo](logo.png)` beside a `logo.png` shows it.
