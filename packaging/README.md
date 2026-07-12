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
| `MarkdownBlaze-<v>-win-x64-setup.exe` | Windows installer (Inno Setup; registers the `.md` association) |
| `MarkdownBlaze-<v>-win-x64.msix` + `.cer` | Windows MSIX (self-signed) + its certificate for sideloading |
| `MarkdownBlaze-<v>-linux-x64.tar.gz` / `-arm64` | Self-contained Linux builds |
| `MarkdownBlaze_<v>_amd64.deb` | Debian/Ubuntu package |
| `MarkdownBlaze-<v>-*.pkg.tar.zst` | Arch Linux package (`pacman -U`) |
| `MarkdownBlaze-<v>-osx-arm64.dmg` / `-osx-x64` | macOS disk images (Apple Silicon / Intel) |
| `SHA256SUMS` | Checksums for every artifact |

### Runtime dependencies
- **Windows:** the Microsoft **WebView2 Runtime** (preinstalled on current Windows).
- **Linux:** **WebKitGTK** + GTK — `webkit2gtk-4.1` and `gtk3` (declared by the `.deb` and the Arch package).
- **macOS:** none — uses the system **WKWebView** (WebKit) built into macOS 11+.

## Arch Linux

Every release includes a prebuilt **`.pkg.tar.zst`** (built with `makepkg` from
`packaging/aur/PKGBUILD.template` in an Arch container) — no AUR needed:

```bash
sudo pacman -U MarkdownBlaze-<v>-1-x86_64.pkg.tar.zst
```

## macOS

The `macos` job publishes a self-contained build for each architecture, wraps it in a
`MarkdownBlaze.app` bundle (`packaging/macos/Info.plist` + an `.icns` generated from
`resources/md_512.png`), and packages it as a drag-to-`/Applications` **`.dmg`**.

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
+ generated tile logos, via the Windows SDK `makeappx`) and **self-signs** it. Because it's
self-signed, install it by sideloading:

```powershell
# (elevated, once) trust the published certificate, then install
Import-Certificate -FilePath MarkdownBlaze-<v>.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage MarkdownBlaze-<v>-win-x64.msix
```

The manifest declares `.md`/`.markdown` file associations, so the installed app registers as a
Markdown handler.

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
