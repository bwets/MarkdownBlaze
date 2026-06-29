# Privacy Policy — MarkdownBlaze

_Last updated: 2026-06-29_

MarkdownBlaze ("the app") is an offline desktop application that renders Markdown files on your
device. Your privacy is simple to describe: **the app does not collect, transmit, store remotely,
or share any personal data.**

## Information we collect

**None.** MarkdownBlaze has no accounts, no sign-in, no analytics, no telemetry, no advertising, and
no tracking of any kind. The developer receives no data from your use of the app.

## Data stored on your device

The app saves a small amount of information **locally on your computer only** — it is never
uploaded or transmitted anywhere:

- **Preferences** — your sidebar and theme settings.
- **History** — the list and titles of Markdown files you have opened, so you can reopen them.

This data lives in your local application-data folder (for example
`%LOCALAPPDATA%\MarkdownBlaze` on Windows) and can be deleted at any time by removing that folder.

To render a document, the app reads the Markdown file you open and any local images or files it
references. This content is processed locally and is not sent anywhere.

## Network use

MarkdownBlaze works fully offline and makes no network requests of its own. All rendering
components (syntax highlighting, diagrams) are bundled with the app. If you click a web link inside
a document, it opens in your default web browser; any data handling at that point is governed by
that browser and the destination website, not by MarkdownBlaze.

## Permissions

On Windows the app is a packaged desktop application and declares the `runFullTrust` capability.
This is required for desktop (Win32) apps that host a local web view; MarkdownBlaze uses it solely
to render the local files you choose to open. It is not used to access, collect, or transmit any
other data.

## Children's privacy

The app is not directed at children and does not knowingly collect any information from anyone.

## Changes to this policy

Any changes will be published at this page. Continued use of the app after a change constitutes
acceptance of the updated policy.

## Contact

Questions about this policy can be raised via the project's issue tracker:
<https://github.com/bwets/MarkdownBlaze/issues>
