# Welcome to MarkdownBlaze

A fast, **offline Markdown viewer**. You're seeing this page because MarkdownBlaze was
started without a file. Open a `.md` file to start reading — everything below is a quick tour.

:::tip How to open a document
- **Drag & drop** a `.md` file onto this window, or
- Right-click a `.md` file in your file manager → **Open with → MarkdownBlaze**, or
- From a terminal: `MarkdownBlaze "path/to/file.md"`
:::

## The toolbar

| Button | Action |
|---|---|
| ☰ | Toggle the sidebar |
| ← / → | Back / Forward through the documents you've read |
| 🗁 | Open a file |
| 🗂 | Open a folder — the whole folder appears in the sidebar |
| ▤ | View — continuous or pages (see below) |
| ⟳ | Refresh the current document |
| 🖨 | Print — opens the print bar (see below) |
| 🗀 | Open the current file's containing folder |
| − 100% + ↺ | Zoom the document out or in, and back to normal size |
| ◐ | Document theme — how the document itself looks |
| ⋮ | More — opens **Settings** |

## The sidebar

Two tabs keep you oriented:

- **Headers** — the folder you are working in, as a tree. Folders and other documents stay closed;
  the document you are reading is open, with its own headings listed underneath. Click a document to
  read it, a heading to jump to it, or a folder to open and close it.
- **History** — everything you have opened, across launches. Entries keep their place when you open
  them again, so a document you use often stays where you last saw it. Right-click any row for
  *Open*, *Open in new window*, *Copy filename*, and *Open containing folder*.

The sidebar is resizable and pinnable — your width and pinned state are remembered.

### The folder you are working in

Whatever you point MarkdownBlaze at sets the folder the tree shows: open `notes/Report.md` and you
get the `notes` folder. Point it at a folder instead — from the 🗂 button, the command line, or
Explorer — and it opens the document that folder leads with: its `index`, `main` or `readme` if it
has one, otherwise the first document in alphabetical order.

Following a link or a history entry does *not* move the root, so you can wander and still have the
folder you started in beside you. Whenever you change document, the tree scrolls it to the top.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Alt + ←` / `Alt + →` | Back / Forward |
| `F5` / `Ctrl + R` | Refresh |
| `Ctrl + P` | Print — once to choose a theme, again to print |
| `F2` / `Shift + F2` | Next / previous view |
| `F9` / `Shift + F9` | Next / previous document theme |
| `Ctrl` + wheel, `Ctrl +` / `Ctrl -` | Zoom the document (`Ctrl 0` resets) |
| `F11` | Fullscreen (`Esc` leaves it) |
| `Ctrl + B` | Toggle sidebar |

## How the document is laid out

The ▤ button offers two ways to look at the same document, and `F2` / `Shift + F2` step through them:

- **Continuous** — one column that scrolls without end. This is the reading view, and the default.
- **Pages** — the document laid onto sheets of paper, with the margins and the breaks it will have
  when printed. Choose **full width** to read a sheet at a comfortable size, or **whole page** to see
  a complete sheet at once and judge the layout.

Page view is a preview of the printed result, so it is the quickest way to check where a document
falls before sending it to the printer. Your choice is remembered.

**Zoom** from the toolbar — **−**, the current percentage, **+**, and **↺** to go back to normal — or
with `Ctrl` + the mouse wheel, `Ctrl +` and `Ctrl -`, or a pinch on the trackpad; `Ctrl 0` resets.
However you change it, the percentage in the toolbar follows. Only the document changes size — the toolbar and sidebar stay put, and the
app itself never scales. In page view, zoom multiplies whichever fit you picked, so `Ctrl 0` returns
you to a clean *full width* or *whole page*. Your zoom is remembered.

## Themes for the document

The ◐ button in the toolbar changes how the document itself looks — the paper colour, the typeface
and the spacing. `F9` and `Shift + F9` step through the list without opening it, naming each theme
briefly at the foot of the window as you go. None of it changes the app around the document: the
toolbar and sidebar keep the light or dark appearance you picked under *Appearance*.

| Theme | Looks like |
|---|---|
| Follow app theme | The app's own look — light or dark, matching *Appearance*. |
| Book | Large serif with generous spacing, for reading end to end. |
| Classic | Compact serif — the plain printed page. |
| Elegant | Airy serif with wide margins, for something you hand over. |
| Newspaper | Dense serif on newsprint — fits the most on a sheet. |
| Academic | The look of a typeset paper, in the LaTeX typeface. |
| Report | Clean sans with ruled headings. |
| Modern | Roomy sans, the look of a web page. |
| Simple | Compact modern sans. |
| Writer | Soft grey writing paper, easy on the eyes. |
| Typewriter | Monospaced draft on off-white paper, roomy for notes. |
| Night | Light text on a dark page, for reading after dark. |

## Printing

Press `Ctrl + P` (or the 🖨 button) and a bar appears along the bottom of the window with the
themes above. Pick one and the document changes as you look at it, so you see what will come out
of the printer. Press `Ctrl + P` again — or the **Print** button in the bar — to send it. `Esc`
closes the bar and puts the document back.

The theme you pick in the bar is for that one print; the one on the ◐ button is untouched.
Only the document is printed — the toolbar, sidebar and any open menu are left out.

Along the bottom of each sheet the browser prints the page number and the address of the document —
`markdown://document/#C:/notes/Report.md` — with the document's name and the date across the top. To
print without any of it, untick **Headers and footers** in the print dialog; the page numbers go too,
as they come as a set.

