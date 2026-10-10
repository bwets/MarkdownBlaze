#!/usr/bin/env bash
#
# Rebuilds src/MarkdownBlaze/wwwroot/css/highlight-content.css — highlight.js keyed to the
# *document's* lightness rather than the app's.
#
# Both palettes already sit in the bundled highlight-theme.css — the light one at the top, the dark
# one inside its .theme-dark block — so they are lifted straight out of it and re-keyed to the
# ctl/ctd markers js/theme.js sets alongside a content theme. Only needs running when highlight-theme.css
# changes; it reads nothing but that file.
#
# The two palettes are found by the `.theme-dark {` marker line and the closing `}` that follows it,
# not by line numbers, so a theme file that gains or loses a line still splits correctly. If the
# marker is missing or either palette comes out empty the script stops instead of writing garbage.
#
# The content themes themselves (css/content-themes.css) are written by hand, not generated: each
# is a set of tokens over one shared structure, which is what keeps them to a single typeface and a
# single type scale.
#
# Usage: scripts/build-highlight-content.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CSS="$ROOT/src/MarkdownBlaze/wwwroot/css"
HL="$CSS/highlight-theme.css"
MARKER='.theme-dark {'

if ! grep -qxF "$MARKER" "$HL"; then
  echo "error: no line reading '$MARKER' in $HL" >&2
  exit 1
fi

# Light palette: everything above the marker. Dark palette: everything between the marker and the
# closing brace that ends the block. (tr drops the CRs a Windows checkout may carry.)
LIGHT="$(tr -d '\r' < "$HL" | awk -v m="$MARKER" '$0 == m { exit } { print }')"
DARK="$(tr -d '\r' < "$HL" | awk -v m="$MARKER" 'found && $0 == "}" { exit } found { print } $0 == m { found = 1 }')"

if [ -z "$LIGHT" ] || [ -z "$DARK" ]; then
  echo "error: could not find both palettes in $HL (light: ${#LIGHT} chars, dark: ${#DARK} chars)" >&2
  exit 1
fi

{
  echo "/* Highlight.js palettes selected by the *content* theme's lightness (js/theme.js sets ctl/ctd on"
  echo "   <html>), so code blocks match the document skin instead of the app chrome. Loaded after"
  echo "   highlight-theme.css, which keeps handling the \"follow app theme\" case."
  echo ""
  echo "   Generated file — do not hand-edit. See scripts/build-highlight-content.sh. */"
  echo ".ctl {"; printf '%s\n' "$LIGHT"; echo "}"
  echo ".ctd {"; printf '%s\n' "$DARK"; echo "}"
} > "$CSS/highlight-content.css"
echo "wrote $CSS/highlight-content.css ($(wc -c < "$CSS/highlight-content.css") bytes)"
