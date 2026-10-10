#!/usr/bin/env bash
#
# Rebuilds src/MarkdownBlaze/wwwroot/lib/highlight-content.css — highlight.js keyed to the
# *document's* lightness rather than the app's.
#
# Both palettes already sit in the bundled highlight-theme.css — the light one at the top, the dark
# one inside its .theme-dark block — so they are lifted straight out of it and re-keyed to the
# ctl/ctd markers app.js sets alongside a content theme. Only needs running when highlight-theme.css
# changes; it reads nothing but that file.
#
# The content themes themselves (lib/content-themes.css) are written by hand, not generated: each
# is a set of tokens over one shared structure, which is what keeps them to a single typeface and a
# single type scale.
#
# Usage: scripts/build-highlight-content.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LIB="$ROOT/src/MarkdownBlaze/wwwroot/lib"
HL="$LIB/highlight-theme.css"
{
  echo "/* Highlight.js palettes selected by the *content* theme's lightness (app.js sets ctl/ctd on"
  echo "   <html>), so code blocks match the document skin instead of the app chrome. Loaded after"
  echo "   highlight-theme.css, which keeps handling the \"follow app theme\" case."
  echo ""
  echo "   Generated file — do not hand-edit. See scripts/build-highlight-content.sh. */"
  echo ".ctl {"; sed -n '1,10p' "$HL"; echo "}"
  echo ".ctd {"; sed -n '12,21p' "$HL"; echo "}"
} > "$LIB/highlight-content.css"
echo "wrote $LIB/highlight-content.css ($(wc -c < "$LIB/highlight-content.css") bytes)"