## Opening from Windows

MarkdownBlaze settles into Windows the first time it runs, with nothing to switch on:

- **Right-click menu** — *Open with MarkdownBlaze* on Markdown files and on folders in Explorer. On
  Windows 11 it lives under *Show more options*.
- **markdown:// links** — the address in this window is the document you are reading, and it works
  as a link. Anything that can open a URL can hand a document over:

  ```text
  markdown://C:/notes/Report.md
  ```

Both are registered for you alone, so no administrator rights are involved, and they repair
themselves if the app is moved or reinstalled elsewhere. Installed packages on Linux and macOS
register the link handler themselves.

## What renders

MarkdownBlaze supports CommonMark plus a rich set of extensions:

- **Tables**, **task lists**, footnotes, and auto-linked headings.
- **Syntax highlighting** for ~190 languages:

  ```csharp
  Console.WriteLine("Hello from MarkdownBlaze!");
  ```

- **Mermaid diagrams**:

  ```mermaid
  flowchart LR
      A[Open .md file] --> B{Has links?}
      B -- yes --> C[Click to navigate in-app]
      B -- no --> D[Read & enjoy]
  ```

- **Admonitions / callouts** in Docusaurus, MkDocs, **and** GitHub/Obsidian styles:

  > [!note] Callouts
  > `> [!note] Title` (GitHub/Obsidian), `:::note` (Docusaurus), and `!!! note` (MkDocs) all
  > work. Add `+`/`-` after an Obsidian type — `> [!tip]- Collapsed` — for a collapsible callout.

- **Math** via KaTeX — inline `$e^{i\pi}+1=0$`, display `$$…$$`, and ```` ```math ```` blocks.
- **Emoji shortcodes** like `:rocket:` → 🚀 and `:tada:` → 🎉.
- **YAML front matter** is parsed and hidden.
- Local **images are inlined**, so documents render fully offline — no network access.

## Obsidian & GitHub extras

MarkdownBlaze aims to be a universal viewer, so vault- and repo-flavoured syntax renders too:

- **Wiki links** — `[[Page]]`, `[[Page|alias]]`, and `[[Page#Heading]]` navigate in-app.
  Image embeds `![[image.png]]` are inlined; a note embed `![[Other Note]]` becomes a link.
- **Tags** — `#project`, `#todo/study` render as pills (a leading `#␣` is still a heading, and
  `C#` / `#123` are left alone).
- **Comments** — Obsidian `%%…%%` (inline or on their own `%%`-fenced lines) are hidden.
- **Highlight / strikethrough / sub- & superscript** — `==mark==`, `~~del~~`, `H~2~O`, `x^2^`.
- **GitHub alerts** — the `> [!NOTE]` / `[!TIP]` / `[!WARNING]` family (see callouts above).

## Links & navigation

- Relative links resolve against the opened file's folder. Extension-less links (and `[[wiki
  links]]`) get `.md`; folder links resolve to `index.md`.
- Edit a file while it's open and the view **auto-refreshes**.

---

Learn more at the [MarkdownBlaze project page](https://github.com/bwets/MarkdownBlaze).
