# Vendored libraries

The libraries below are committed as files, because the viewer has to work offline and must not
fetch code from a CDN at run time. They are loaded on demand by `wwwroot/js/render.js` (a library is only
requested when a document contains code, a formula or a diagram). Nothing here is edited by hand.

| Library | Version | Licence | Files | Upstream |
|---|---|---|---|---|
| highlight.js | 11.12.0 | BSD-3-Clause | `highlight/highlight.min.js` | <https://github.com/highlightjs/highlight.js> |
| Mermaid | 12.1.0 | MIT | `mermaid/mermaid.min.js` | <https://github.com/mermaid-js/mermaid> |
| KaTeX | 0.19.0 | MIT | `katex/katex.min.js`, `katex/katex.min.css`, `katex/fonts/*.woff2` | <https://github.com/KaTeX/KaTeX> |

Licence texts are in [`THIRD-PARTY-NOTICES.md`](../../../../THIRD-PARTY-NOTICES.md) at the repository root,
which also ships with the app.

## Where each file comes from

- **highlight.js** — the npm package `@highlightjs/cdn-assets@11.12.0`
  (<https://registry.npmjs.org/@highlightjs/cdn-assets/-/cdn-assets-11.12.0.tgz>, the same files
  highlightjs.org's CDN serves). `highlight/highlight.min.js` is that package's `highlight.min.js`
  (the core with the common languages) followed by every `languages/*.min.js` (193 grammars), each
  preceded by a newline, so that any fenced language is highlighted without a per-language download.
  The upstream CDN file on its own would highlight only the common languages.
- **Mermaid** — <https://cdn.jsdelivr.net/npm/mermaid@12.1.0/dist/mermaid.min.js>, unchanged. The
  file is a single bundle that includes Mermaid's own dependencies.
- **KaTeX** — <https://cdn.jsdelivr.net/npm/katex@0.19.0/dist/katex.min.js>, `.../katex.min.css` and the
  `.woff2` fonts under `.../fonts/`, unchanged. The stylesheet also names `.woff` and `.ttf`
  fallbacks; they are not shipped because WebView2 uses the `.woff2`. `katex.min.css` refers to
  its fonts as `fonts/<name>`, relative to the stylesheet, which is why the folder layout is kept.

Neighbouring files that are *not* vendored libraries: `wwwroot/css/highlight-theme.css` is
highlight.js's "GitHub" and "GitHub Dark" themes (BSD-3-Clause, see the notices);
`wwwroot/css/highlight-content.css` is generated from it by `scripts/build-highlight-content.sh`; and
`wwwroot/fonts/latin-modern/` is the Latin Modern Roman typeface (GUST Font License).

## Checking and updating

`SHA256SUMS` in this folder lists the SHA-256 of every vendored file. The files are marked `-text` in
`.gitattributes`, so a checkout never changes a byte of them.

```sh
scripts/update-vendor.sh --check    # do the files in the tree match SHA256SUMS?
scripts/update-vendor.sh            # re-download the pinned versions, verify them, reinstall
scripts/update-vendor.sh --accept   # after bumping a version in the script: download, install, rewrite SHA256SUMS
```

To update a library, change its version in `scripts/update-vendor.sh`, in the table above and in
`THIRD-PARTY-NOTICES.md`, run `--accept`, read the release notes and the diff, then open
`samples/wiki/syntax-showcase.md` and a document with code and a Mermaid diagram to see that all three
render. The Content-Security-Policy in `index.html` allows no `unsafe-eval`; if a newer Mermaid needs
it, that is a decision to make, not a setting to loosen quietly.
