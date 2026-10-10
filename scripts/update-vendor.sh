#!/usr/bin/env bash
#
# Fetches the vendored JavaScript libraries — highlight.js, Mermaid and KaTeX — at the versions pinned
# below, checks every file against src/MarkdownBlaze/wwwroot/vendor/SHA256SUMS, and installs them into
# src/MarkdownBlaze/wwwroot/vendor/. The files are committed (the app has to run offline); this script
# exists so a reader can see exactly where they came from, reproduce them byte for byte, and update
# them on purpose rather than by pasting a file from somewhere.
#
# Usage:
#   scripts/update-vendor.sh            download the pinned versions, verify, install
#   scripts/update-vendor.sh --check    verify the files already in the tree; downloads nothing
#   scripts/update-vendor.sh --accept   download, install, and rewrite SHA256SUMS (after changing a version below)
#
# To update a library: change its version here and in wwwroot/vendor/VERSIONS.md and
# THIRD-PARTY-NOTICES.md, run with --accept, read the diff (git diff --stat, and the library's release
# notes), and test a document with code, a formula and a diagram (samples/wiki/syntax-showcase.md).
#
# Needs curl, tar and sha256sum (or shasum on macOS). The vendor files are marked `-text` in
# .gitattributes, so line-ending conversion never changes their hashes.
set -euo pipefail

HIGHLIGHT_VERSION=11.12.0
MERMAID_VERSION=12.1.0
KATEX_VERSION=0.19.0

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VENDOR="$ROOT/src/MarkdownBlaze/wwwroot/vendor"
SUMS="$VENDOR/SHA256SUMS"

mode="${1:-install}"
case "$mode" in
  install|--check|--accept) ;;
  *) echo "usage: $0 [--check | --accept]" >&2; exit 2 ;;
esac

sha256_check() { # sha256_check <sums-file>, run inside the directory the paths are relative to
  if command -v sha256sum >/dev/null 2>&1; then sha256sum --check --quiet "$1"; else shasum -a 256 --check --quiet "$1"; fi
}
sha256_list() { # sha256_list <file>... prints "<hash>  <file>" lines
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$@"; else shasum -a 256 "$@"; fi
}

if [ "$mode" = "--check" ]; then
  (cd "$VENDOR" && sha256_check SHA256SUMS) && echo "vendor files match SHA256SUMS"
  exit 0
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
STAGE="$WORK/stage"
mkdir -p "$STAGE/highlight" "$STAGE/mermaid" "$STAGE/katex/fonts" "$WORK/hl"

fetch() { curl --fail --silent --show-error --location --retry 3 --output "$2" "$1"; }

# highlight.js: the bundled file is the CDN build's core (highlight.min.js) followed by every language
# grammar of the same release (languages/*.min.js, in C-locale name order, each preceded by a newline),
# so that any fenced language works without a lazy per-language download. Source: the npm package
# @highlightjs/cdn-assets, which is what highlightjs.org's CDN serves.
echo "highlight.js $HIGHLIGHT_VERSION"
fetch "https://registry.npmjs.org/@highlightjs/cdn-assets/-/cdn-assets-$HIGHLIGHT_VERSION.tgz" "$WORK/hl.tgz"
tar -xzf "$WORK/hl.tgz" -C "$WORK/hl"
{
  cat "$WORK/hl/package/highlight.min.js"
  for f in $(cd "$WORK/hl/package/languages" && LC_ALL=C ls ./*.min.js | LC_ALL=C sort); do
    printf '\n'
    cat "$WORK/hl/package/languages/$f"
  done
} > "$STAGE/highlight/highlight.min.js"

# Mermaid: the single-file browser bundle (it contains its own dependencies).
echo "Mermaid $MERMAID_VERSION"
fetch "https://cdn.jsdelivr.net/npm/mermaid@$MERMAID_VERSION/dist/mermaid.min.js" "$STAGE/mermaid/mermaid.min.js"

# KaTeX: script, stylesheet, and the .woff2 fonts the stylesheet names (the stylesheet also lists .woff
# and .ttf fallbacks, which WebView2 never asks for, so those are not shipped).
echo "KaTeX $KATEX_VERSION"
KATEX_URL="https://cdn.jsdelivr.net/npm/katex@$KATEX_VERSION/dist"
fetch "$KATEX_URL/katex.min.js" "$STAGE/katex/katex.min.js"
fetch "$KATEX_URL/katex.min.css" "$STAGE/katex/katex.min.css"
for font in $(grep -o 'fonts/[A-Za-z0-9_-]*\.woff2' "$STAGE/katex/katex.min.css" | LC_ALL=C sort -u); do
  fetch "$KATEX_URL/$font" "$STAGE/katex/$font"
done

# Verify (or, with --accept, record) before anything in the tree is touched.
(cd "$STAGE" && sha256_list $(find . -type f | LC_ALL=C sort | sed 's#^\./##') > "$WORK/SHA256SUMS.new")
if [ "$mode" = "--accept" ]; then
  cp "$WORK/SHA256SUMS.new" "$SUMS"
  echo "SHA256SUMS rewritten"
else
  if ! (cd "$STAGE" && sha256_check "$SUMS" 2>"$WORK/err"); then
    cat "$WORK/err" >&2 || true
    echo "error: a downloaded file does not match $SUMS; nothing was installed." >&2
    echo "       (after a deliberate version change, run with --accept and review the diff)" >&2
    exit 1
  fi
fi

mkdir -p "$VENDOR"
cp -R "$STAGE/." "$VENDOR/"
echo "installed into $VENDOR"
