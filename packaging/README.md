# Packaging & releases

MarkdownBlaze is a **Photino + Blazor (Fluent UI)** desktop app on .NET 10. Releases are produced by the
**`release` GitHub Actions workflow** (`.github/workflows/release.yml`), which is **manual only**
(`workflow_dispatch`) — it never runs on push.

## Running a release

Actions → **release** → *Run workflow*:

- **version** — leave empty to use `1.0.<run_number>` (the patch increases with each build), or type
  an explicit version.
- **publish_aur** — also push the AUR package (needs the AUR secrets below).

It creates a GitHub **Release `v<version>`** with:

| Artifact | Contents |
|---|---|
| `MarkdownBlaze-<v>-win-x64.zip` | Portable Windows build (self-contained; uses the OS WebView2 runtime) |
| `MarkdownBlaze-<v>-win-x64.msix` + `.cer` | Windows MSIX (self-signed) + its certificate for sideloading |
| `MarkdownBlaze-<v>-linux-x64.tar.gz` / `-arm64` | Self-contained Linux builds |
| `MarkdownBlaze_<v>_amd64.deb` | Debian/Ubuntu package |
| `SHA256SUMS` | Checksums for every artifact |

### Runtime dependencies
- **Windows:** the Microsoft **WebView2 Runtime** (preinstalled on current Windows).
- **Linux:** **WebKitGTK** + GTK — `webkit2gtk-4.1` and `gtk3` (declared by the `.deb` and the AUR package).

## AUR

`packaging/aur/PKGBUILD.template` is rendered by the workflow (version + tarball sha256) and pushed
to the AUR. One-time setup:
1. Create an [AUR account](https://aur.archlinux.org) and add your SSH public key.
2. Add repo secrets `AUR_USERNAME`, `AUR_EMAIL`, `AUR_SSH_PRIVATE_KEY`.
3. Run the release with **publish_aur = true**.

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

**Microsoft Store:** replace the self-signed cert with your Partner Center identity (set the
`<Identity>` Name/Publisher in `AppxManifest.xml` to the reserved values) and submit the package;
the Store re-signs it.
