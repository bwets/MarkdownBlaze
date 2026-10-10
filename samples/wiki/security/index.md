---
title: Hostile Documents
tags:
  - sample
  - security
---

# Hostile Documents

A document is not trusted: it can be a download, a colleague's wiki page, a file that arrived by mail.
MarkdownBlaze is registered to open `.md` files from Explorer, so *anything* can end up on screen in
front of it. The pages in this folder are what such documents look like — and each of them is **harmless
by construction**: the payloads only call `alert`, point at `example.com` or at the reserved
`.invalid` domain, or name files that do not exist. Opening them in another viewer might pop up a
dialog. Opening them here must not.

## How to use these pages

1. Open each page below.
2. Read the **expected outcome** beside every payload and confirm it.
3. In a debug build, open the developer tools (<kbd>F12</kbd>) and check that the console shows no
   script running and no Content-Security-Policy violation.

Nothing on these pages may ever show a dialog, start a program, open a window, change the address of
the app, or put the contents of a file into the page. If it does, that is a bug.

## The pages

| Page | What it throws at the viewer |
|---|---|
| [Script injection](script-injection) | Raw `<script>`, event-handler attributes, obfuscated `javascript:` addresses |
| [Dangerous links](dangerous-links) | Links that would run a program, open a share or hand code to a handler |
| [File access](file-access) | Images that point at files the document has no business reading |
| [Page hijack](page-hijack) | Markup that tries to redirect, overlay or impersonate the app itself |

The same payloads are checked automatically: the unit tests render every page in this folder and fail
if a dangerous marker survives in what reaches the window, so these pages cannot quietly go stale.

> Back to the [Sci-Fi Library](../index) or the [Link Handling Test](../link-test).
