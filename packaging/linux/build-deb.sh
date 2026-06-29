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

PKG="MarkdownBlaze_${VERSION}_amd64"
ROOT="$PKG"
rm -rf "$ROOT"

# Layout: app under /opt, launcher symlink in /usr/bin, desktop + icon under /usr/share.
install -dm755 "$ROOT/opt/MarkdownBlaze"
cp -a "$PUBLISH_DIR/." "$ROOT/opt/MarkdownBlaze/"
chmod 755 "$ROOT/opt/MarkdownBlaze/MarkdownBlaze"

install -dm755 "$ROOT/usr/bin"
ln -sf /opt/MarkdownBlaze/MarkdownBlaze "$ROOT/usr/bin/MarkdownBlaze"

install -Dm644 "$PUBLISH_DIR/MarkdownBlaze.desktop" "$ROOT/usr/share/applications/MarkdownBlaze.desktop"
install -Dm644 "$PUBLISH_DIR/MarkdownBlaze.svg" "$ROOT/usr/share/icons/hicolor/scalable/apps/MarkdownBlaze.svg"

install -dm755 "$ROOT/DEBIAN"
cat > "$ROOT/DEBIAN/control" <<EOF
Package: MarkdownBlaze
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: amd64
Depends: libgtk-3-0, libwebkit2gtk-4.1-0
Maintainer: bwets
Description: A fast, offline, feature-rich Markdown viewer.
EOF

dpkg-deb --root-owner-group --build "$ROOT" "${PKG}.deb"
echo "Built ${PKG}.deb"
