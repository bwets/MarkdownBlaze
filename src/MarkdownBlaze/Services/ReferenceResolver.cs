using System.Text;
using AngleSharp.Dom;
using MarkdownBlaze.Security;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Services;

/// <summary>
/// The stage between the sanitizer and the page that decides what the links and pictures of one
/// document point at. It works on the sanitized tree, so that what Markdown wrote
/// (<c>[a](b.md)</c>, <c>![](c.png)</c>) and what an author wrote as HTML (<c>&lt;a href="b.md"&gt;</c>,
/// <c>&lt;img src="c.png"&gt;</c>) go through the one piece of code, and so that the two things only this
/// class may put on the page — <c>data-nav</c> and <c>mdasset://</c> — are put there after the sanitizer
/// has run and cannot have been written by an author: the sanitizer removes every one of either it
/// meets, and this is the only code that comes after it.
/// <para>
/// Links. A relative address becomes an in-app link when it names a Markdown document (a folder is its
/// <c>index.md</c>, an address without an extension is a <c>.md</c>), carried in <c>data-nav</c> with
/// <c>href="#"</c>; web and mail addresses are left as written, to be opened only if they pass
/// <see cref="ExternalLinks"/>. A link to a local file by an absolute address — <c>C:/x.md</c>,
/// <c>file:///C:/x.md</c> — is either a Markdown document that exists, and then navigates in-app, or it is
/// made inert: it is never left to be opened as an "external" link, because the shell would run whatever it
/// names.
/// </para>
/// <para>
/// Pictures. The local ones are pointed at <see cref="AssetHandler"/> (<c>mdasset://</c>), within the
/// limits of <see cref="LocalPath"/>: image types only, never a device path, a UNC path only on the
/// server the document itself came from, a cap per image and a cap for the document. The bytes are not
/// read here — the WebView asks for them when it draws the page — but each image is opened, by the same
/// <see cref="LocalPath.TryOpenImage"/> the handler uses, so that one that would be refused is a
/// placeholder in the markup rather than a broken picture, and so that the document's budget can be added
/// up. The render's own <see cref="Token"/> is the one the handler is told to accept
/// (<see cref="AssetGrant"/>).
/// </para>
/// <para>
/// Every address looked at here is looked at on disk, which is slow when there are thousands: a document
/// is resolved up to <see cref="MarkdownService.MaxResolvedLinks"/> links and
/// <see cref="MarkdownService.MaxImages"/> pictures, and the ones past that are inert, or a placeholder.
/// </para>
/// </summary>
internal sealed partial class ReferenceResolver(string baseDir, CancellationToken cancellationToken)
{
    private static readonly ILogger Logger = Log.For(nameof(ReferenceResolver));

    /// <summary>What a picture past <see cref="MarkdownService.MaxImages"/> says in place of the picture.</summary>
    internal const string TooManyImages = "Too many images";

    private long _remainingImageBytes = LocalPath.MaxDocumentImageBytes;
    private int _links;
    private int _images;
    private int _shown;

    /// <summary>The folder relative addresses are resolved against; empty for text that has none.</summary>
    public string BaseDir { get; } = baseDir;

    /// <summary>Random per render: part of every picture address this render makes, and what the handler checks.</summary>
    // Decided: a fresh token for every render. A reload (file watcher, F5) therefore gives every image
    // a new address, so the WebView asks for each of them again and the browser cache cannot help; the
    // bytes come from the file each time, never through the page. That is deliberate: reusing the token
    // would let the cache work, but an image that was edited would stay as it was, and showing the
    // edited picture matters more than saving a file read.
    public string Token { get; } = Guid.NewGuid().ToString("N");

    /// <summary>The grant the handler needs to serve this render's images, or null when it has none.</summary>
    public AssetGrant? Grant => _shown > 0 ? new AssetGrant(Token, BaseDir) : null;

    /// <summary>Resolves every link and picture below <paramref name="root"/>, in document order.</summary>
    /// <param name="root">The sanitized body.</param>
    public void Resolve(IElement root)
    {
        foreach (var element in root.QuerySelectorAll("a[href], img[src]"))
        {
            cancellationToken.ThrowIfCancellationRequested(); // pictures are the slow part of a document that has many

            if (string.Equals(element.LocalName, "img", StringComparison.Ordinal))
                ResolveImage(element);
            else
                ResolveLink(element);
        }
    }

    // ---- links ---------------------------------------------------------------------------------------

