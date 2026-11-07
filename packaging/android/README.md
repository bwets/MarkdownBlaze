# Android

The Android app is the same page as the desktop one, shown by a .NET MAUI Blazor Hybrid host
(`src/MarkdownBlaze.Android`, application id `net.bwets.markdownblaze`, Android 8.0 and up, built against API 36).
It is built by the `android` job of `.github/workflows/release.yml` into a signed **APK**, which goes on the GitHub
release, and a signed **AAB** (the format Google Play takes), which goes to Play when the run asks for it.
`.github/workflows/ci.yml` builds the same project on every push, signed with the debug key.

## The signing key

Android installs an update only over a copy signed with the **same key**. The release key is a keystore that you
create once, keep safe, and give to the workflow as GitHub secrets. Nothing of it is in the repository (`.gitignore`
lists `*.jks` and `*.keystore`), and the build is given the passwords as `env:NAME`, which makes it read them from the
environment, so they are on no command line and in no log.

> **Back the keystore up, in two places, outside the repository (a password manager's file store, an encrypted
> drive).** If it is lost, nobody who installed the APK from GitHub can ever be updated: the next build has to be
> installed as a different app, after uninstalling the first. Play is more forgiving. With **Play App Signing**
> (the default for new apps) the key your users' phones check is Google's, and the key you hold is only an *upload*
> key, which Google can reset if you lose it. The key that signs the APK on GitHub has no such safety net.

### Create it

Use the JDK's `keytool` (Temurin 21 has it). One key for the APK and for the AAB upload, RSA 4096, valid for 10 000
days (about 27 years; Play wants a key valid beyond 2033):

```bash
keytool -genkeypair -v -keystore markdownblaze-release.jks -alias markdownblaze \
  -keyalg RSA -keysize 4096 -validity 10000
```

It asks for the keystore password, the key password (press Enter to use the same one) and your name. Keep the alias,
both passwords and the file together in the backup. To see what it holds: `keytool -list -v -keystore markdownblaze-release.jks`.

### Give it to GitHub

Four repository secrets (Settings, Secrets and variables, Actions), set by you, never by a script in this repository:

| Secret | Value |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | the keystore file, base64-encoded: `base64 -w0 markdownblaze-release.jks` (Linux), `[Convert]::ToBase64String([IO.File]::ReadAllBytes("markdownblaze-release.jks"))` (PowerShell) |
| `ANDROID_KEYSTORE_PASSWORD` | the keystore password |
| `ANDROID_KEY_ALIAS` | `markdownblaze` (the alias you gave) |
| `ANDROID_KEY_PASSWORD` | the key password |

If any of the four is missing the `android` job stops with `::error::The secret ... is not set` and builds nothing
unsigned. Like the macOS job it may fail without stopping the rest of the release, so desktop releases are not held
up while this is not set up; the release notes then say the Android package is not in the release.

The release says which key signed the APK: `apksigner verify --print-certs MarkdownBlaze-<v>-android.apk` shows a
certificate whose SHA-256 digest is the same in every release. Write the digest down when you create the key
(`keytool -list -v` prints it).

## Version numbers

Android compares a **version code**, a whole number that has to go up with every release. The project derives it from
the version: `major * 1 000 000 + minor * 1 000 + patch`, so `1.0.24` is `1000024`; a suffix such as `-rc.1` is
ignored (so a prerelease and its release share a code: give the release a higher patch). The minor and patch numbers
have to stay under 1 000 and the major at most 2 000 (Play accepts up to 2 100 000 000). The workflow checks this
before it builds, the project refuses a build outside the range, and the finished APK is checked against the code the
workflow worked out. The default release version is `1.0.<run number>`, so the range lasts 999 builds of a minor
version; then give the next release a new minor version.

## Building it yourself

The toolchain, all of it needed:

- **.NET 10 SDK** and the workload: `dotnet workload install maui-android --version 10.0.403` (the workload set whose
  `maui-android` is 10.0.110, the version the MAUI packages in `Directory.Packages.props` are held at; CI and the
  release install the same one).
- **A Temurin 21 JDK** (Eclipse Adoptium). Not the Microsoft build of OpenJDK: its version string
  (`21.0.x.0.y`) makes the Android tasks fail with `XA0033`. With both installed, point the build at Temurin:
  `-p:JavaSdkDirectory="C:\Program Files\Eclipse Adoptium\jdk-21.0.13.9-hotspot"` (or set `JAVA_HOME`).
