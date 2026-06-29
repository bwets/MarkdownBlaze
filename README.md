# MarkdownBlaze

A fast, **offline Markdown viewer** built with **[Photino](https://www.tryphotino.io/) + Blazor**
and **[Microsoft Fluent UI](https://www.fluentui-blazor.net/)** on **.NET 10**. Point it at a `.md`
file and it renders a clean, navigable document — syntax highlighting, Mermaid diagrams,
admonitions, a live outline, and history — entirely offline, in a native OS WebView.

> 📖 The [`docs/`](docs/index.md) folder is a self-contained sample wiki that doubles as a feature
> tour. Open it with `MarkdownBlaze docs/index.md`.

---

## Features

### Rendering
- **CommonMark + extensions** via [Markdig](https://github.com/xoofx/markdig) (`UseAdvancedExtensions`): tables, task lists, footnotes, auto-identifiers, …
- **Syntax highlighting** with a bundled [highlight.js](https://highlightjs.org/) (all ~190 languages). Light/dark aware.
- **Mermaid diagrams** — ` ```mermaid ` blocks render as live diagrams.
- **Admonitions / callouts** in **both** Docusaurus (`:::tip`) and MkDocs (`!!! note`, `??? note`) syntaxes.
- **YAML front matter** is parsed and hidden.
- Local **images are inlined** (data URIs) so they load inside the WebView. Everything is offline — no network access.

Rendering is provided by the standalone [`bwets.Markdig.Extensions`](https://github.com/bwets/bwets.Markdig.Extensions) NuGet package.

### Links & navigation
- **Smart link rewriting** relative to the opened file's folder: extension-less wiki links get `.md`; folder links resolve to `index.md`.
- Clicking a local Markdown link **opens it in-app**; external links open in the system browser.
- **Back / Forward** session history plus a persisted **global history**.
- **Auto-refresh**: edit the file and the view reloads automatically.

### UI (Fluent)
- **Resizable, pinnable sidebar** with three tabs — **Headers** (outline), **Session** history, **Global** history. Preferences are remembered.
- **Toolbar**: sidebar toggle, Back, Forward, Refresh, Print, Open containing folder, and a ⋮ menu → Settings.
- History rows show the **page title** (path in a tooltip) with a **right-click menu**: Open, Open in new window, Copy filename, Open containing folder.
- **Light / Dark / System** theme (Settings), driven by Fluent design tokens.

### Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Alt + ←` / `Alt + →` | Back / Forward |
| `F5` / `Ctrl + R` | Refresh |
| `Ctrl + P` | Print |
| `Ctrl + B` | Toggle sidebar |

---

## Getting started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- **Windows:** the [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (preinstalled on current Windows)
- **Linux:** WebKitGTK + GTK — `webkit2gtk-4.1` and `gtk3` (Arch/CachyOS: `sudo pacman -S --needed gtk3 webkit2gtk-4.1 dbus`)

### Build & run

```bash
dotnet run --project src/MarkdownBlaze -- "docs/index.md"
```

### Releases & packaging
Prebuilt, self-contained binaries (Windows zip, Linux `tar.gz`/`.deb`, AUR) are produced by the
manual [`release` workflow](packaging/README.md). On Linux, `packaging/linux/install-linux.sh`
installs a `.desktop` entry and registers MarkdownBlaze as the default `.md` handler.

---

## Tech stack
- **.NET 10**, **PhotinoX.Blazor** (native OS WebView host), **Microsoft Fluent UI Blazor**
- **Markdig** + **bwets.Markdig.Extensions** for Markdown → HTML
- **highlight.js** + **Mermaid** (bundled offline) running in the WebView

## Project layout
```
docs/                       Sample wiki / feature showcase
src/
  MarkdownBlaze/
    Program.cs              Photino + Blazor host
    App.razor               Root (theme, view switch)
    ViewerView.razor        Toolbar + sidebar + content
    SettingsView.razor      Settings page
    Services/               Rendering, navigation/history, file-watch, JS interop
    wwwroot/                Host page, app.css/js, offline highlight.js + mermaid
  packaging/                Linux .desktop + installer, .deb builder, AUR PKGBUILD
```

## License
Personal project — all rights reserved unless stated otherwise.