    private void ResolveLink(IElement link)
    {
        var href = link.GetAttribute("href")?.Trim();
        if (string.IsNullOrEmpty(href)) return;
        if (href[0] == '#' || href.StartsWith("//", StringComparison.Ordinal)) return;

        var scheme = HtmlSanitizer.SchemeOf(href);
        if (scheme is not null && ExternalLinks.AllowedSchemes.Contains(scheme)) return; // http:, mailto:, ... — the page and ExternalLinks decide what opens
        if (scheme is not null && !HtmlSanitizer.IsFileScheme(scheme))
        {
            _ = link.RemoveAttribute("href"); // the sanitizer lets nothing like this through; if something did, it goes
            return;
        }

        if (++_links > MarkdownService.MaxResolvedLinks)
        {
            Block(link, "limit");
            return;
        }

        // Markdig percent-encodes the address it writes (a backslash is %5C, a space %20), so what the
        // author meant is what is left once it has been decoded.
        var decoded = Uri.UnescapeDataString(href);
        if (scheme is not null || LocalPath.IsUncOrDevice(decoded) || IsDriveRooted(decoded))
            ResolveLocalAddress(link, href, decoded, scheme);
        else
            ResolveRelative(link, href);
    }

    /// <summary>A link written as an absolute address: <c>file:///C:/x.md</c>, <c>C:/x.md</c>, <c>\\server\share\x.md</c>.</summary>
    private static void ResolveLocalAddress(IElement link, string href, string decoded, string? scheme)
    {
        string? local = null;
        if (string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase))
        {
            // A UNC target would make Windows open a network session just to look at it.
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.IsFile && !uri.IsUnc)
                local = uri.LocalPath;
        }
        else if (!LocalPath.IsUncOrDevice(decoded))
        {
            local = decoded;
        }

        if (local is not null
            && LocalPath.IsSafe(local)
            && local.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            && File.Exists(local)
            && TryFullPath(local) is { } full)
        {
            Navigate(link, full);
        }
        else
        {
            Block(link, "link");
        }
    }

    private void ResolveRelative(IElement link, string href)
    {
        var splitIndex = href.IndexOfAny(['#', '?']);
        var path = splitIndex >= 0 ? href[..splitIndex] : href;
        if (path.Length == 0 || path.StartsWith('/') || path.StartsWith('\\')) return;

        try
        {
            var decoded = Uri.UnescapeDataString(path);
            var fullPath = Path.GetFullPath(Path.Combine(BaseDir, decoded));

            var endsWithSeparator = decoded.EndsWith('/') || decoded.EndsWith('\\');
            if (endsWithSeparator || Directory.Exists(fullPath))
            {
                fullPath = Path.Combine(fullPath, "index.md");
            }
            else
            {
                var fileName = Path.GetFileName(decoded);
                if (fileName.Length > 0
                    && fileName is not ("." or "..")
                    && string.IsNullOrEmpty(Path.GetExtension(fileName)))
                {
                    fullPath += ".md";
                }
            }

            if (fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                Navigate(link, fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A path the file system cannot make sense of: the link stays as the author wrote it.
            LogLinkNotResolved(Logger, ex, href);
        }
    }

    private static string? TryFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            LogLinkNotResolved(Logger, ex, path);
            return null;
        }
    }

    private static void Navigate(IElement link, string target)
    {
        link.SetAttribute("href", "#");
        link.SetAttribute("data-nav", target);
    }

    private static void Block(IElement link, string reason)
    {
        link.SetAttribute("href", "#");
        link.SetAttribute("data-md-blocked", reason);
        _ = link.RemoveAttribute("data-nav");
    }

    // ---- pictures ------------------------------------------------------------------------------------

    private void ResolveImage(IElement image)
    {
        var src = image.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(src)) return;
        // data: images are the author's own and are vetted by the sanitizer (and are the app's placeholders).
        if (HtmlSanitizer.Normalised(src).StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;

        var decoded = LocalPathOf(src, out var isLocal);
        if (!isLocal)
        {
            // A remote picture is kept as it is, by decision (SEC-5) — whatever the web schemes are, and
            // nothing else: a drive letter the sanitizer let through is not one of them.
            if (HtmlSanitizer.SchemeOf(src) is { } scheme && !IsWebScheme(scheme))
                _ = image.RemoveAttribute("src");

            return;
        }

        if (++_images > MarkdownService.MaxImages)
        {
            Skip(image, "limit");
            return;
        }

        if (decoded is null)
        {
            Skip(image, "blocked"); // //server/share/x.png, \\?\C:\x.png and the like
            return;
        }

        switch (LocalPath.Check(decoded, BaseDir, LocalPath.MaxImageBytes, _remainingImageBytes, out var length))
        {
            case LocalPath.ImageOutcome.Allowed:
                _remainingImageBytes -= length;
                image.SetAttribute("src", AssetHandler.UrlFor(Token, decoded));
                _shown++;
                break;
            case LocalPath.ImageOutcome.Blocked:
                Skip(image, "blocked");
                break;
            case LocalPath.ImageOutcome.TooLarge:
                Skip(image, "too-large");
                break;
            case LocalPath.ImageOutcome.Missing:
                // Left as written, so the page shows its broken-image alt text. (For dropped text
                // this includes every relative path: it has no folder to be relative to.)
                break;
        }
    }

    /// <summary>
    /// The decoded filesystem path a picture address stands for. <paramref name="isLocal"/> is false for
    /// anything that is not a local file (http:, ...); the result is null when it is local but must not be
    /// read (a device path, a share on another machine).
    /// </summary>
    private string? LocalPathOf(string src, out bool isLocal)
    {
        isLocal = true;
        var trimmed = src.TrimStart();

        // Refuse before the URL parser gets to interpret it: on Windows "//server/share/x.png" is
        // parsed as an absolute file: URI, and merely looking for such a file starts an SMB session.
        // The one share that is not refused is the one the document itself was opened from (SEC-15):
        // that session exists already, and a document on a share has its pictures there too.
        if (LocalPath.IsUncOrDevice(trimmed)) return LocalPath.IsOnServerOf(trimmed, BaseDir) ? Unescaped(trimmed) : null;

        var scheme = HtmlSanitizer.SchemeOf(src);
        if (scheme is null) return Unescaped(src);

        if (string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(src, UriKind.Absolute, out var absolute) || !absolute.IsFile || absolute.IsUnc) return null;
            var local = absolute.LocalPath;
            return LocalPath.IsUncOrDevice(local) ? null : local;
        }

        if (HtmlSanitizer.IsFileScheme(scheme))
        {
            var drive = Unescaped(src); // C:/x.png, or C:%5Cx.png as Markdig writes C:\x.png
            return LocalPath.IsUncOrDevice(drive) ? null : drive;
        }

        isLocal = false;
        return null;
    }

    /// <summary>The address without its query and fragment, percent-decoded.</summary>
    private static string Unescaped(string address)
    {
        var splitIndex = address.IndexOfAny(['#', '?']);
        return Uri.UnescapeDataString(splitIndex >= 0 ? address[..splitIndex] : address);
    }

    private static void Skip(IElement image, string reason)
    {
        image.SetAttribute("data-md-skipped", reason);
        if (string.Equals(reason, "limit", StringComparison.Ordinal))
        {
            // The pictures past the limit have no drawing of their own: the document may have tens of thousands
            // of them, and a data: address for each (about 500 characters) would make a page of a few
            // megabytes out of a file of a few hundred kilobytes. Each has the words instead, as its alternative
            // text, which the page draws where the picture would have been (app.css gives it the look of the others).
            _ = image.RemoveAttribute("src");
            image.SetAttribute("alt", TooManyImages);
            return;
        }

        image.SetAttribute("src", string.Equals(reason, "too-large", StringComparison.Ordinal) ? Placeholders.TooLarge : Placeholders.Blocked);
    }

    // ---- reading an address -----------------------------------------------------------------------------

    private static bool IsWebScheme(string scheme) =>
        string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase);

    private static bool IsDriveRooted(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\';

    [LoggerMessage(Level = LogLevel.Debug, Message = "The link target {Url} is not a path this system can resolve; the link is left as written")]
    private static partial void LogLinkNotResolved(ILogger log, Exception ex, string url);

    /// <summary>What stands in the page for an image that is not shown on purpose.</summary>
    private static class Placeholders
    {
        public static readonly string TooLarge = Make("Image too large to display");
        public static readonly string Blocked = Make("Image not displayed");

        private static string Make(string message)
        {
            var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='280' height='40' viewBox='0 0 280 40'>"
                + "<rect x='.5' y='.5' width='279' height='39' rx='4' fill='none' stroke='#888' stroke-dasharray='4 3'/>"
                + "<text x='140' y='25' font-family='sans-serif' font-size='13' fill='#888' text-anchor='middle'>"
                + message
                + "</text></svg>";
            return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        }
    }
}
