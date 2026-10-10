#!/usr/bin/env bash
# Builds a .deb from a published linux-x64 output directory.
#
# Usage: build-deb.sh <version> <publish-dir>
#   <version>     e.g. 1.0.5
#   <publish-dir> the `dotnet publish` output (contains the `MarkdownBlaze` binary,
#                 plus MarkdownBlaze.desktop and MarkdownBlaze.svg copied in by the workflow)
set -euo pipefail

VERSION="${1:?version required}"
PUBLISH_DIR="${2:?publish dir required}"

# Debian package names are lowercase; the program, the command and the .desktop file keep their name.
NAME="markdownblaze"
PKG="${NAME}_${VERSION}_amd64"
ROOT="$PKG"
rm -rf "$ROOT"

# Layout: app under /opt, launcher symlink in /usr/bin, desktop + icon under /usr/share.
install -dm755 "$ROOT/opt/$NAME"
cp -a "$PUBLISH_DIR/." "$ROOT/opt/$NAME/"
chmod 755 "$ROOT/opt/$NAME/MarkdownBlaze"

install -dm755 "$ROOT/usr/bin"
ln -sf "/opt/$NAME/MarkdownBlaze" "$ROOT/usr/bin/MarkdownBlaze"

install -Dm644 "$PUBLISH_DIR/MarkdownBlaze.desktop" "$ROOT/usr/share/applications/MarkdownBlaze.desktop"
install -Dm644 "$PUBLISH_DIR/MarkdownBlaze.svg" "$ROOT/usr/share/icons/hicolor/scalable/apps/MarkdownBlaze.svg"

install -dm755 "$ROOT/DEBIAN"
cat > "$ROOT/DEBIAN/control" <<EOF
Package: ${NAME}
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: amd64
Depends: libgtk-3-0, libwebkit2gtk-4.1-0
Maintainer: Gregory Kieffer <gregory.kieffer@gmail.com>
Homepage: https://github.com/bwets/MarkdownBlaze
Description: A fast, offline, feature-rich Markdown viewer.
 MarkdownBlaze renders Markdown documents with syntax highlighting, math,
 diagrams and document themes, entirely offline.
EOF

# Refresh what the desktop caches about installed applications, so the menu entry, the icon and the
# "open markdown files with" association show up without logging out. Each tool is optional: a
# minimal system without a desktop does not have it, and that is not a failure.
cat > "$ROOT/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "configure" ]; then
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
    fi
fi
exit 0
EOF

cat > "$ROOT/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
    fi
fi
exit 0
EOF
chmod 755 "$ROOT/DEBIAN/postinst" "$ROOT/DEBIAN/postrm"

dpkg-deb --root-owner-group --build "$ROOT" "${PKG}.deb"
echo "Built ${PKG}.deb"
