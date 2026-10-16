#!/usr/bin/env bash
# Stages a snapcraft build directory from a published linux output directory.
#
# Usage: build-snap.sh <version> <publish-dir> [<out-dir>]
#   <version>     e.g. 1.0.5 (the same shape the release workflow accepts, a prerelease suffix allowed)
#   <publish-dir> the `dotnet publish` output for ONE architecture (contains the `MarkdownBlaze` binary)
#   <out-dir>     where to create the staged directory (default: the current directory)
#
# Contract: this script only stages; it does not run snapcraft, so CI can hand the result to
# snapcore/action-build and a developer can run `snapcraft pack` in it. It creates
# <out-dir>/markdownblaze-snap/ holding
#   snap/snapcraft.yaml            the recipe with the version filled in
#   snap/gui/markdownblaze.desktop derived from packaging/linux/MarkdownBlaze.desktop
#   snap/gui/markdownblaze.svg     branding/icon.svg
#   publish/                       a copy of <publish-dir>
# and prints the absolute path of that directory as the LAST line of stdout (everything else goes to
# stderr), so a caller can use `dir="$(build-snap.sh ...)"`. An existing staged directory is replaced.
# Which architecture is built is decided by the machine running snapcraft and by what <publish-dir>
# contains, so run it once per architecture on a runner of that architecture.
set -euo pipefail

VERSION="${1:?version required}"
PUBLISH_DIR="${2:?publish dir required}"
OUT_DIR="${3:-.}"

# The same check the release workflow makes, so a bad version fails here rather than in the store.
if ! printf '%s' "$VERSION" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$'; then
  echo "Invalid version '$VERSION' (expected X.Y.Z or X.Y.Z-prerelease)" >&2
  exit 1
fi
if [ ! -f "$PUBLISH_DIR/MarkdownBlaze" ]; then
  echo "No MarkdownBlaze binary in '$PUBLISH_DIR'; is it a linux publish output?" >&2
  exit 1
fi

# Everything is located relative to this script, so it works from any current directory.
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
SHARED_DESKTOP="$REPO/packaging/linux/MarkdownBlaze.desktop"
ICON="$REPO/branding/icon.svg"

mkdir -p "$OUT_DIR"
STAGE="$(cd "$OUT_DIR" && pwd)/markdownblaze-snap"
rm -rf "$STAGE"
install -dm755 "$STAGE/snap/gui" "$STAGE/publish"

# The version is validated above, so it contains nothing sed would treat specially.
sed "s/@VERSION@/$VERSION/" "$HERE/snapcraft.yaml" > "$STAGE/snap/snapcraft.yaml"
if grep -q '@VERSION@' "$STAGE/snap/snapcraft.yaml"; then
  echo "Version placeholder was not replaced in snapcraft.yaml" >&2
  exit 1
fi

# The desktop entry stays defined once, in the shared file. Here only what differs in a snap is rewritten:
# snapd rewrites an Exec that starts with the app name to the snap's launcher, and the icon is read from
# the snap's own meta/gui directory. The shared file's comment lines are about install-linux.sh and
# would mislead in this copy, so comments are dropped.
sed -e '/^#/d' \
    -e 's|^Exec=.*|Exec=markdownblaze %u|' \
    -e 's|^Icon=.*|Icon=${SNAP}/meta/gui/markdownblaze.svg|' \
    "$SHARED_DESKTOP" > "$STAGE/snap/gui/markdownblaze.desktop"
for key in 'Exec=markdownblaze %u' 'Icon=${SNAP}/meta/gui/markdownblaze.svg' \
           'MimeType=.*x-scheme-handler/markdown' 'StartupWMClass='; do
  if ! grep -q -- "^$key" "$STAGE/snap/gui/markdownblaze.desktop"; then
    echo "Derived desktop entry lacks '$key'; has the shared file changed shape?" >&2
    exit 1
  fi
done
install -m644 "$ICON" "$STAGE/snap/gui/markdownblaze.svg"

cp -a "$PUBLISH_DIR/." "$STAGE/publish/"
chmod 755 "$STAGE/publish/MarkdownBlaze"

echo "Staged snap build directory for markdownblaze $VERSION; run 'snapcraft pack' in it." >&2
echo "$STAGE"
