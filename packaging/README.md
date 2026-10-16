# Packaging & releases

MarkdownBlaze is a **Photino + Blazor (Fluent UI)** desktop app on .NET 10. Releases are produced by the
**`release` GitHub Actions workflow** (`.github/workflows/release.yml`), which is **manual only**
(`workflow_dispatch`) — it never runs on push.

## Running a release

Actions → **release** → *Run workflow*:

- **version** — leave empty to use `1.0.<run_number>` (the patch increases with each build), or type
  an explicit version.
- **publish_winget** — also open a winget update PR (needs `WINGET_TOKEN`; see `packaging/winget`).
- **publish_store** — also submit to the Microsoft Store (see below).
- **publish_snap** — also upload the snap to the Snap Store and release it to `stable` (needs the
  `SNAPCRAFT_STORE_CREDENTIALS` secret; see *Snap Store / Ubuntu App Center* below).

It creates a GitHub **Release `v<version>`** with:

| Artifact | Contents |
|---|---|
| `MarkdownBlaze-<v>-win-x64.zip` | Portable Windows build (self-contained; uses the OS WebView2 runtime) |
| `MarkdownBlaze-<v>-win-x64-setup.exe` | Windows installer (Inno Setup; registers the document types, the folder menu and `markdown://`, see below) |
| `MarkdownBlaze-<v>-win-x64.msix` + `.cer` | Windows MSIX (signed, see below) + its certificate for sideloading |
| `MarkdownBlaze-<v>-linux-x64.tar.gz` / `-arm64` | Self-contained Linux builds |
| `markdownblaze_<v>_amd64.deb` | Debian/Ubuntu package |
| `markdownblaze-<v>-1-x86_64.pkg.tar.zst` | Arch Linux package (`pacman -U`) |
| `MarkdownBlaze-<v>-osx-arm64.dmg` / `-osx-x64` | macOS disk images (Apple Silicon / Intel) |
| `SHA256SUMS` | Checksums for every artifact |

The snap is not in this table: the same run builds it, but it goes to the Snap Store, not to the release.

The release notes start with the thumbprint of the certificate the MSIX was signed with, and every
file is covered by a build-provenance attestation (`gh attestation verify <file> --repo <owner>/<repo>`):
the installer and the zip are not code-signed, so the attestation is how a download can be tied to
the workflow run that built it. Below that, the notes list which of the expected files are part of this
release: the macOS job may fail without stopping the release, and the list (and a warning on the run)
says so if it did.

### Runtime dependencies
- **Windows:** the Microsoft **WebView2 Runtime** (preinstalled on current Windows).
- **Linux:** **WebKitGTK** + GTK — `webkit2gtk-4.1` and `gtk3` (declared by the `.deb` and the Arch package).
- **macOS:** none — uses the system **WKWebView** (WebKit) built into macOS 11+.

## Windows registration

How MarkdownBlaze ties into Windows (Explorer "Open with", Default apps, `markdown://` links) depends on
how it was installed. The three builds do it differently on purpose, and the unpackaged two write the
**same keys**, so they never fight:

| Build | Who registers | When | Who removes it |
|---|---|---|---|
| Installer (`-setup.exe`) | The installer, from `[Registry]` in `windows/MarkdownBlaze.iss` | At install | The uninstaller (every key has `uninsdeletekey` / `uninsdeletevalue`) |
| Portable zip | The app (`ShellIntegration`), **only on request** | Settings → *Windows integration* → *Add to Explorer and register markdown:// links*, or `MarkdownBlaze.exe --register` | The same row's *Remove*, or `MarkdownBlaze.exe --unregister` |
| MSIX / Store | The package manifest (`windows/AppxManifest.xml`) | At install | Windows, on uninstall |

The app writes nothing to the registry at startup, and does nothing at all when it detects an MSIX
package (`GetCurrentPackageFullName`): inside a package its writes to `HKCU\Software\Classes` go to the
package's private hive, where Explorer does not look.

What is registered (all under `HKEY_CURRENT_USER\Software\Classes`, so no elevation):

- `MarkdownBlaze.Document` — the ProgId — and `.md`, `.markdown`, `.mdx` → `OpenWithProgids`. This makes
  MarkdownBlaze a choice in *Open with* and in *Settings → Default apps*, where the user decides whether
  it is the default. (It used to be a verb under `SystemFileAssociations\<ext>\shell`, which is a menu
  entry only. Both the installer and the app delete those leftovers.)
- `Directory\shell\MarkdownBlaze` and `Directory\Background\shell\MarkdownBlaze` — *Open with MarkdownBlaze* on
  folders and on the empty space inside one (`%V`). Folders have no default app, hence a verb.
