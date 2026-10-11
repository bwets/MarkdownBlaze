# Privacy Policy — MarkdownBlaze

_Last updated: 2026-10-11_

MarkdownBlaze ("the app") is an offline desktop application that renders Markdown files on your
device. Your privacy is simple to describe: **the app does not collect, transmit, store remotely,
or share any personal data.**

## Information we collect

**None.** MarkdownBlaze has no accounts, no sign-in, no analytics, no telemetry, no advertising, and
no tracking of any kind. The developer receives no data from your use of the app.

## Data stored on your device

The app saves a small amount of information **locally on your computer only** — it is never
uploaded or transmitted anywhere:

- **Preferences** — how you have set the app up: sidebar, appearance, document theme, view, zoom and
  window placement; whether the guided tour and the one-time rating request have been shown; and a
  count of the documents you have opened, kept only to decide when that request appears.
- **History** — the list and titles of Markdown files you have opened, so you can reopen them.
- **Log** — a small rolling log file (`markdownblaze.log` in a `logs` folder next to them, a megabyte at
  most, with three older ones kept) that records problems the app ran into, such as a file it could not read
  or a link that would not open; it can name the files involved and, at the detailed level, the addresses of links the app refused to open. It is for
  diagnosing faults, stays on your computer, is never sent anywhere, and is deleted with the folder.
  Starting the app with `--log-level debug` makes it more detailed.
- **Web view data** (Windows) — the browser engine that draws the window (WebView2) keeps its working files in a
  `webview` folder next to the files above: its profile, and whatever a page leaves behind (cookies, local
  storage). The app asks the engine not to keep a disk cache, so the pictures a document loads from the web are
  not written to disk; the little the engine does keep stays on your computer and is never sent anywhere. Older
  builds put these files in a folder named `Photino`, shared with other programs built on the same toolkit; this
  app no longer uses it and leaves it alone, because other programs may — you can delete
  `%LOCALAPPDATA%\Photino` yourself if nothing else needs it.

This data lives in your local application-data folder (for example
`%LOCALAPPDATA%\MarkdownBlaze` on Windows) and can be deleted at any time by removing that folder.

To render a document, the app reads the Markdown file you open and any local images or files it
references. This content is processed locally and is not sent anywhere.

## Windows registration

On Windows the app can tell Windows that it opens Markdown files and `markdown://` links: an *Open with*
entry for `.md`, `.markdown` and `.mdx` files, an *Open with MarkdownBlaze* entry on folders, and the
`markdown://` link handler. These are entries under `HKEY_CURRENT_USER\Software\Classes` (your account
only; no administrator rights) plus, for the installer, a marker under `HKEY_CURRENT_USER\Software\MarkdownBlaze`.
They contain program paths and labels, nothing about you or your documents. They are written only when
you ask for them:

- by the **installer**, which removes them again when you uninstall;
- by **Settings → Windows integration → Add to Explorer…** in the portable version, or by running
  `MarkdownBlaze.exe --register`. **Remove** in the same place, or `MarkdownBlaze.exe --unregister`, deletes
  them.

The app never changes the registry when it starts, and the Microsoft Store version does not write to it at
all (its file types are declared in its package).

## Network use

The app itself makes no network requests — no update check, no crash reporting, no analytics, and
everything needed to render is bundled — and the only time it goes online is when a document you
open references a resource on the web (typically an image with an `http://` or `https://` address),
which the web view then fetches the way a browser would. All rendering
components (syntax highlighting, diagrams) are bundled with the app. If you click a web link inside
a document, it opens in your default web browser; any data handling at that point is governed by
that browser and the destination website, not by MarkdownBlaze. Likewise, if you choose **Rate it**
on the app's one-time rating request, the Microsoft Store app opens on MarkdownBlaze's page;
MarkdownBlaze itself sends nothing.

The one case where the app goes online is a document that references a web resource. The web view
fetches it exactly as a browser would, which reveals to the hosting server that your IP address
requested it, and when. Local images are read from your disk by the app when the window asks for them and are shown in the window;
they never leave your machine. Whether a document references a remote resource is the choice of its
author, so documents from untrusted sources may contain such references.

## Permissions

The Microsoft Store version is a packaged desktop application and declares the `runFullTrust` capability.
This is required for desktop (Win32) apps that host a local web view; MarkdownBlaze uses it solely
to render the local files you choose to open. It is not used to access, collect, or transmit any
other data. The installer and portable versions run with your normal account's permissions and ask for
no more.

## Children's privacy

The app is not directed at children and does not knowingly collect any information from anyone.

## Changes to this policy

Any changes will be published at this page. Continued use of the app after a change constitutes
acceptance of the updated policy.

## Contact

Questions about this policy can be raised via the project's issue tracker:
<https://github.com/bwets/MarkdownBlaze/issues>
