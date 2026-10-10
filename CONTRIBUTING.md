# Contributing

Thanks for looking. This is a small, carefully commented code base; the way to fit in is to read the code
around your change and write in the same voice. Start with [docs/architecture.md](docs/architecture.md):
the three invariants and the trust boundary are the rules a change most easily breaks.

## Build and run

You need the [.NET 10 SDK](https://dotnet.microsoft.com/) (`global.json` pins the feature band) and, on
Linux, `webkit2gtk-4.1` and `gtk3`; Windows needs the WebView2 Runtime, which current Windows has.

```bash
dotnet build src/MarkdownBlaze.slnx            # the app and its tests; nothing outside this repository
dotnet run --project src/MarkdownBlaze -- samples/wiki/index.md
```

`src/MarkdownBlaze.slnx` is what a fresh clone builds. The launch profiles in
`src/MarkdownBlaze/Properties/launchSettings.json` open the sample wiki or the welcome manual. The
"Sample Wiki" profile names its file relative to the project folder (`../../samples/wiki/index.md`), which is
where Visual Studio and Rider start it. `dotnet run --launch-profile` ignores a `workingDirectory` setting
(tried: the profile then opens the welcome manual), so from the repository root use the command above, or
`cd src/MarkdownBlaze` first.

The app writes a log to `%LOCALAPPDATA%\MarkdownBlaze\logs`; `--log-level debug` makes it chatty, and
`--devtools` turns the web view's developer tools on in a Release build (a Debug build always has them, on F12).

### `LocalSources`: working on bwets.Markdig.Extensions at the same time

The Markdown extensions come from the `bwets.Markdig.Extensions` NuGet package. To edit that library and
this app in one loop, check it out next to this repository (`../bwets.Markdig.Extensions`), set
`LocalSources=true` (in the git-ignored `src/MarkdownBlaze/MarkdownBlaze.csproj.user`, or with
`-p:LocalSources=true`) and open `src/MarkdownBlaze.Dev.slnx`, which lists both. A Release build with
`LocalSources=true` is an error (`Directory.Build.targets`), so a local project reference can never ship:
build Release the way CI does, with `-p:LocalSources=false`.

```bash
dotnet build src/MarkdownBlaze/MarkdownBlaze.csproj -c Release -warnaserror -p:LocalSources=false
```

## Rules the build enforces

- **Zero warnings.** `Directory.Build.props` turns warnings into errors and enables the analyzers
  (`AnalysisLevel=latest-all`, Roslynator, Meziantou), and `.editorconfig` raises every analyzer message to
  a warning. A change that adds a diagnostic does not build.
- **Format.** CI runs the formatter in verify mode; run it before you push:

  ```bash
  dotnet format src/MarkdownBlaze.slnx --verify-no-changes --severity info    # check
  dotnet format whitespace src/MarkdownBlaze.slnx                             # fix layout
  ```

  `dotnet format` applying every analyzer fix at once can crash on this code base; run its sub-commands
  (`whitespace`, `style`, `analyzers --diagnostics <id>`) one at a time.
- **Line endings are LF** on every platform (`.gitattributes`, `.editorconfig`), whatever `core.autocrlf`
  says on your machine.
- **Before you push**, all of these are clean (CI runs them): `dotnet build src/MarkdownBlaze.slnx -warnaserror`,
  the Release build above, `dotnet test tests/MarkdownBlaze.Tests`, the format check, and `npm test`.
- **Tests.** `dotnet test tests/MarkdownBlaze.Tests`. Every security-relevant or behavioural change comes
  with a test. Tests that only make sense on one platform use `[SkippableFact]` and say why in the skip
  message. The sample documents in `samples/wiki/security/` are rendered by a test, so they stay accurate.
- **JavaScript tests.** The page's JavaScript is plain ES modules in `src/MarkdownBlaze/wwwroot/js/` (no build
  step, no bundler). Its pure parts are tested with vitest over jsdom: `npm ci && npm test`, tests in
  `tests/js/`. Nothing the tests need ships with the app; `package.json` is only the harness.
- **Comments stay.** The explanatory comments are a feature; moving code means moving them with it.

### Suppressing a diagnostic

Fix the code. A suppression is the last resort, and every one is reviewed:

1. It lives in exactly one place: `.editorconfig`, as `dotnet_diagnostic.<ID>.severity = none` (or a lower
   severity), scoped to the folder or file it is for. Never in a `.csproj` `NoWarn`, and never as
   `#pragma warning disable` except for one line where the rule is simply wrong for that call, with a
   `// Justification:` on the same line.
2. The comment directly above it gives the rule ID and title, one sentence of justification, how many
   places it silences, and ends with `Reviewed by Fable: pending`. The reviewer changes `pending` to
   `approved <date>` or sends the code back for fixing. An entry still marked `pending` is not finished.
3. These are never accepted: the `CA2xxx`, `CA3xxx` and `CA5xxx` security and reliability rules,
   `MA0009` (regex timeouts), `MA0012` / `MA0015` (exception use), `MA0042` / `MA0045` (blocking calls in
   async code), and anything whose fix is simply writing the code correctly.

## Where things go

The layout is described in the README's *Project layout*. In short: components in `Components/`, services
registered in DI in `Services/`, plain data in `Models/`, the things that decide what a hostile document
may do in `Security/`, starting programs (`IShellLauncher`) and Windows-only code in `Platform/`, calls
between the page and .NET in `Interop/`. One type per file, named after the file. Anything that has to start
a process takes an `IShellLauncher`; tests hand it `FakeShellLauncher`, so a test never opens a browser.
Work that is started and not awaited goes through `Background.Run` so that its failure is logged.

Vendored JavaScript lives in `src/MarkdownBlaze/wwwroot/vendor/` and is never edited by hand; see
`wwwroot/vendor/VERSIONS.md` and `scripts/update-vendor.sh`. A new third-party dependency gets an entry,
with its licence text, in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## The changelog

`src/MarkdownBlaze/Content/WhatsNew.md` is the one changelog: it is what the app shows under ⋮ → What's new,
and there is no `CHANGELOG.md`. Changes a reader would notice go under the `## {{version}}` section at the top,
in the same voice as the entries there (a bold lead, then a sentence or two); the file's header comment
says what to do when a release is cut. Tests, CI and refactoring are not for readers and stay out of it.

## Reporting problems

Bugs and ideas: <https://github.com/bwets/MarkdownBlaze/issues>. Vulnerabilities: [SECURITY.md](SECURITY.md),
privately.
