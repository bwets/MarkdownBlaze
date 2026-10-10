# Security policy

MarkdownBlaze opens documents that nobody vetted: downloads, colleagues' wiki pages, files handed over by
a `markdown://` link. Keeping a hostile document from doing anything but be read is the point of the
design (see [docs/architecture.md](docs/architecture.md#the-trust-boundary)), so a report that a document
can run code, read a file it should not, reach the network, or make the viewer start a program is taken
seriously.

## Reporting a vulnerability

Please **do not open a public issue** for a vulnerability.

Report it privately through GitHub: on the repository's **Security** tab choose **Report a vulnerability**
(<https://github.com/bwets/MarkdownBlaze/security/advisories/new>). Include:

- the version (⋮ → What's new shows it, or the log's first line) and the operating system;
- the smallest Markdown file, or link, that shows the problem; the files in
  [`samples/wiki/security/`](samples/wiki/security/index.md) are a good model;
- what you expected, and what happened instead.

You will get an acknowledgement, and a fix or an explanation, as quickly as one person can manage; this is a
personal project, so there is no service-level promise. Please give a fix a reasonable time to reach users
before you publish the details; you will be credited in the release notes unless you prefer not to be.

## What counts

In scope: anything that lets a document, or a link to one, run script in the page, start a program, read or
disclose a file or a credential, contact a host the document's author chose other than by an ordinary
`http(s)` image or link that the user can see, or take the viewer's window somewhere that is not the app.

Not a vulnerability by itself: a document that references an image by web address makes the web view fetch
it, which tells that server the reader's IP address. That is documented behaviour ([PRIVACY.md](PRIVACY.md)).

## Supported versions

Only the latest release receives fixes.
