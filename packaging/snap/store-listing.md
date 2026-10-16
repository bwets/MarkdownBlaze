# Snap Store listing — MarkdownBlaze

Copy-paste fields for the listing on snapcraft.io (Ubuntu App Center shows the same page). The title,
summary, description, licence and links also come from `snapcraft.yaml`; what is below is what to
enter in the store dashboard for the things the yaml cannot carry (icon, screenshots, category).

## Title

MarkdownBlaze

## Summary

At most 79 characters; this one is 52.

> A fast, offline Markdown viewer that prints properly

## Description

The store shows plain paragraphs and simple bullets. This is the same text as `description:` in
`snapcraft.yaml`.

MarkdownBlaze is a fast, no-fuss viewer for Markdown (.md) files. Point it at a document, or at a
whole folder, and it renders a clean, readable page in a moment, then gets out of your way.

It is also the rare Markdown viewer that prints properly. Choose from eleven document themes, switch to
page view to see the document laid out on A4 sheets exactly as it will print, and pick the look for each
print from a bar along the bottom of the window. Only the document is printed, never the app around it.

It understands the Markdown you actually use: tables and task lists, footnotes, code with syntax
highlighting for about 190 languages, Mermaid diagrams, KaTeX math, callouts, and Obsidian wiki links,
tags and comments.

The sidebar shows the folder you are reading as collapsible panels, with the outline of the current
document. Links between Markdown files open inside the app, and the view refreshes when you save the
file in your editor.

MarkdownBlaze makes no network requests of its own and has no accounts, ads or tracking. The only time
anything goes online is when a document you open references a resource on the web, such as a remote image.

Files on removable media (USB drives) are reachable once you run:
snap connect markdownblaze:removable-media

## Category

Productivity (the store's second choice, if it offers one: Utilities or Development).

## Icon

`branding/icon.svg` (the store takes PNG or SVG, 256x256 to 512x512 recommended for a PNG: use
`branding/icon-512.png` if the dashboard refuses the SVG).

## Screenshots

The store takes PNG, JPEG and GIF. Every image below is under 300 KB (the GIF under 750 KB), far below
any size limit the dashboard is likely to apply; if it states a limit, check it. In the order to upload them (the first is the one the
App Center shows first):

| # | File | Size | Pixels | Caption |
|---|---|---|---|---|
| 1 | `branding/screenshots/tables-and-diagrams.png` | 218 KB | 1600x923 | Tables and diagrams, in the light appearance |
| 2 | `branding/screenshots/document-themes.png` | 257 KB | 1600x923 | Eleven document themes, one click away |
| 3 | `branding/screenshots/print-bar.png` | 293 KB | 1600x923 | Pick the look for each print and see it before you send it |
| 4 | `branding/screenshots/page-view.png` | 174 KB | 1600x923 | Page view: the document on paper, before you print |
| 5 | `branding/screenshots/code.png` | 282 KB | 1600x923 | Syntax highlighting for about 190 languages |
| 6 | `branding/screenshots/math.png` | 195 KB | 1600x923 | Math, tags and Obsidian syntax |
| 7 | `branding/screenshots/theme-academic.png` | 215 KB | 1600x923 | The Academic theme: the look of a typeset paper |

`branding/screenshots/slideshow.gif` (1200x692, 749 KB) also qualifies, but it is the same material as
the stills, so leave it out unless an animated first impression is wanted.

## Links

| Field | Value |
|---|---|
| Website | https://github.com/bwets/MarkdownBlaze |
| Source code | https://github.com/bwets/MarkdownBlaze |
| Issues / contact | https://github.com/bwets/MarkdownBlaze/issues |
| Privacy policy | https://github.com/bwets/MarkdownBlaze/blob/main/PRIVACY.md |
| License | MIT |

## Forum request: auto-connect `removable-media`

Until the store grants it, users have to run `snap connect markdownblaze:removable-media` themselves.
Post this on [forum.snapcraft.io](https://forum.snapcraft.io) in the **store-requests** category, after
the first revision is published (the request names a snap that exists). Title:

> Request: auto-connect removable-media for markdownblaze

Body:

> Snap: markdownblaze (https://snapcraft.io/markdownblaze)
> Interface: removable-media
> Request: auto-connection on install
>
> MarkdownBlaze is a viewer for Markdown files. It opens a file or a folder the user picks and renders it
> offline; it does not edit or write documents, and it makes no network requests of its own.
>
> Users keep Markdown notes and documentation on USB drives, SD cards and other mounted volumes under
> /media and /mnt, and open them with a file manager's "Open with" or the app's own Open dialog. Without
> removable-media those files cannot be read. The `home` plug does not cover them.
> The app's use of the interface is the same as that of other file viewers and editors that are granted
> it: it reads the file the user chose and, for a folder, the Markdown files next to it.
>
> Source: https://github.com/bwets/MarkdownBlaze

Before posting, check that the claims above (viewer only, no network requests of its own) still match the app.
