# winget packaging

MarkdownBlaze is published to [winget](https://github.com/microsoft/winget-pkgs) as a **portable**
package installed from the release **zip** (`MarkdownBlaze-<v>-win-x64.zip`). The zip needs no code
signing, so this works without a trusted certificate (unlike the MSIX).

> Tip: once the app is in the **Microsoft Store**, it's also installable with
> `winget install --source msstore MarkdownBlaze` automatically — no manifest needed. This community
> manifest is the Store-independent path.

## First submission (one-time, manual)

winget updates require the package to already exist in `winget-pkgs`. Seed it once:

```powershell
winget install Microsoft.WingetCreate
# Generates fresh manifests from the release zip and opens the PR for you:
wingetcreate new https://github.com/bwets/MarkdownBlaze/releases/download/v<ver>/MarkdownBlaze-<ver>-win-x64.zip
```

When prompted, set:
- **PackageIdentifier:** `bwets.MarkdownBlaze`
- **InstallerType:** `zip`, **NestedInstallerType:** `portable`
- **Nested file:** `MarkdownBlaze.exe` (command alias `MarkdownBlaze`)

The `*.yaml` files here mirror that structure for reference (with `__VERSION__` / `__SHA256__`
placeholders).

## Subsequent versions (automatic)

The `release` workflow has a **`winget`** job (gated by the `publish_winget` input) that uses
[`winget-releaser`](https://github.com/vedantmgoyal9/winget-releaser) to open an update PR for each
new GitHub Release. Requirements:
- A repo secret **`WINGET_TOKEN`** — a GitHub PAT (classic `public_repo`, or fine-grained with
  access to your `winget-pkgs` fork) used to push the PR.
- The package must already exist (see first submission above).

Then run the release with **publish_winget = true**.

## Install

```powershell
winget install bwets.MarkdownBlaze
```
