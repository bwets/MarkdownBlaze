# Architecture

MarkdownBlaze is a native window (PhotinoX, which hosts the operating system's own web view) running a
Blazor UI built with Microsoft Fluent UI, on .NET 10. A document is turned into HTML by Markdig on the
.NET side, cleaned by an allow-list sanitizer, and shown in the page; a small amount of JavaScript
(the ES modules in `src/MarkdownBlaze/wwwroot/js/`) does what only the browser can do: measure, scroll, print, and
draw code, formulas and diagrams. The page is served from the app's own `markdown://` scheme, and
everything it needs to render is bundled, so nothing is fetched at start-up.

This page records the three rules the code is built around, and the trust boundary. The comments in the
source explain each rule where it bites; this is the overview to read first.

## Three invariants

### 1. Blazor owns the document element; renderers change what is there, never what is in the tree

The rendered Markdown is one element (`.content.markdown-body` in `ViewerView.razor`) whose children
Blazor has put there as a `MarkupString`. highlight.js, KaTeX and Mermaid then write into those children
(coloured spans, math markup, an SVG replacing a `<pre>`), and the pagination code adjusts them.

Blazor keeps a virtual render tree that describes the DOM it believes exists. Inserting nodes among its
children, or removing them, leaves that tree describing a DOM that no longer exists, and the next diff
blanks the document. So:

- the document element carries an `@key` that changes on every navigation, which makes Blazor discard
  and rebuild the whole subtree instead of diffing it against markup the renderers have edited;
- renderers and layout code **mutate in place** (change attributes, text, styles and classes of the
  nodes that are there) and **never add or remove nodes** in the document's tree. When something has to
  appear (a page gap, a marker), it is painted with CSS or marked with a `data-` attribute on an
  existing node;
- elements Blazor owns elsewhere (the sidebar rows, for example) are marked with data attributes, not
  classes, for the same reason.

### 2. The document's theme is separate from the app's theme

There are two independent looks. The **app theme** (Light, Dark or System; Fluent UI tokens) styles the
chrome: toolbar, sidebar, dialogs, settings. The **document theme** (eleven of them, plus "Follow app
theme") styles only the document: paper colour, typeface, size and spacing.

A document theme is a set of `--ct-*` CSS tokens selected by a `ct-<id>` class on `<html>`, together with
a `ctl` / `ctd` marker that says whether the document reads light or dark. The shared structure in
`wwwroot/css/content-themes.css` reads those tokens, so a rule that must hold for every theme (one
typeface, one body size, one heading scale, outlined boxes) holds by construction. The marker, not the
app theme, decides the highlight.js palette (`highlight-content.css`) and the Mermaid theme, so code and
diagrams match the page they are on. Neither theme reads the other: a dark app can show a white
Book-theme page, and the other way round. Printing uses the print bar's own choice of theme for that one
print.

### 3. Pagination is CSS-only

Page view lays the document onto A4 sheets, the way it will print, without any page elements. The
document element is given the width of a sheet; a repeating `linear-gradient` on that same element paints
the paper, its edges and the gaps between sheets; a CSS `drop-shadow` follows the gradient's alpha so each
sheet has its own shadow. JavaScript only **measures** the flow and sets a `margin-top` on a block that
would otherwise straddle a gap, pushing it to the next sheet. It adds no nodes (invariant 1), and it
drops back to the plain flow before printing, because the printer paginates by itself with `@media print`
rules (`break-inside: avoid` on figures, tables, code and callouts). Fit-to-width and whole-page zoom are
a single `--doc-zoom` value held on the pane.

## Threading model

**The UI thread owns the state.** `NavigationService` (the current document, the back/forward stack) and
`ViewerState` (zoom, view, popups, the sidebar's rows) are single-threaded by construction: they are not
locked, every member is called on the Blazor renderer's thread, and every event they raise is raised
there. Debug builds assert it (`Debug.Assert(CheckAccess())`), so a mutation from the wrong thread fails
at once instead of corrupting the render tree later.

The work that happens elsewhere is handed back through `IUiDispatcher` (`Services/IUiDispatcher.cs`):
`CheckAccess()`, and `InvokeAsync` for an action or an async function. `ViewerView` implements it with
its own `InvokeAsync` — the renderer's dispatcher — and attaches itself to `NavigationService` and
`ViewerState` when it is created; until then (and in tests) the default runs the work where it is
called. Three kinds of thread feed the UI thread this way:

- **the render itself**, on the thread pool: a document is read, parsed and sanitized off the UI thread,
  and only the finished result is applied on it. A newer navigation, reload or drop cancels the render in
  flight, and a superseded result is never applied;
- **timers**: the file watcher's debounce (a reload), the toast, the debounced saves of the preferences
  and the history (these only write files);
- **callbacks from the page**: the JavaScript interop calls the instance methods of `ViewerInterop`, which
  `PageEvents` turns into a command or a navigation on the UI thread.

Work that is started and not awaited goes through `Background.Run` / `Background.Observe`
(`Services/Background.cs`), which write the failure of a discarded task to the log instead of losing it.

## How a document gets to the screen

1. **Ask.** A click, a history row, a key, a drop or the command line becomes `NavigationService.NavigateAsync`
   (or `ReloadAsync` when the file watcher fires). It updates the back/forward stack at once, starts watching
   the file, and starts the render on the thread pool, cancelling the one before it.
2. **Read.** `MarkdownService.RenderFile` opens the file with a share mode that tolerates an editor's lock and
   refuses anything over 32 MB.
3. **Parse.** Markdig parses it with the `bwets.Markdig.Extensions` pipeline (front matter, callouts, wiki
   links, math, tags, …). The title, the headings and the document's language are taken from the tree.
4. **Rewrite links and images.** Relative links become in-app navigation (`data-nav`) or, if unsafe, inert
   ones; each local image that `LocalPath` allows gets a per-render token in its place.
5. **Render** the tree to HTML.
6. **Sanitize.** The HTML goes through the allow-list in `HtmlSanitizer`, which removes everything an author
   could have used to run code — including any `data-nav` or `mdasset:` address the author wrote.
7. **Restore tokens.** The renderer's own `data-nav` targets and `mdasset://doc/<token>/…` image addresses
   are swapped in after the sanitizer, so the only ones on the page are the ones it made.
8. **Apply, on the UI thread.** `NavigationService.Apply` takes the result through the dispatcher: it sets
   the current HTML, title, headings and language, hands the image grant to `AssetScope` (so the
   handler serves this render's pictures and no other's), records the history entry, bumps `RenderToken` and
   raises `Changed`.
9. **Draw.** `ViewerView` re-renders; the document element is `@key`ed on the token, so Blazor rebuilds it
   from the new markup (invariant 1). In `OnAfterRenderAsync` it sees the new token and calls the page's
   `renderDocument` through `ViewerInterop`.
10. **Finish in the page.** `renderDocument` (JavaScript) lays the document out and marks the outline at
    once, then highlights code, typesets math and draws diagrams in place, and lays the pages out again
    when those are done and the fonts are ready. The WebView fetches the pictures from the `mdasset://`
    handler as it draws them.

## The trust boundary

**A Markdown file is untrusted input, and so is the page that renders it.** MarkdownBlaze is the handler
for `.md` files and for `markdown://` addresses, so any downloaded file, colleague's wiki page or web
link can drive it. What stands between a hostile document and the user's machine is a set of independent
layers, each assuming the one before it failed.

**The sanitizer** (`Security/HtmlSanitizer.cs`, built on HtmlSanitizer and AngleSharp). Markdig passes raw
HTML through, so the rendered body is run through an allow-list before it reaches Blazor, in the one place
every rendering path goes through (`MarkdownService.RenderCore`: files, dropped text, What's new). Only
the tags Markdig and its extensions emit, plus the harmless HTML authors write in Markdown (`details`,
`kbd`, tables, task-list checkboxes), survive. Scripts, frames, forms, `<meta>`, `<base>`, `<svg>`,
event-handler attributes, `style` (except `text-align`) and URL attributes with a scheme outside the
allow-list are removed, not escaped.

**The link allow-list** (`Security/ExternalLinks.cs`; the page is handed the list when it starts, it keeps no copy). A click
on an external link hands the address to the operating system only if its scheme is on a short list whose
handlers are a browser, a mail client or a dialler: `http`, `https`, `mailto`, `tel`, `ftp`. `file:`,
`javascript:`, `data:`, `vbscript:`, drive letters, UNC paths and the Windows URL handlers with a history
of code execution (`ms-msdt:`, `search-ms:`) are dropped, and the rewriting pass in `MarkdownService`
turns such links into an inert `href="#"`, so a click does nothing. In-app navigation (`data-nav`) is only
followed for files the viewer can open (`NavigationService.IsFollowable`).

**The local-path policy** (`Security/LocalPath.cs`). A path in a document is a request, not an
instruction. Only files with an image extension are shown as images, never as
`application/octet-stream`. UNC and device paths (`\\server\share`, `//server/share`, `\\?\`, `\\.\`) are
refused outright, because opening one makes Windows authenticate to a host the document's author chose.
A single image is capped at 20 MB and a document's images at 64 MB, and a refused image shows a
placeholder, not an error. The same helper guards `markdown://` addresses. A document itself is read only
up to 32 MB, with a share mode that tolerates an editor's lock.

**The image scheme** (`Security/AssetHandler.cs`, `Services/AssetScope.cs`). The pictures of a document are
not carried in the page: the renderer gives each allowed one the address
`mdasset://doc/<token>/<path>` (the path as the document wrote it, escaped into a single segment so that
the browser cannot fold a `..` into the token) and the WebView host serves it from disk, as a second
custom scheme beside `markdown://`. There is one decision about which images may be shown,
`LocalPath.TryOpenImage`; the renderer asks it to choose between a picture and a placeholder, and the
handler asks it again for every request, so the two cannot disagree. The token is random per render and
`AssetScope` (set by `NavigationService` when a render is put on the page, cleared when the page shows
something with no pictures) holds the one that is current: a request carrying any other — from a
document that has been replaced, or one an author guessed — is refused, and a refusal is a 404. The
sanitizer never lets an `mdasset:` address through, in `src` or `href`: the only ones on the page are
those the renderer swaps in afterwards. Images anywhere on disk are served (decision SEC-4: an image
type that is not a UNC or device path), which is the reach images had before they were served this way; the page, not
the handler, is what a document controls, and `connect-src 'self'` stops a script from reading the
bytes back out with `fetch`.

**The Content-Security-Policy** (`wwwroot/index.html`). The page may load code and styles only from itself
(`script-src 'self'`, no `unsafe-eval`), may not embed frames or objects, submit forms or change its base
address, and may connect only back to its own origin, so a script that got past the sanitizer cannot post
data out. `img-src` allows `https:` and `http:` on purpose: a document may show a picture from the web,
and the web view then fetches it as a browser would (see `PRIVACY.md`). That is the only way the app
touches the network, and it is the document author's choice, not the app's. `mdasset:` is the
app's own scheme for the open document's pictures (above).

**The web view is locked down** (`Program.ConfigureWebView`). Developer tools exist only in debug builds
or with `--devtools`; the engine's own context menu (Reload, View source, Inspect) is never shown;
browser permissions are not granted; navigation away from the app's own origin is cancelled; and
`target="_blank"` or `window.open` never open a window: a web address goes through the link allow-list to
the browser, anything else is dropped.

**No shell-out with document-supplied strings.** Everything the app starts, it starts through one seam,
`IShellLauncher` (`Platform/ShellLauncher.cs`; tests use a fake that records what it was asked). The callers
ask `ExternalLinks` first and the launcher asks it again, so an address that is not on the allow-list never
reaches the shell. The only things started are the user's default browser or mail client for an
allow-listed address, the Store's review page (a fixed address of the app's own), `explorer.exe` (by its full
path under the Windows folder), `open` or `xdg-open` for "open containing folder", and another copy of
itself for "open in new window". Arguments are passed as a list, never as a quoted string, and the user's profile is the working
directory, so nothing in a document's folder can shadow a program. Windows registration (the ProgId and
"Open with" entries for documents, the folder verbs and the `markdown://` handler) is written by the installer,
or by the app on request (Settings, `--register`) — never at startup, and not at all in the MSIX package. The app
writes it with `Microsoft.Win32.Registry` under `HKEY_CURRENT_USER`, never by running `reg.exe`.

**The bridge.** The page talks to .NET through instance methods of one object (`Interop/ViewerInterop.cs`),
which the page was handed a `DotNetObjectReference` to when it started: there is no static entry point
that a script in a document could find by assembly name. What the page reports is still treated as a
request — a link target is only followed if it is a supported document that exists, a key only counts if
it is one the command registry gave the page to report, and `data-nav`, the attribute that marks an
in-app link, is removed by the sanitizer and written by the renderer after it, so a document cannot forge
one. The sanitizer and the CSP still come first.

To report a problem, see [SECURITY.md](../SECURITY.md). The pages in
[`samples/wiki/security/`](../samples/wiki/security/index.md) show what a hostile document's payloads look
like, and a test renders every one of them and checks that none survives.

## Where things are

| Concern | Where |
|---|---|
| Markdown to HTML, startup file, app version | `Services/MarkdownService.cs` |
| Back/forward, links, file watching; the one place document state changes (UI thread only) | `Services/NavigationService.cs` |
| Starting programs: the browser, another window, the file manager | `Platform/IShellLauncher.cs`, `Platform/ShellLauncher.cs` |
| Returning to the UI thread; work nobody awaits | `Services/IUiDispatcher.cs`, `Services/Background.cs` |
| Sanitizer, link allow-list, local-path policy | `Security/` |
| Settings and history on disk (atomic, debounced) | `Services/PreferencesStore.cs`, `HistoryStore.cs`, `JsonFile.cs`, `SaveDebouncer.cs` |
| The local log file | `Services/FileLoggerProvider.cs`, `Services/Log.cs` |
| Calls from the page into .NET; native dialogs | `Interop/` |
| Windows only: Explorer "Open with" and folder verbs, `markdown://` handler, package detection, window icon | `Platform/Windows/` (`ShellIntegration`, `PackageIdentity`, `WindowIcon`) |
| Rendering, pagination, zoom, print, tour | `wwwroot/js/` (`main.js` and the modules it imports) |
| Vendored highlight.js, Mermaid, KaTeX | `wwwroot/vendor/` (pinned by `VERSIONS.md` and `SHA256SUMS`) |

All paths in the table are relative to `src/MarkdownBlaze/`.
