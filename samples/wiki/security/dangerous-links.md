---
title: Dangerous Links
tags:
  - sample
  - security
  - test
---

# Dangerous Links

Clicking a link in a document the reader did not write must never start a program, open a network
share or hand code to another application. The viewer hands only `http`, `https`, `mailto`, `tel`
and `ftp` addresses to the operating system; everything else is refused. Each link below names a
program, a share or a scheme that is *not* on that list — the hosts end in `.invalid`, which can
never resolve.

**Expected, for every link in the first section:** it is rendered as an inert link. The pointer does
nothing, a click does nothing, no program starts, no window opens, and no error appears. The page
stays exactly where it is.

## Must do nothing

- [File URL to a program](file:///C:/Windows/System32/calc.exe) — must not start the calculator.
- [Drive path to a program](C:/x.exe) — must not start anything.
- [Share on another machine, UNC style](\\\\attacker.invalid\\share\\run.lnk) — must not touch the network.
- [File URL to a share](file://attacker.invalid/share/run.lnk) — must not touch the network.
- [Diagnostics handler](ms-msdt:/id%20PCWDiagnostic) — not on the allow-list, so nothing happens.
- [Search handler](search-ms:query=report&crumb=location:%5C%5Cattacker.invalid%5Cshare) — likewise.
- [JavaScript URL](javascript:alert('nope')) — must not run anything.
- [HTML in a data URL](data:text/html;base64,PHNjcmlwdD5hbGVydCgnbm9wZScpPC9zY3JpcHQ+) — must not open a page.
- [VBScript URL](vbscript:msgbox('nope')) — must not run anything.

## Must still work

These are on the allow-list, and they must keep working — the point is to refuse the dangerous ones,
not to refuse links:

- [A web address](https://example.com) — opens in your browser.
- [An email address](mailto:someone@example.com) — opens your mail client.
- [A local page](../index) — opens in the viewer, as ever.