- `markdown` — the `URL Protocol` key, with `DefaultIcon` and `shell\open\command`.
- `HKCU\Software\MarkdownBlaze`, value `InstallSource` = `installer` — written **only by the installer**. It
  is how the app knows the installer did the registering, so Settings says *Managed by the installer* and
  offers no button.

If `ShellIntegration` and the `.iss` ever disagree about this list, a test fails
(`PackagingTests.The_installer_registers_every_key_the_app_can_register`).

`MarkdownBlaze.exe --register` and `--unregister` do the work and exit without opening a window. The exit
code is `0` when done, `1` when the registry refused something (the reason is in the log), and `2` when
they do not apply (not Windows, or the Store build). The program is a GUI application, so a script that
wants the code has to wait for it: `Start-Process -Wait` in PowerShell, `start /wait` in `cmd`.
`--unregister` removes the registration whoever made it, the installer's marker included; run
`--register` afterwards and the registration is the app's own, which Settings can remove again.

## Linux

`packaging/linux/MarkdownBlaze.desktop` is the one definition of the desktop entry: the `.deb`, the Arch
package and `packaging/linux/install-linux.sh` all install it, and the script only rewrites its `Exec`
path (to the absolute path of the binary it is given). The scalable icon every package installs as
`MarkdownBlaze.svg` is `branding/icon.svg`; the workflow copies it into each Linux build.

The `.deb` is named `markdownblaze` (Debian names are lowercase) and declares `Maintainer` and `Homepage`;
its `postinst` and `postrm` refresh the desktop and icon caches (`update-desktop-database`,
`gtk-update-icon-cache`) where those tools exist. The program, the `/usr/bin/MarkdownBlaze` command and the
`.desktop` file keep their name. The `.desktop` entry sets `StartupWMClass=MarkdownBlaze` so the taskbar groups
the window with the launcher (unverified on a running desktop, see the comment in the file).

## Arch Linux

Every release includes a prebuilt **`.pkg.tar.zst`** (built with `makepkg` from
`packaging/arch/PKGBUILD.template` in an Arch container) — no AUR needed:

```bash
sudo pacman -U markdownblaze-<v>-1-x86_64.pkg.tar.zst
```

The package is named `markdownblaze` (Arch names are lowercase); the program, the command and the
`.desktop` file are still `MarkdownBlaze`. It provides and conflicts with the old mixed-case package name,
so `pacman` offers to replace an earlier install. The `release` job copies the tarball to the file name
the PKGBUILD's `source` expects (`markdownblaze-<v>.tar.gz`).

## macOS

The `macos` job publishes a self-contained build for each architecture, wraps it in a
`MarkdownBlaze.app` bundle (`packaging/macos/Info.plist` + an `.icns` generated from
`branding/icon-512.png`), and packages it as a drag-to-`/Applications` **`.dmg`**. The bundle declares only the
Markdown document type (`net.daringfireball.markdown`), not `public.plain-text`: that would make it an "Open
With" candidate for every `.txt`, `.log` and `.csv`. Opening a `.txt` is a convenience of the app's own file
dialog.

The app is **ad-hoc signed** (so it launches on Apple Silicon) but **not notarized**, so on first
launch macOS Gatekeeper needs a manual allow:

```bash
# Either right-click the app → Open (once), or clear the quarantine attribute:
xattr -dr com.apple.quarantine /Applications/MarkdownBlaze.app
```

To distribute without that step, sign with a Developer ID certificate and notarize (needs an Apple
Developer account); the `codesign --sign -` step in the workflow is where that would plug in.

## Snap Store / Ubuntu App Center

`sudo snap install markdownblaze` installs MarkdownBlaze from the Snap Store, and Ubuntu's App Center
lists it. The snap is **strictly confined**, so the store checks it automatically and no human review
is needed. It is not a release artifact: the `.snap` is not attached to the GitHub release (a snap
installed from outside the store needs `--dangerous` and never updates); the store is where users get it.

### What is built

The snap repackages the self-contained `linux-x64` / `linux-arm64` publish output; nothing is compiled
inside it.

- `packaging/snap/snapcraft.yaml` is the recipe: `core24`, strict confinement, one snap per architecture
  (`amd64`, `arm64`). Its `version` is a placeholder that the staging script fills in. The `summary` and
  `description` the store shows are written there too (the same text as in `packaging/snap/store-listing.md`).
- `packaging/snap/build-snap.sh <version> <publish-dir> [<out-dir>]` stages a build directory
  (`markdownblaze-snap/`: the recipe with the version filled in, `snap/gui/` and `publish/`) and prints
  its path as the last line. It does not run snapcraft. The desktop entry in `snap/gui/` is derived
  from `packaging/linux/MarkdownBlaze.desktop` (the one definition, as for the other Linux packages):
  `Exec` becomes `markdownblaze %u` and `Icon` points into the snap; `MimeType` (including the
  `markdown` link) and `StartupWMClass` are kept. The icon is `branding/icon.svg`.
