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
