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
