#!/usr/bin/env bash
#
# Rebuilds the generated stylesheets behind the document themes:
#
#   src/MarkdownBlaze/wwwroot/lib/content-themes.css     the eleven themes
#   src/MarkdownBlaze/wwwroot/lib/highlight-content.css  highlight.js keyed to the document's theme
#   src/MarkdownBlaze/wwwroot/lib/fonts/lmroman10-*.woff the Academic theme's typeface
#
# Both CSS files are committed, so this only needs running to add a theme, change a palette, or pick
# up a fix from upstream. It is offline-safe in the sense that the *app* never fetches anything —
# but the script itself downloads the upstream stylesheets, so it needs a network connection.
#
# Sources, all permissively licensed:
#   markdowncss (splendor, modest, retro)          MIT   github.com/markdowncss
#   markdown-styles layouts (jasonm23-*, mixu-page,
#     markedapp-byword, witex)                     BSD-3 github.com/mixu/markdown-styles
#   sakura.css (earthly, vader)                    MIT   github.com/oxalorg/sakura
#   Typora default themes (newsprint)              MIT   github.com/typora/typora-default-themes
#   Latin Modern (witex assets)                    GUST font licence
#
# Each upstream sheet was written to style a whole page. Getting one into the app means stripping
# what cannot come along, scoping the rest to the document element, and bridging its palette onto
# the app's own --md-* tokens — the three functions below, in that order.
#
# Usage: scripts/build-content-themes.sh [output.css]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LIB="$ROOT/src/MarkdownBlaze/wwwroot/lib"
OUT="${1:-$LIB/content-themes.css}"
C="$(mktemp -d)"
trap 'rm -rf "$C"' EXIT

fetch() { # <name> <url>
  curl -fsSL -o "$C/$1" "$2" || { echo "could not fetch $1 from $2" >&2; exit 1; }
}

MS="https://raw.githubusercontent.com/mixu/markdown-styles/master/layouts"
echo "Fetching upstream stylesheets…"
fetch splendor.css          "https://cdn.jsdelivr.net/gh/markdowncss/splendor@master/css/splendor.css"
fetch modest.css            "https://cdn.jsdelivr.net/gh/markdowncss/modest@master/css/modest.css"
fetch retro.css             "https://cdn.jsdelivr.net/gh/markdowncss/retro@master/css/retro.css"
fetch sakura-earthly.css    "https://cdn.jsdelivr.net/npm/sakura.css/css/sakura-earthly.css"
fetch sakura-vader.css      "https://cdn.jsdelivr.net/npm/sakura.css/css/sakura-vader.css"
fetch newsprint.css         "https://cdn.jsdelivr.net/gh/typora/typora-default-themes@master/themes/newsprint.css"
fetch jasonm23-markdown.css "$MS/jasonm23-markdown/assets/style.css"
fetch jasonm23-foghorn.css  "$MS/jasonm23-foghorn/assets/style.css"
fetch markedapp-byword.css  "$MS/markedapp-byword/assets/style.css"
fetch mixu-page.css         "$MS/mixu-page/assets/css/style.css"
fetch witex.css             "$MS/witex/assets/css/style.css"

# The Academic theme is the LaTeX look, and that is the LaTeX face. Fetched once and committed.
mkdir -p "$LIB/fonts"
for weight in regular bold italic bolditalic; do
  [ -f "$LIB/fonts/lmroman10-$weight.woff" ] && continue
  echo "Fetching lmroman10-$weight.woff…"
  curl -fsSL -o "$LIB/fonts/lmroman10-$weight.woff" "$MS/witex/assets/fonts/lmroman10-$weight.woff"
done

