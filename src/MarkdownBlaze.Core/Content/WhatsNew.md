%%
What's new, as shown by More (⋮) → What's new. One "## " section per app version, newest first.

The release workflow numbers a version when it is published (1.0.<run number>), so the section for
the version being worked on cannot carry its number yet. It is headed {{version}}, which the app
replaces with its own version when it shows this page.

When cutting a release: nothing to do. When starting on the next one: give the section below the
number its release ended up with (see the git tags), and start a fresh "## {{version}}" above it.
%%

## {{version}}

**The document**

- **Document themes rebuilt.** Every theme now uses a single typeface for text and one for code, and
  the same modest heading scale — headings no longer jump in size from one theme to the next, and
  nothing gets bigger as it nests deeper.
- **Justified, hyphenated text** in the document's own language. The language is recognised from
  the text, or set with `lang:` in the front matter.
- **Tables** have a cleaner look: a rounded outline, a tinted header and a line between rows.
- **Code blocks** scroll sideways in *Follow app theme* and wrap in the document themes, in page
  view and on paper.
- **Printing without backgrounds** keeps every box visible: code, callouts, highlights, tags and
  table headers are outlined as well as tinted.
- **Diagrams** are set in the document's typeface.

**Safer with documents from anywhere**

- **A document can no longer run anything.** Raw HTML in a Markdown file is cleaned before it is
  shown — scripts, frames, forms and event handlers are removed — and the window can show nothing
  but MarkdownBlaze itself.
- **Links are checked before they are followed.** Web, mail, phone and FTP addresses open in the
  program that handles them. A link to a program, a script or a network share does nothing, and a
  link to another document opens only documents.
- **Pictures have to be pictures.** A file that is not an image is never shown as one, a network
  address is never contacted to draw one, and a picture that is too large is replaced by a note.
  Images in other folders still work, so a wiki can share one folder of pictures.
- **A document cannot dress up as the app or take the window over.** Only the formatting a document
  genuinely needs is kept (code, callouts, task lists, footnotes, math, diagrams); the names the app
  uses for its own toolbar, menus and sidebar are not available to it. A page that nests its markup
  a thousand levels deep, or has thousands of headings, opens normally, and one that cannot be
  drawn within 30 seconds says so instead of freezing the window.
- **You can see where a link goes before you click it.** Rest the pointer on a link — or tab to it —
  and the real address appears in the bottom left corner, whatever the link's text says.
- **Pictures written as HTML work too.** `<img src="logo.png" width="120">` next to a document is
  shown like `![](logo.png)`, and a document on a network share shows its own pictures.
- **The sample wiki has a `security/` section:** pages that try to misbehave, so you can see for
  yourself that nothing happens. The developer tools are off unless you start the app with `--devtools`.

**The app**

- **Sidebar as panels.** Folders and documents are collapsible panels; the open document's panel
  holds its headings. A folder holding the open document can now be closed.
- **Toolbar reorganised** into three groups: files on the left, over the sidebar; the sidebar
  toggle and back/forward in the middle, starting where the document does; view, zoom and theme on
  the right.
- **Zoom survives a reload.** When another program saves the file you are reading, the document no
  longer snaps back to 100%.
- **What's new** (this page) and a **guided tour** of the main features — it can be skipped or
  put off, and is available again from *Settings*.
- **Windows integration on your terms.** The installer registers MarkdownBlaze for Markdown files (under
  *Open with* and *Default apps*), folders and `markdown://` links, and removes it all again when
  uninstalled. The portable zip no longer registers anything by itself: *Settings* has an *Add to Explorer*
  button (and *Remove*) for it, and `MarkdownBlaze.exe --register` / `--unregister` do the same from a script.
- **Faster, and it stays responsive.** A document is rendered in the background, with a small
  *Rendering…* note if it takes a moment, so a large file no longer freezes the window — and a file
  over 32 MB is declined with an explanation.
- **Pictures come straight from disk** instead of being packed into the page, so a document full of
  large images opens faster and needs far less memory.
- **Editing elsewhere is smoother.** An editor that saves by replacing the file no longer makes the
  page flash *Unable to open this file*.
- **Settings and history are saved safely.** A crash or a power cut cannot leave them half-written,
  a damaged file is set aside instead of lost, and the window remembers where it was even if the app
  did not close properly.
- **A history entry whose file is gone** says so and offers to remove it, and coming back from
  *Settings* keeps your place in the document.
- **A log** of what goes wrong quietly — a file that could not be read, a link that did not open — is
  written to `%LOCALAPPDATA%\MarkdownBlaze\logs`; *Settings* shows where. `--log-level debug` adds detail.
- **Keyboard and screen readers.** Menus, dialogs, the folder tree and the toasts announce
  themselves, can be used from the keyboard, and give the focus back when they close.
- Updated to the latest Fluent UI, PhotinoX, Markdig, highlight.js, KaTeX and Mermaid.

**Earlier in this version**

- **Eleven document themes**, chosen from the toolbar or with `F9`, plus *Follow app theme*.
- **Page view** — the document laid out on A4 sheets, the way it will print. `F2` switches.
- **Print bar** — `Ctrl + P` lets you pick the theme for that one print.
- **Folder tree** in the sidebar, **zoom for the document only**, and opening from Explorer and
  from `markdown://` links.

## 1.0.21

- Updated the libraries the app is built on.

## 1.0.20

- Opening another document starts at its top.
- History can be managed: remove single entries, or clear it all from *Settings*. Entries for
  files that no longer exist are removed automatically.

## 1.0.16

- `F11` for fullscreen, `F1` and `?` for the sidebar and the list of keyboard shortcuts.
- Faster startup.
- macOS disk image.

## 1.0.13

- Obsidian and GitHub syntax: callouts, wiki links, tags, hidden comments.
- Math with KaTeX.
- The window remembers its size, position and whether it was maximised.

## 1.0.12

- An *Open file* button, a splash screen while starting, and the app icon.
- Sharp rendering on high-DPI displays.

## 1.0.11

- A welcome manual when no file is given.
- Drag a file onto the window to open it.
- More reliable links between documents.

## 1.0.8

- Windows installer, which also registers MarkdownBlaze for `.md` files.

## 1.0.4

- MarkdownBlaze as it is today: rewritten on Photino, Blazor and Fluent UI.