- The **Android SDK** (platform 36 and its build tools). `dotnet build src/MarkdownBlaze.Android/MarkdownBlaze.Android.csproj -t:InstallAndroidDependencies -f net10.0-android -p:AndroidSdkDirectory=<sdk> -p:AcceptAndroidSDKLicenses=True` installs them.

Build it (outside CI also pass `-p:LocalSources=false` unless the sibling `bwets.Markdig.Extensions` checkout is there):

```bash
dotnet build src/MarkdownBlaze.Android.slnx -c Release -p:LocalSources=false
```

A signed release, the way the workflow does it (the passwords in environment variables, named with `env:`):

```bash
export ANDROID_KEYSTORE_PASSWORD=... ANDROID_KEY_PASSWORD=...
dotnet publish src/MarkdownBlaze.Android/MarkdownBlaze.Android.csproj -c Release -p:LocalSources=false -p:Version=1.0.7 \
  -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=markdownblaze-release.jks -p:AndroidSigningKeyAlias=markdownblaze \
  -p:AndroidSigningStorePass=env:ANDROID_KEYSTORE_PASSWORD -p:AndroidSigningKeyPass=env:ANDROID_KEY_PASSWORD \
  -o publish/android
```

That writes `net.bwets.markdownblaze-Signed.apk` and `net.bwets.markdownblaze-Signed.aab`. Without the signing
properties the packages are signed with the debug key. Check and install one:

```bash
apksigner verify --print-certs publish/android/net.bwets.markdownblaze-Signed.apk   # build-tools of the Android SDK
adb install publish/android/net.bwets.markdownblaze-Signed.apk
```

A phone that has an APK signed with another key (a debug build, say) refuses the update: uninstall it first.

`packaging/android/check-manifest.sh <merged AndroidManifest.xml>` is what CI runs on
`src/MarkdownBlaze.Android/obj/Release/net10.0-android/android/AndroidManifest.xml`: the only permissions are
`INTERNET` and the app's own `DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` (androidx adds that one; only this app can
hold it), no `markdown://` filter, no cleartext traffic, no backup.

## Google Play

1. **Developer account**: <https://play.google.com/console>, a one-time fee of 25 USD, with identity verification.
2. **Create the app** (name MarkdownBlaze, free, app id `net.bwets.markdownblaze`, which cannot change afterwards) and
   let Play manage the app signing. Fill in the store listing (text, the 512 px icon from `branding/`, screenshots from a phone).
3. **Privacy policy**: the link to `PRIVACY.md` on GitHub.
4. **Data safety** form:
   - Does the app collect or share user data? **No.** Nothing is sent to the developer or to anyone else.
   - The `INTERNET` permission is used by the web view only, to show a picture that a document refers to by an
     `https://` address, as a browser would; the app makes no request of its own. Plain `http://` pictures are not
     fetched at all.
   - Data is not encrypted in transit *by the app* because the app does not transmit any; the pictures come over HTTPS.
   - The user cannot request deletion of data held by the developer, because there is none; what the app keeps on the
     phone is removed by uninstalling it, or folder by folder in Settings.
5. **Content rating** (a questionnaire; no user content is shared, so the lowest rating), **target audience** (not
   children), **ads** (none), and the other declarations Play asks for.
6. **Testing before production.** A *new personal* developer account has to run a **closed test with at least 12
   testers who stay opted in for 14 days** before it may apply for production. Make a closed-test track, add 12 or more
   testers' Google accounts (or a Google Group), have them accept the invitation and install from Play, and keep it running
   for 14 days. Then apply for production access in the console. (An organisation account does not need this.)
7. **First release by hand.** Play does not accept the first release of an app through its API. Upload the first AAB
   (the one from the `android` job's artifact) in the console to the internal track and roll it out.
8. **After that, from CI.** Create a service account (Google Cloud, with the Play Developer API enabled), give it
   release permissions for this app under *Users and permissions* in the Play Console, create a JSON key for it and store
   the whole JSON as the secret `PLAY_SERVICE_ACCOUNT_JSON`. Then run the release workflow with **publish_play**: the
   `play` job uploads the AAB to the **internal** track. Moving a release up (closed, open, production) is done in the
   console. Each upload needs a higher version code than the one before, which the version rule above gives.

When Play App Signing is on, the AAB is signed with the *upload* key (the keystore above) and Play re-signs what it
delivers with its own key. The APK on GitHub is signed with the keystore directly, so an install from GitHub and one from
Play have different signatures and cannot update each other; uninstall one to move to the other.
