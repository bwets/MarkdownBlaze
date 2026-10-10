# Scripts

Maintenance scripts for the repository. They are Bash and need nothing beyond `curl`, `tar` and
`sha256sum` (or `shasum`); none of them runs as part of a build.

## `update-vendor.sh`

Reproduces the vendored JavaScript libraries (highlight.js, Mermaid, KaTeX) in
`src/MarkdownBlaze/wwwroot/vendor/` from their upstream releases at the versions pinned at the top of the
script, and refuses to install anything whose SHA-256 differs from `vendor/SHA256SUMS`. `--check` verifies
the files already in the tree without downloading; `--accept` is for a deliberate version bump (it
installs the new files and rewrites `SHA256SUMS`, for you to review). The versions, upstream URLs and the
exact way the highlight.js bundle is assembled are in `wwwroot/vendor/VERSIONS.md`.

## `build-highlight-content.sh`

Regenerates `src/MarkdownBlaze/wwwroot/css/highlight-content.css`, the highlight.js colour palettes keyed to
the *document theme's* lightness (`.ctl` / `.ctd`) instead of the app's. It lifts the light palette and the
dark palette out of the bundled `highlight-theme.css`, finding them by its `.theme-dark {` marker rather than
by line numbers, and stops with an error if the marker or either palette is missing. Run it only when
`highlight-theme.css` changes. The document themes themselves (`css/content-themes.css`) are written by
hand, not generated.
