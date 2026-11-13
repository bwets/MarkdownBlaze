#!/bin/sh
# Installs or updates MarkdownBlaze for the current user, from a GitHub release.
#
#   curl -fsSL https://github.com/bwets/MarkdownBlaze/releases/latest/download/installer.sh | sh /dev/stdin
#
# Options, as key=value after /dev/stdin (or after the script's path when run from a file):
#   dest=<dir>                  where the app goes (default: ~/.local/markdownblaze.app)
#   launch=n                    do not start MarkdownBlaze when done
#   installer=version-<x.y.z>   install that release instead of the latest
#   installer=<path.tar.gz>     install a tarball already on disk (no download, no checksum)
#
# It needs nothing but curl, tar and sha256sum, and touches only the user's home: the app directory,
# a `markdownblaze` command in ~/.local/bin, and the desktop entry and icon (through the install-linux.sh
# that ships in the tarball). Running it again updates in place.
set -eu

REPO="bwets/MarkdownBlaze"
DEST="$HOME/.local/markdownblaze.app"
LAUNCH=y
SOURCE=latest

die() { echo "Error: $*" >&2; exit 1; }

for arg in "$@"; do
    case "$arg" in
        dest=*) DEST="${arg#dest=}" ;;
        launch=*) LAUNCH="${arg#launch=}" ;;
        installer=*) SOURCE="${arg#installer=}" ;;
        *) die "unknown option: $arg (expected dest=, launch= or installer=)" ;;
    esac
done
case "$DEST" in /*) ;; *) DEST="$(pwd)/$DEST" ;; esac
DEST="${DEST%/}"

[ "$(uname -s)" = Linux ] || die "this installer is for Linux; see https://github.com/$REPO/releases/latest for other systems."
case "$(uname -m)" in
    x86_64 | amd64) ARCH=x64 ;;
    aarch64 | arm64) ARCH=arm64 ;;
    *) die "no MarkdownBlaze build for $(uname -m)." ;;
esac
command -v tar >/dev/null || die "tar is required."

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT INT TERM

case "$SOURCE" in
    latest | version-*)
        command -v curl >/dev/null || die "curl is required."
        command -v sha256sum >/dev/null || die "sha256sum is required."
        if [ "$SOURCE" = latest ]; then
            # /releases/latest redirects to /releases/tag/v<version>.
            url="$(curl -fsSL -o /dev/null -w '%{url_effective}' "https://github.com/$REPO/releases/latest")" \
                || die "could not reach GitHub."
            VERSION="${url##*/v}"
        else
            VERSION="${SOURCE#version-}"
            VERSION="${VERSION#v}"
        fi
        case "$VERSION" in "" | */*) die "could not work out the latest version." ;; esac
        NAME="MarkdownBlaze-$VERSION-linux-$ARCH.tar.gz"
        BASE="https://github.com/$REPO/releases/download/v$VERSION"
        echo "Downloading MarkdownBlaze $VERSION ($ARCH)..."
        curl -fL --progress-bar -o "$TMP/$NAME" "$BASE/$NAME" || die "could not download $BASE/$NAME"
        curl -fsSL -o "$TMP/SHA256SUMS" "$BASE/SHA256SUMS" || die "could not download SHA256SUMS."
        expected="$(awk -v n="$NAME" '{ f = $2; sub(/^.*\//, "", f); if (f == n) print $1 }' "$TMP/SHA256SUMS")"
        [ -n "$expected" ] || die "$NAME is not listed in SHA256SUMS."
        actual="$(sha256sum "$TMP/$NAME" | cut -d' ' -f1)"
        [ "$expected" = "$actual" ] || die "checksum mismatch for $NAME: the download is corrupt or was altered."
        TARBALL="$TMP/$NAME"
        ;;
    *)
        [ -f "$SOURCE" ] || die "no such file: $SOURCE"
        TARBALL="$SOURCE"
        ;;
esac

# Unpack beside the destination first, so a failed unpack leaves the installed version untouched.
mkdir -p "$(dirname "$DEST")"
STAGE="$DEST.new"
rm -rf "$STAGE"
mkdir -p "$STAGE"
tar -xzf "$TARBALL" -C "$STAGE" --strip-components=1 || { rm -rf "$STAGE"; die "could not unpack the tarball."; }
[ -x "$STAGE/MarkdownBlaze" ] || { rm -rf "$STAGE"; die "the tarball has no MarkdownBlaze executable."; }

# Replace only a previous MarkdownBlaze install or an empty directory, never anything else.
if [ -e "$DEST" ]; then
    if [ -x "$DEST/MarkdownBlaze" ] || [ -z "$(ls -A "$DEST" 2>/dev/null)" ]; then
        rm -rf "$DEST"
    else
        rm -rf "$STAGE"
        die "$DEST exists and is not a MarkdownBlaze install; choose another dest=."
    fi
fi
mv "$STAGE" "$DEST"

mkdir -p "$HOME/.local/bin"
ln -sf "$DEST/MarkdownBlaze" "$HOME/.local/bin/markdownblaze"

echo "MarkdownBlaze is installed in $DEST"
echo "Command: markdownblaze (in ~/.local/bin)"
if [ -f "$DEST/install-linux.sh" ]; then
    bash "$DEST/install-linux.sh" "$DEST/MarkdownBlaze" >/dev/null
    echo "It is in the app launcher and opens .md files and markdown:// links."
else
    echo "Warning: this release has no install-linux.sh; the desktop entry and icon were not installed." >&2
fi

# The window is GTK + WebKitGTK, which come from the system, not from the tarball.
LDCONFIG="$(command -v ldconfig || echo /sbin/ldconfig)"
if [ -x "$LDCONFIG" ] && ! "$LDCONFIG" -p 2>/dev/null | grep -q 'libwebkit2gtk-4\.1\.so'; then
    echo "Warning: WebKitGTK 4.1 was not found. Install your distribution's webkit2gtk-4.1 (or libwebkit2gtk-4.1-0) package." >&2
fi

if [ "$LAUNCH" != n ] && { [ -n "${DISPLAY:-}" ] || [ -n "${WAYLAND_DISPLAY:-}" ]; }; then
    nohup "$DEST/MarkdownBlaze" >/dev/null 2>&1 &
fi
