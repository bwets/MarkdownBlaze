#!/usr/bin/env bash
# Registers MarkdownBlaze as a handler for markdown files on Linux (per-user).
#
# Usage:
#   ./install-linux.sh [path-to-MarkdownBlaze-executable]
#
# If no path is given, it looks for an "MarkdownBlaze" binary next to this script
# (i.e. a published self-contained build placed alongside it).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"
BIN="${1:-$SCRIPT_DIR/MarkdownBlaze}"

if [ ! -x "$BIN" ]; then
    echo "Error: MarkdownBlaze executable not found or not executable at: $BIN" >&2
    echo "Pass the path to the published MarkdownBlaze binary as the first argument." >&2
    exit 1
fi

APPS_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$APPS_DIR"
DESKTOP_FILE="$APPS_DIR/MarkdownBlaze.desktop"

cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Name=MarkdownBlaze
Comment=Markdown viewer
Exec="$BIN" %f
Icon=MarkdownBlaze
Terminal=false
Categories=Office;Viewer;TextTools;
MimeType=text/markdown;text/x-markdown;
EOF

chmod 644 "$DESKTOP_FILE"

# Refresh the desktop database and set MarkdownBlaze as the default for markdown files.
update-desktop-database "$APPS_DIR" 2>/dev/null || true
xdg-mime default MarkdownBlaze.desktop text/markdown 2>/dev/null || true
xdg-mime default MarkdownBlaze.desktop text/x-markdown 2>/dev/null || true

echo "Installed: $DESKTOP_FILE"
echo "MarkdownBlaze is now the default application for markdown (.md) files."
echo "If file associations don't take effect immediately, log out and back in."
