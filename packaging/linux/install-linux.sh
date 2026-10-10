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

# The committed MarkdownBlaze.desktop is the one definition of the entry (the .deb and the Arch package
# install it too). It sits next to this script in the repository, and next to the binary in a release
# tarball. Only its Exec line is rewritten here, to the absolute path of the binary; everything else
# (including %u and the markdown:// scheme handler) is installed exactly as committed.
TEMPLATE="$SCRIPT_DIR/MarkdownBlaze.desktop"
if [ ! -f "$TEMPLATE" ]; then TEMPLATE="$(dirname "$BIN")/MarkdownBlaze.desktop"; fi
if [ ! -f "$TEMPLATE" ]; then
    echo "Error: MarkdownBlaze.desktop not found next to this script or the executable." >&2
    exit 1
fi

# Escape what sed treats specially in the replacement: backslash, the | delimiter, and &.
BIN_ESCAPED="$(printf '%s' "$BIN" | sed -e 's/[\\|&]/\\&/g')"
sed "s|^Exec=.*|Exec=\"$BIN_ESCAPED\" %u|" "$TEMPLATE" > "$DESKTOP_FILE"

chmod 644 "$DESKTOP_FILE"

# Refresh the desktop database and set MarkdownBlaze as the default for markdown files.
update-desktop-database "$APPS_DIR" 2>/dev/null || true
xdg-mime default MarkdownBlaze.desktop text/markdown 2>/dev/null || true
xdg-mime default MarkdownBlaze.desktop text/x-markdown 2>/dev/null || true

echo "Installed: $DESKTOP_FILE"
echo "MarkdownBlaze is now the default application for markdown (.md) files."
echo "If file associations don't take effect immediately, log out and back in."
