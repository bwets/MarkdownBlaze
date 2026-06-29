# winget packaging

MarkdownBlaze is published to [winget](https://github.com/microsoft/winget-pkgs) using the
**Inno Setup installer** (`MarkdownBlaze-<v>-win-x64-setup.exe`) from each release. That installer
does a per-user install and **registers the `.md` / `.markdown` file association** — something the
portable zip and the (self-signed) MSIX can't provide through winget. No code signing is required.

> Tip: once the app is in the **Microsoft Store**, it's also installable with
> `winget install --source msstore MarkdownBlaze` automatically — no manifest needed. This community
> manifest is the Store-independent path.

## First submission (one-time, manual)

winget updates require the package to already exist in `winget-pkgs`. Seed it once:

```powershell
winget install Microsoft.WingetCreate
# Generates fresh manifests from the release installer and opens the PR for you:
wingetcreate new https://github.com/bwets/MarkdownBlaze/releases/download/v<ver>/MarkdownBlaze-<ver>-win-x64-setup.exe
```

When prompted, set:
- **PackageIdentifier:** `GregoryKieffer.MarkdownBlaze`
- **InstallerType:** `inno`, **Scope:** `user`

The `*.yaml` files here mirror that structure for reference (with `__VERSION__` / `__SHA256__`
placeholders).

## Subsequent versions (automatic)

The `release` workflow has a **`winget`** job (gated by the `publish_winget` input) that uses
[`winget-releaser`](https://github.com/vedantmgoyal9/winget-releaser) to open an update PR for each
new GitHub Release (matching `…-win-x64-setup.exe`). Requirements:
- A repo secret **`WINGET_TOKEN`** — a GitHub PAT (classic `public_repo`, or fine-grained with
  access to your `winget-pkgs` fork) used to push the PR.
- The package must already exist (see first submission above).

Then run the release with **publish_winget = true**.

## Install

```powershell
winget install GregoryKieffer.MarkdownBlaze
```