- **GTK 3 and WebKitGTK are not in the snap.** The `gnome` extension takes them from the shared
  `gnome-46-2404` content snap, which keeps the snap small and gives WebKit security updates
  without a MarkdownBlaze release. The extension also brings the `desktop`, `wayland` and `x11` plugs.
- **ICU** (which .NET globalization needs and `core24` lacks) is listed as a stage package (`libicu74`),
  but the extension's graphics cleanup removes from the snap everything the `mesa-2404` content snap
  also ships, and that includes ICU 74. At run time .NET is expected to find it through the `gpu-2404`
  plug. The stage package is kept to say what is needed, and as a safeguard.

### The `snap` workflow

`.github/workflows/snap.yml` is a workflow of its own, so that the snap can be tried without a release (a
release run always creates a GitHub release). It has two entry points:

| Started by | What it does |
|---|---|
| Actions → **snap** → *Run workflow* | Builds and smoke-tests both architectures. **Never publishes.** *version* defaults to `0.0.<run_number>-dev`. The snaps are kept for 7 days as the artifacts `snap-amd64` and `snap-arm64`. |
| The `release` workflow's `snap` job | The same, with the release's version. Publishes only if **publish_snap** is ticked. |

Each architecture is built on a runner of its own kind (`ubuntu-24.04`, `ubuntu-24.04-arm`) from its own
`dotnet publish`, then smoke-tested: the job installs the snap with `--dangerous`, installs and connects
the content snaps, starts the app under `xvfb-run` on `samples/wiki/index.md`, and requires that the app
log (`markdownblaze.log`) shows it started *and* rendered `index.md`, and that the process is still alive
20 seconds later. The app renders only after the web view has loaded its page, so this shows that WebKit
started under confinement and that the `home` plug lets the app read the document. It does **not** prove
that anything is visible: the display is virtual and nobody looks at it, so a blank window or a broken
layout would pass. That is checked by hand on a real desktop before the first publish.

Publishing is the separate `publish` job in `snap.yml`. It `needs` the whole build matrix and runs only
when **publish_snap** is ticked, so if either architecture fails its smoke test, **nothing is published**.
For each architecture it downloads the snap the build job tested (nothing is rebuilt), uploads it and
releases it to the **`stable`** channel straight away; it reaches App Center users once the store's
automatic review has passed it. The credentials are given to that one step. The `release` job does not
wait for any of this, so a problem with the store never holds back the GitHub release.

### What each plug is for

| Plug | Why |
|---|---|
| `home` | Documents in the user's home folder. **Hidden folders in it (such as `~/.notes`) are not reachable** under this plug; documents in them cannot be opened. |
| `removable-media` | Documents on USB drives and other mounted volumes (`/media`, `/mnt`). **Not connected automatically:** until the store grants that (to be requested on forum.snapcraft.io after launch; the text is in `packaging/snap/store-listing.md`), users run `snap connect markdownblaze:removable-media`. |
| `network` | Remote images in documents. The app makes no other network requests. |
| `cups` | Printing: the print dialog lists the system's printers. |
| `opengl` | Graphics acceleration for the web view. |

`desktop`, `wayland`, `x11` and the content plugs come from the `gnome` extension.

### Where the logs and settings are

A snap runs with its own `HOME`, `~/snap/markdownblaze/<revision>/`, and `~/snap/markdownblaze/current`
is the link to the active revision. The app keeps its files in a `MarkdownBlaze` folder under the
platform's local app data (`AppPaths.DataFolder`), which on Linux is `$XDG_DATA_HOME` or else
`$HOME/.local/share`; inside the snap that should be
`~/snap/markdownblaze/current/.local/share/MarkdownBlaze/`, holding `preferences.json`, the history and
`logs/markdownblaze.log`. (The exact folder has not been checked on an installed snap; the smoke test
finds the log with `find ~/snap/markdownblaze -name markdownblaze.log` so that it does not depend on it.)
Settings shows where the log is. Snapd copies the folder to the new revision on a refresh and removes it
with the snap.

### Credentials

The `publish` job needs the repository secret **`SNAPCRAFT_STORE_CREDENTIALS`** (Settings → Secrets and
variables → Actions); the name `markdownblaze` is already registered in the store. Create the credentials
with:

```bash
snapcraft export-login --snaps=markdownblaze --channels=stable --acls=package_access,package_push,package_update,package_release creds.txt
```