# Strip everything that cannot survive the move into an offline, content-scoped stylesheet:
# remote font imports, @font-face blocks (the one font we ship is emitted separately, at top
# level, where @font-face is legal), and any other url() asset we do not carry.
#
# The rest is dead weight these sheets have carried since ~2013 — IE-era properties, vendor-
# prefixed gradients superseded by the unprefixed one on the next line, two upstream typos
# (-webkit-hypens), a malformed length (.0.3125rem), and @page rules that are illegal nested
# inside a style rule. Every one of them is inert in a modern WebView but shows up as a warning
# in the editor, so they go. `linear-gradient(top, …)` is corrected to the standard `to bottom`
# form — as written it is invalid and the gradient silently never rendered at all.
strip() {
  awk '
    /@font-face[[:space:]]*{/ { inff=1 }
    inff { if (/}/) inff=0; next }
    /@import/ { next }
    /url\(/ { next }
    /-ms-filter/ { next }
    /-webkit-hypens|-moz-hypens/ { next }
    /@page/ { next }
    /background: *-(moz|webkit|o|ms)-|-webkit-gradient/ { next }
    # NB: awk gsub has no capture groups — the replacement must be a literal.
    { gsub(/\.0\./, "0."); gsub(/linear-gradient\(top,/, "linear-gradient(to bottom,"); print }
  ' "$1"
}

# Scope a page-level stylesheet to the content element.
#   :not(html) / :not(body)   dropped, not rewritten — a nested selector containing & loses its
#                             implicit scope, so :not(&) would let the rule escape the document.
#   *:not(div)…               this is upstream's "gross hack": it gives every block the narrow
#                             measure and lets div/blockquote bleed wide. Narrowed to the text
#                             blocks it means — as `*` it also lands on inline runs, where
#                             `margin: … auto` turns a callout icon into a flex spacer. Every block
#                             is listed except <table>: prose, code, diagrams and callouts share one
#                             measure, while a table keeps its natural width up to the full column —
#                             squeezed into a book measure its columns break mid-word. app.css caps
#                             it at the column so it can never scroll.
#   html/body/:root/#write    the page is one element here, so they all become that element.
scope() {
  sed -e 's|:not(html)||g' \
      -e 's|:not(body)||g' \
      -e 's|\*:not(div):not(img):not(li):not(blockquote):not(p)|h1, h2, h3, h4, h5, h6, ul, ol, dl, pre, hr, figure, .admonition, .katex-display|' \
      -e 's|#write|\&|g' \
      -e 's|:root|\&|g' \
      -e 's/\bhtml\b/\&/g' \
      -e 's/\bbody\b/\&/g'
}

theme() {  # <id> <cssfile>
  echo ".ct-$1 {"
  echo "  .markdown-body {"
  strip "$C/$2" | scope | sed 's/^/    /'
}
{
cat <<'HDR'
/* ============================================================================================
   Content themes — the look of the document itself: paper colour, typeface, size and spacing.
   They never touch the app chrome; the toolbar, sidebar and dialogs keep following the app theme.

   Each theme is one `.ct-<id>` block. app.js puts that class on <html> together with `ctl`/`ctd`,
   which tells the rest of the CSS (highlight.js palette, mermaid) whether the document reads light
   or dark. Nesting scopes every rule to `.markdown-body`, the content element, so a theme cannot
   escape the document. Each block ends with a bridge mapping the theme's palette onto the app's
   own --md-* tokens, so tables, quotes, callouts and KaTeX come along with it.

   Generated file — do not hand-edit. See scripts/build-content-themes.sh.
   ============================================================================================ */

/* The one bundled typeface: the Academic theme is the LaTeX look, and that is the LaTeX face. */
@font-face { font-family: 'Latin Modern Roman'; font-weight: normal; font-style: normal;
             src: url('fonts/lmroman10-regular.woff') format('woff'); font-display: swap; }
@font-face { font-family: 'Latin Modern Roman'; font-weight: bold; font-style: normal;
             src: url('fonts/lmroman10-bold.woff') format('woff'); font-display: swap; }
@font-face { font-family: 'Latin Modern Roman'; font-weight: normal; font-style: italic;
             src: url('fonts/lmroman10-italic.woff') format('woff'); font-display: swap; }
@font-face { font-family: 'Latin Modern Roman'; font-weight: bold; font-style: italic;
             src: url('fonts/lmroman10-bolditalic.woff') format('woff'); font-display: swap; }

HDR

echo "/* ---- Book — large serif with generous spacing ------------------------------------------- */"
theme book splendor.css
cat <<'B'

    --md-fg: #333333;
    --md-border: #dddddd;
    --md-link: #2980b9;
    --md-code-bg: #fafafa;
    --md-quote: #555555;
    --md-row: #fafafa;
    background: #ffffff;
    .admonition { background: #fafafa; }
  }
  .content-area.document-pane { background: #ffffff; }
}
B

echo "/* ---- Classic — compact serif, the plain printed page ------------------------------------ */"
theme classic jasonm23-markdown.css
cat <<'B'

    --md-fg: #111111;
    --md-border: #dddddd;
    --md-link: #0645ad;
    --md-code-bg: #f5f5f5;
    --md-quote: #666666;
    --md-row: #f7f7f7;
    background: #fefefe;
    .admonition { background: #f7f7f7; }
  }
  .content-area.document-pane { background: #fefefe; }
}
B

echo "/* ---- Elegant — airy serif with wide margins --------------------------------------------- */"
theme elegant jasonm23-foghorn.css
cat <<'B'

    --md-fg: #333333;
    --md-border: #cccccc;
    --md-link: #2484c1;
    --md-code-bg: #f7f7f7;
    --md-quote: #666666;
    --md-row: #f9f9f9;
    background: #fefefe;
    .admonition { background: #f9f9f9; }
  }
  .content-area.document-pane { background: #fefefe; }
}
B

echo "/* ---- Newspaper — dense serif on newsprint ----------------------------------------------- */"
theme newspaper newsprint.css
cat <<'B'

    --md-fg: #1f0909;
    --md-border: #c5c5c5;
    --md-link: #065588;
    --md-code-bg: #e8e7df;
    --md-quote: #656565;
    --md-row: #e8e7df;
    background: #f3f2ee;
    .admonition { background: #e8e7df; }
  }
  .content-area.document-pane { background: #f3f2ee; }
}
B

echo "/* ---- Academic — the LaTeX paper look ---------------------------------------------------- */"
theme academic witex.css
cat <<'B'

    --md-fg: #000000;
    --md-border: #cccccc;
    --md-link: #0645ad;
    --md-code-bg: #f5f5f5;
    --md-quote: #555555;
    --md-row: #f7f7f7;
    background: #ffffff;
    .admonition { background: #f7f7f7; }
  }
  .content-area.document-pane { background: #ffffff; }
}
B

echo "/* ---- Report — clean sans with ruled headings -------------------------------------------- */"
theme report modest.css
cat <<'B'

    --md-fg: #444444;
    --md-border: #e4e4e4;
    --md-link: #2980b9;
    --md-code-bg: #fafafa;
    --md-quote: #666666;
    --md-row: #fafafa;
    background: #ffffff;
    .admonition { background: #fafafa; }
  }
  .content-area.document-pane { background: #ffffff; }
}
B

echo "/* ---- Modern — roomy sans, the look of a web page ---------------------------------------- */"
theme modern mixu-page.css
cat <<'B'

    --md-fg: #222222;
    --md-border: #e5e5e5;
    --md-link: #0a84c1;
    --md-code-bg: #f8f8f8;
    --md-quote: #666666;
    --md-row: #f8f8f8;
    background: #ffffff;
    .admonition { background: #f8f8f8; }
  }
  .content-area.document-pane { background: #ffffff; }
}
B

echo "/* ---- Simple — compact modern sans ------------------------------------------------------- */"
theme simple sakura-earthly.css
cat <<'B'

    --md-fg: #222222;
    --md-border: #e0e0e0;
    --md-link: #006994;
    --md-code-bg: #f7f7f7;
    --md-quote: #4a4a4a;
    --md-row: #f7f7f7;
    background: #ffffff;
    .admonition { background: #f7f7f7; }
  }
  .content-area.document-pane { background: #ffffff; }
}
B

echo "/* ---- Writer — soft grey writing paper --------------------------------------------------- */"
theme writer markedapp-byword.css
cat <<'B'

    --md-fg: #3c3c3c;
    --md-border: #d5d5d5;
    --md-link: #308bd8;
    --md-code-bg: #e9e9e9;
    --md-quote: #777777;
    --md-row: #e9e9e9;
    background: #f2f2f2;
    /* html{62.5%} and body{1.7em} collapse onto one element here — restore the intended 17px. */
    font-size: 17px;
    .admonition { background: #e9e9e9; }
  }
  .content-area.document-pane { background: #f2f2f2; }
}
B

echo "/* ---- Typewriter — monospaced draft. The upstream sheet is a dark terminal look; the ------"
echo "       bridge below re-papers it, which is what makes it usable on a printed page. */"
theme typewriter retro.css
cat <<'B'

    --md-fg: #23201a;
    --md-border: #d8d2c2;
    --md-link: #7a4b1e;
    --md-code-bg: #f1ecdd;
    --md-quote: #5b5648;
    --md-row: #f6f2e8;
    background: #fdfcf7;
    color: #23201a;
    a, a:visited, a:hover { color: #7a4b1e; }
    h1, h2, h3, h4, h5, h6 { color: #23201a; }
    blockquote { border-left-color: #b8ae94; color: #5b5648; }
    code, pre { background: #f1ecdd; color: #23201a; }
    .admonition { background: #f6f2e8; }
  }
  .content-area.document-pane { background: #fdfcf7; }
}
B

echo "/* ---- Night — light text on a dark page -------------------------------------------------- */"
theme night sakura-vader.css
cat <<'B'

    --md-fg: #d9d8dc;
    --md-border: #40363a;
    --md-link: #eb99a1;
    --md-code-bg: #40363a;
    --md-quote: #b3b0b6;
    --md-row: #1d1518;
    background: #120c0e;
    .admonition { background: #1d1518; }
  }
  .content-area.document-pane { background: #120c0e; }
}
B

cat <<'FOOTER'

/* ---- Applies to every content theme ------------------------------------------------------------
   These sheets were written for a whole page, where the document element *is* the scroll container:
   `overflow-y: scroll` reserves a scrollbar so the page does not jump, and `min-height: 100%` fills
   the viewport. Here the document sits inside .content-area, which already scrolls, so both would
   only add a second scrollbar of their own. */
.ctl .markdown-body, .ctd .markdown-body {
  overflow: visible;
  min-height: 0;
}
FOOTER
} > "$OUT"

# highlight.js, keyed to the *document's* lightness rather than the app's. Both palettes already sit
# in the bundled highlight-theme.css — the light one at the top, the dark one inside its .theme-dark
# block — so they are lifted straight out of it and re-keyed to the ctl/ctd markers app.js sets.
HL="$LIB/highlight-theme.css"
{
  echo "/* Highlight.js palettes selected by the *content* theme's lightness (app.js sets ctl/ctd on"
  echo "   <html>), so code blocks match the document skin instead of the app chrome. Loaded after"
  echo "   highlight-theme.css, which keeps handling the \"follow app theme\" case."
  echo ""
  echo "   Generated file — do not hand-edit. See scripts/build-content-themes.sh. */"
  echo ".ctl {"; sed -n '1,10p' "$HL"; echo "}"
  echo ".ctd {"; sed -n '12,21p' "$HL"; echo "}"
} > "$LIB/highlight-content.css"
echo "wrote $LIB/highlight-content.css ($(wc -c < "$LIB/highlight-content.css") bytes)"
