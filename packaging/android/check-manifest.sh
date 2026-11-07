#!/usr/bin/env bash
# Checks the manifest an Android build merged (the app's own plus the ones its libraries bring) against
# what PRIVACY.md promises. The unit tests read the app's own manifest; this reads the merged one, which
# is where a library's permission would turn up, and which needs a build to exist.
#
# Usage: check-manifest.sh <merged AndroidManifest.xml>
#   e.g. src/MarkdownBlaze.Android/obj/Release/net10.0-android/android/AndroidManifest.xml
set -euo pipefail

MANIFEST="${1:?path of the merged AndroidManifest.xml required}"
APP_ID="net.bwets.markdownblaze"

fail=0
problem() { echo "::error::$1"; fail=1; }

# The permissions the manifest asks for, one per line, without the attribute around the name.
permissions=$(grep -o '<uses-permission[^>]*android:name="[^"]*"' "$MANIFEST" | sed 's/.*android:name="//; s/"$//' | sort -u)
echo "uses-permission:"
echo "$permissions" | sed 's/^/  /'

# INTERNET is for the pictures a document takes from the web. The other is the app's own signature
# permission, which androidx.core adds for receivers registered at run time: only this app can hold it.
expected=$(printf '%s\n' "android.permission.INTERNET" "$APP_ID.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION" | sort -u)
if [ "$permissions" != "$expected" ]; then
  problem "The merged manifest asks for permissions beyond INTERNET: $(echo "$permissions" | grep -vxF -e android.permission.INTERNET -e "$APP_ID.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION" | tr '\n' ' ')"
fi

if grep -q 'android:scheme="markdown"' "$MANIFEST"; then
  problem "An intent filter claims the markdown:// scheme; its addresses carry the desktop's paths and are not for Android."
fi
if grep -q 'android:usesCleartextTraffic="true"' "$MANIFEST"; then
  problem "usesCleartextTraffic is on: plain-http pictures are not shown, by design."
fi
if grep -q 'android:allowBackup="true"' "$MANIFEST" || ! grep -q 'android:allowBackup="false"' "$MANIFEST"; then
  problem "allowBackup must be false: a cloud backup would copy the history, the log and the preferences off the device."
fi
if grep -q 'android:debuggable="true"' "$MANIFEST"; then
  problem "The manifest is debuggable."
fi

exit "$fail"
