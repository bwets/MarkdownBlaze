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
| ← / → | Back / Forward through your session |
| ⟳ | Refresh the current document |
| 🖨 | Print |
| 🗀 | Open the current file's containing folder |
| ⋮ | More — opens **Settings** |

## The sidebar

Three tabs keep you oriented:

- **Headers** — a live outline of the current document. Click a heading to jump to it.
- **Session** — every document you've opened this session (back/forward history).
- **Global** — a persisted history across launches. Right-click any row for
  *Open*, *Open in new window*, *Copy filename*, and *Open containing folder*.

The sidebar is resizable and pinnable — your width and pinned state are remembered.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Alt + ←` / `Alt + →` | Back / Forward |
| `F5` / `Ctrl + R` | Refresh |
| `Ctrl + P` | Print |
| `Ctrl + B` | Toggle sidebar |

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