They expire after **one year** (`--expires` changes that); the current ones expire on **2027-10-16**.
Renew them before then: run the command again and replace the secret, or a release with **publish_snap**
ticked fails at the upload. Delete `creds.txt` afterwards. `snapcraft login` does not work under WSL
(there is no keyring); `export-login` does not need one. In PowerShell, set the secret like this (single
quotes, because a WSL path contains `$`; `<` redirection does not exist in PowerShell):

```powershell
Get-Content '<path to creds.txt>' -Raw | gh secret set SNAPCRAFT_STORE_CREDENTIALS --repo bwets/MarkdownBlaze
```

The listing on snapcraft.io (icon, screenshots, category, links) is filled in by hand once, after the first
upload; the text to paste is in `packaging/snap/store-listing.md`.

### Building locally

`snapcraft` builds `core24` snaps in LXD (destructive mode only works if the host is Ubuntu 24.04), so
LXD has to be installed and initialized. Then, on a machine of the architecture you want:

```bash
dotnet publish src/MarkdownBlaze/MarkdownBlaze.csproj -c Release -r linux-x64 --self-contained true -o publish-linux-x64
dir="$(bash packaging/snap/build-snap.sh 1.0.0 publish-linux-x64 snapbuild | tail -n 1)"
cd "$dir" && snapcraft pack
sudo snap install --dangerous ./markdownblaze_1.0.0_amd64.snap
```

A snap installed this way may need its content snaps (`gnome-46-2404`, `mesa-2404`) installed and
connected by hand; `snap connections markdownblaze` shows what is missing.

## Windows MSIX

The `release` workflow packs the published app into an **MSIX** (`packaging/windows/AppxManifest.xml`
+ generated tile logos, via the Windows SDK `makeappx`) and signs it. Because the certificate is not
from a public CA, install the package by sideloading:

```powershell
# (elevated, once) trust the published certificate, then install
Import-Certificate -FilePath MarkdownBlaze-<v>.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage MarkdownBlaze-<v>-win-x64.msix
```

The manifest declares `.md`/`.markdown`/`.mdx` file associations and the `markdown` protocol, so the
installed app registers as a Markdown handler and a `markdown://` link handler (the Store build has no
folder menu entry; see the comment in the manifest).

### Signing certificate

The package is signed with a certificate whose subject is the manifest's `Publisher`
(`CN=87D75823-...`). Which certificate that is depends on two optional repository secrets
(Settings → Secrets and variables → Actions):

| Secret | Contents |
|---|---|
| `MSIX_SIGNING_PFX` | The signing certificate with its private key as a PFX file, **base64-encoded** (`[Convert]::ToBase64String([IO.File]::ReadAllBytes("signing.pfx"))`) |
| `MSIX_SIGNING_PASSWORD` | The password the PFX was exported with |

- **Both set:** every release is signed with that one certificate, so a sideloaded install keeps
  trusting later releases and the `.cer` in each release is the same file. The workflow stops with an
  error if the certificate's subject differs from the manifest's `Publisher`.
- **Not set:** a new self-signed certificate is created for each run (what the workflow did before the
  secrets existed): every release then has a different thumbprint and has to be trusted again.

Either way the thumbprint is printed in the run summary and at the top of the release notes. To make a
long-lived self-signed certificate once:

```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=87D75823-3F39-43DE-B07A-CCE71CEE43E2" `
    -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(10)
Export-PfxCertificate -Cert $cert -FilePath signing.pfx -Password (Read-Host -AsSecureString "PFX password")
```

The Store does not care which certificate is used: it re-signs the package it receives.

## Continuous integration

`.github/workflows/ci.yml` builds, format-checks and tests every push to `main` and every pull request
on Windows, Linux and macOS. `.github/dependabot.yml` proposes weekly updates to the NuGet packages
(`Directory.Packages.props`) and to the GitHub Actions the workflows use. Actions are pinned by commit
SHA with the version in a comment; Dependabot updates both.

## Microsoft Store (automated)

With **publish_store = true**, the `store` job submits the MSIX to the Store via the
[Microsoft Store Developer CLI](https://learn.microsoft.com/windows/apps/publish/msstore-dev-cli/overview)
(`msstore`). The package's `<Identity>` already matches Partner Center, and the Store re-signs it.

Requirements:
- The app is already **published/live** (the first submission was created + certified manually) and
  is a **free** product (the automated flow doesn't support paid apps yet).
- A Microsoft Entra (Azure AD) app registration **added in Partner Center → Account settings → User
  management → Microsoft Entra applications with the `Manager` role**.
- Repo secrets: `AZURE_AD_TENANT_ID`, `AZURE_AD_APPLICATION_CLIENT_ID`, `AZURE_AD_APPLICATION_SECRET`,
  `SELLER_ID`, and `STORE_PRODUCT_ID` (the app's Store product ID).

Submissions still go through Store certification before going live.
