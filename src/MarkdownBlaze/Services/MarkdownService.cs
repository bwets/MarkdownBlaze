using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using bwets.Markdig.Extensions;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownBlaze.Models;
using MarkdownBlaze.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarkdownBlaze.Services;

/// <summary>
/// Renders Markdown to body HTML using the bwets.Markdig.Extensions pipeline: YAML front-matter,
/// advanced extensions, emoji shortcodes, admonitions (Docusaurus/MkDocs/GitHub-Obsidian callouts),
/// mermaid, wiki links, hidden Obsidian comments, and Obsidian tags. Syntax highlighting (highlight.js)
/// and math (KaTeX) are applied client-side over the emitted classes. Relative links are rewritten for
/// in-app navigation, and local images are given an <c>mdasset://</c> address that the WebView host
/// serves from disk (see <see cref="AssetHandler"/>) rather than being carried in the page.
/// </summary>
internal sealed partial class MarkdownService
{
    private static readonly ILogger Logger = Log.For(nameof(MarkdownService));

    /// <summary>
    /// The largest file that is rendered. Parsing, sanitising and handing a multi-megabyte page to the
    /// WebView takes long enough to look like a hang; a log file opened as Markdown by mistake is the
    /// usual culprit, and a message beats a frozen window.
    /// </summary>
    public const long MaxFileBytes = 32L * 1024 * 1024;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley(enableSmileys: false) // :shortcode: only — no ":)"→emoji, matching GitHub/Obsidian
        .UseAdmonitions()
        .UseMermaid()
        .UseWikiLinks()
        .UseObsidianComments()
        .UseObsidianTags()
        .Build();

    private readonly ILogger _log;

    public MarkdownService(ILogger<MarkdownService>? log = null)
    {
        _log = log ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>The rendered document, or null when it could not be rendered (see <see cref="RenderFile"/> for why).</summary>
    public RenderResult? Render(string markdownFilePath) => RenderFile(markdownFilePath).Result;

    /// <summary>
    /// Reads and renders a file, saying what became of it. This is the work that must not run on the UI
    /// thread — <see cref="RenderFileAsync"/> does it on the thread pool — so it observes
    /// <paramref name="cancellationToken"/> between its stages: a reader who has moved on to another
    /// document does not wait for this one to finish.
    /// </summary>
    public FileRender RenderFile(string markdownFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(markdownFilePath) || !File.Exists(markdownFilePath))
            return new FileRender(RenderStatus.Missing);

        var fullPath = Path.GetFullPath(markdownFilePath);
        string markdown;
        try
        {
            // FileShare.ReadWrite|Delete: an editor that has the file open (or is half-way through
            // replacing it) must not make it unreadable for us.
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            if (stream.Length > MaxFileBytes)
                return new FileRender(RenderStatus.TooLarge, Length: stream.Length);

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            markdown = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Gone between the check above and the open: an editor saving by replacing the file.
            LogVanished(_log, ex, fullPath);
            return new FileRender(RenderStatus.Missing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogUnreadable(_log, ex, fullPath); // locked / permission denied / not really a file → caller shows a fallback
            return new FileRender(RenderStatus.Unreadable);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var baseDir = Path.GetDirectoryName(fullPath) ?? string.Empty;
        return new FileRender(
            RenderStatus.Rendered,
            RenderCore(markdown, baseDir, fullPath, Path.GetFileName(fullPath), cancellationToken));
    }

    /// <summary><see cref="RenderFile"/> on the thread pool, so that the window keeps answering while a big document renders.</summary>
    public Task<FileRender> RenderFileAsync(string markdownFilePath, CancellationToken cancellationToken) =>
        Task.Run(() => RenderFile(markdownFilePath, cancellationToken), cancellationToken);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rendered {File} in {Milliseconds} ms; the HTML handed to the page is {Characters} characters")]
    private static partial void LogRendered(ILogger log, string file, long milliseconds, int characters);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Path} vanished while it was being opened")]
    private static partial void LogVanished(ILogger log, Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {Path}")]
    private static partial void LogUnreadable(ILogger log, Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not list the documents in {Folder}")]
    private static partial void LogUnreadableFolder(ILogger log, Exception ex, string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the What's new page at {Path}")]
    private static partial void LogWhatsNewMissing(ILogger log, Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The link target {Url} is not a path this system can resolve; the link is left as written")]
    private static partial void LogLinkNotResolved(ILogger log, Exception ex, string url);

    /// <summary>
    /// Renders raw Markdown that has no backing file path (e.g. a file dropped into the WebView, where
    /// the real path is not available). Relative links/images cannot be resolved without a base folder.
    /// </summary>
    public RenderResult RenderText(string fileName, string markdown)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "Untitled.md" : fileName.Trim();
        return RenderCore(markdown ?? string.Empty, string.Empty, name, name, CancellationToken.None);
    }

    private static RenderResult RenderCore(string markdown, string baseDir, string path, string file, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var document = Markdown.Parse(markdown, Pipeline);
        cancellationToken.ThrowIfCancellationRequested();
        var images = new ImageRefs(baseDir, cancellationToken);
        var targets = new LinkTargets();
        RewriteLinksAndImages(document, images, targets);
        cancellationToken.ThrowIfCancellationRequested();

        using var writer = new StringWriter();
        var htmlRenderer = new HtmlRenderer(writer);
        Pipeline.Setup(htmlRenderer);
        _ = htmlRenderer.Render(document);
        writer.Flush();
        cancellationToken.ThrowIfCancellationRequested();

        // The document is untrusted, and so is the raw HTML Markdig lets through in it: nothing reaches
        // the page without passing the sanitizer. It is done here, in the one place every render goes
        // through, so text dropped onto the window and the What's new page are covered as well.
        // The addresses of the images (mdasset://) and the in-app link targets (data-nav) are both
        // things only this class may put on the page: the sanitizer removes every one an author wrote,
        // so the ones that are left are the ones this class made, swapped in after it has run.
        var body = targets.Restore(images.Restore(HtmlSanitizer.Sanitize(writer.ToString())));

        var headings = ExtractHeadings(document);
        var title = DeriveTitle(headings, path);
        var elapsed = (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogRendered(Logger, file, elapsed, body.Length);

        return new RenderResult(path, file, title, body, headings, LanguageOf(document), images.Grant);
    }

    /// <summary>
    /// What the document says its language is in its front matter, or failing that what its prose
    /// reads as. Code is left out of the reckoning: it is English whatever the document is.
    /// </summary>
    private static string? LanguageOf(MarkdownDocument document)
    {
        var frontMatter = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        var declared = DocumentLanguage.FromFrontMatter(frontMatter?.Lines.ToString());
        if (declared is not null) return declared;

        var prose = new StringBuilder();
        foreach (var literal in document.Descendants<LiteralInline>())
            _ = prose.Append(literal.Content.ToString()).Append(' ');

        return DocumentLanguage.Detect(prose.ToString());
    }

    // ---- startup file ---------------------------------------------------------------------------

    /// <summary>File extensions MarkdownBlaze opens (startup argument + drag &amp; drop).</summary>
    public static readonly string[] SupportedExtensions = [".md", ".markdown", ".mdx", ".txt"];
    private static readonly FrozenSet<string> SupportedSet = SupportedExtensions.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the file's extension (the last one, in any capitalisation) is one of <see cref="SupportedExtensions"/>.</summary>
    /// <param name="path">A file name or path.</param>
    public static bool IsSupported(string path) => SupportedSet.Contains(Path.GetExtension(path));

    /// <summary>
    /// The document to open for the command line: the first argument that names a readable supported
    /// file (or a folder to open the entry document of), else the bundled manual.
    /// </summary>
    /// <param name="args">The command-line arguments the process was started with (without the executable).</param>
    public string? GetStartupFilePath(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg)) continue;
            // A markdown:// URL is how another application (or a link) asks us to open a document;
            // everything else on the command line is an ordinary path.
            var path = UriScheme.TryGetFilePath(arg) ?? arg.Trim().Trim('"');

            // A folder is a legitimate thing to be pointed at — open the document it leads with.
            if (Directory.Exists(path))
            {
                var entry = FindEntryFile(path);
                if (entry is not null) return entry;
                continue;
            }

            if (!IsSupported(path)) continue;
            var resolved = ResolveExisting(path);
            if (resolved is not null) return resolved;
        }

        // No readable supported argument → fall back to the bundled manual, shown like any other file.
        return GetUserManualPath();
    }

    /// <summary>Files a folder leads with, in the order they are looked for.</summary>
    private static readonly string[] EntryFileNames = ["index", "main", "readme"];

    /// <summary>
    /// The document to open for a folder: its index/main/readme if it has one, otherwise the first
    /// supported file in alphabetical order. Null when the folder holds no readable document.
    /// </summary>
    public static string? FindEntryFile(string folder)
    {
        List<string> files;
        try
        {
            files = [.. Directory.EnumerateFiles(folder).Where(IsSupported)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // An unreadable folder is simply "nothing to open"; the log says why, at Debug.
            LogUnreadableFolder(Logger, ex, folder);
            return null;
        }

        foreach (var name in EntryFileNames)
        {
            var match = files.FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return Path.GetFullPath(match);
        }

        var first = files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        return first is null ? null : Path.GetFullPath(first);
    }

    /// <summary>
    /// The version this build was published as — "1.0.24" — or null for a build made without one,
    /// which is every build that did not come out of the release workflow.
    /// </summary>
    public static string? AppVersion { get; } = VersionOf(typeof(MarkdownService).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion);

    /// <summary>
    /// The version a build announces itself as, from its informational version: the text before the
    /// source-control suffix ("1.0.24+3f9c2a1" is "1.0.24"), or null for none. A build made without a
    /// version of its own says <c>0.0.0-dev</c> (the csproj's default), and anything ending in
    /// <c>-dev</c> is that: a local build, which has no number to show.
    /// </summary>
    /// <param name="informationalVersion">The assembly's informational version; may be null.</param>
    internal static string? VersionOf(string? informationalVersion)
    {
        var version = informationalVersion?.Split('+')[0];
        return string.IsNullOrEmpty(version) || version.EndsWith("-dev", StringComparison.OrdinalIgnoreCase) ? null : version;
    }

    /// <summary>
    /// The bundled "what's new" page as body HTML, one section per app version. The section for the
    /// version being run is headed {{version}} in the file, because a version only gets its number
    /// when it is released; it is given the real one here. Null if the file is missing.
    /// </summary>
    public string? RenderWhatsNew()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "WhatsNew.md");
        string markdown;
        try
        {
            markdown = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWhatsNewMissing(_log, ex, path);
            return null;
        }

        return RenderWhatsNew(markdown, AppVersion);
    }

    /// <summary>Renders a "what's new" page, giving the {{version}} section the number <paramref name="version"/> (or "This build" for none).</summary>
    internal string RenderWhatsNew(string markdown, string? version)
    {
        markdown = markdown.Replace("## {{version}}", "## " + (version ?? "This build"), StringComparison.Ordinal);
        return RenderText("WhatsNew.md", markdown).BodyHtml;
    }

    /// <summary>The bundled user manual shown as a welcome screen when no file is opened.</summary>
    public string? GetUserManualPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "UserManual.md");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// True for the bundled manual. It lives in the install directory, which is nobody's working
    /// folder, so it must not become the root of the file tree.
    /// </summary>
    public static bool IsUserManual(string path) =>
        string.Equals(
            Path.GetFullPath(path),
            Path.Combine(AppContext.BaseDirectory, "UserManual.md"),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The file a command-line argument names. A relative path is relative to the working directory and
    /// nothing else: it used to be searched for in every folder above the executable as well, which let
    /// `MarkdownBlaze readme.md` open some other readme.md from outside the folder the reader was in.
    /// </summary>
    private static string? ResolveExisting(string path) => File.Exists(path) ? Path.GetFullPath(path) : null;

    // ---- headings / title -----------------------------------------------------------------------

    private static List<Heading> ExtractHeadings(MarkdownDocument document)
    {
        var headings = new List<Heading>();
        foreach (var h in document.Descendants<HeadingBlock>())
        {
            var text = InlineText(h.Inline);
            if (string.IsNullOrWhiteSpace(text)) continue;
            headings.Add(new Heading(h.Level, text, h.GetAttributes().Id ?? string.Empty));
        }

        return headings;
    }

    private static string DeriveTitle(List<Heading> headings, string path)
    {
        var heading = headings.FirstOrDefault(h => h.Level == 1) ?? (headings.Count > 0 ? headings[0] : null);
        return heading is not null && !string.IsNullOrWhiteSpace(heading.Text)
            ? heading.Text
            : Path.GetFileNameWithoutExtension(path);
    }

    private static string InlineText(ContainerInline? inline)
    {
        if (inline is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var node in inline.Descendants())
        {
            switch (node)
            {
                case LiteralInline literal:
                    _ = sb.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    _ = sb.Append(code.Content);
                    break;
            }
        }

        return sb.ToString().Trim();
    }

    // ---- link / image rewriting -----------------------------------------------------------------

    private static void RewriteLinksAndImages(MarkdownDocument document, ImageRefs images, LinkTargets targets)
    {
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (link.IsImage)
                images.Rewrite(link);
            else
                RewriteLink(link, images.BaseDir, targets);
        }
    }

    /// <summary>
    /// Internal markdown links (relative paths, folders → index.md, extension-less → .md) become
    /// in-app navigation targets carried in a data-nav attribute (see <see cref="LinkTargets"/> for how
    /// it gets onto the page); web and mail links are left as-is
    /// (and are opened only if they pass <see cref="ExternalLinks"/>). A link that points at a local
    /// file by an absolute address — <c>C:/x.md</c> or <c>file:///C:/x.md</c> — is either a Markdown
    /// document that exists, and then navigates in-app, or it is made inert: it is never left to be
    /// opened as an "external" link, because the shell would run whatever it names.
    /// </summary>
    private static void RewriteLink(LinkInline link, string baseDir, LinkTargets targets)
    {
        var url = link.Url;
        if (string.IsNullOrWhiteSpace(url)) return;
        if (url.StartsWith('#') || url.StartsWith("//", StringComparison.Ordinal)) return;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            RewriteAbsoluteLink(link, absolute, targets);
            return;
        }

        var splitIndex = url.IndexOfAny(['#', '?']);
        var path = splitIndex >= 0 ? url[..splitIndex] : url;
        if (path.Length == 0 || path.StartsWith('/') || path.StartsWith('\\')) return;

        try
        {
            var decoded = Uri.UnescapeDataString(path);
            var fullPath = Path.GetFullPath(Path.Combine(baseDir, decoded));

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
            {
                link.Url = targets.Add(fullPath);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A path the file system cannot make sense of: the link stays as the author wrote it.
            LogLinkNotResolved(Logger, ex, url);
        }
    }

    /// <summary>The part of <see cref="RewriteLink"/> that deals with a link written as an absolute address.</summary>
    private static void RewriteAbsoluteLink(LinkInline link, Uri absolute, LinkTargets targets)
    {
        // http:, mailto:, ... are external links; the page and ExternalLinks decide what opens.
        if (!absolute.IsFile) return;

        // file:///C:/x.md, or what the Windows parser makes of C:/x.md and \\server\share\x.md.
        // A UNC target would make Windows open a network session just to look at it.
        var local = absolute.IsUnc ? null : absolute.LocalPath;
        if (local is not null
            && LocalPath.IsSafe(local)
            && local.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            && File.Exists(local))
        {
            link.Url = targets.Add(Path.GetFullPath(local));
        }
        else
        {
            link.GetAttributes().AddProperty("data-md-blocked", "link");
            link.Url = "#";
        }
    }

    /// <summary>
    /// Carries the in-app targets of one document's links through the sanitizer.
    /// <para>
    /// <c>data-nav</c> is what the page follows without asking: the target is opened on a click. So it must
    /// be something only this class can put on the page — and a sanitizer that lets <c>data-nav</c> through
    /// cannot tell the attribute written here from one an author wrote by hand in an HTML block, which
    /// would make any Markdown file on the machine a link target. The sanitizer therefore removes every
    /// <c>data-nav</c>, and a link is given a token as its address (random per render, so a document cannot
    /// forge one — the mechanism <see cref="ImageRefs"/> uses for pictures); <see cref="Restore"/>
    /// swaps the token for <c>href="#" data-nav="target"</c> after the sanitizer has run.
    /// </para>
    /// </summary>
    private sealed class LinkTargets
    {
        private readonly string _prefix = "mdnav-" + Guid.NewGuid().ToString("N") + "-";
        private readonly List<string> _targets = [];

        /// <summary>Registers an in-app target; the result is the address to give the link for now.</summary>
        public string Add(string target)
        {
            _targets.Add(target);
            return _prefix + (_targets.Count - 1).ToString(CultureInfo.InvariantCulture);
        }

        public string Restore(string html)
        {
            // As for images: the closing quote is part of what is replaced, so that token 1 cannot match
            // the start of token 10.
            for (var i = 0; i < _targets.Count; i++)
            {
                var token = "href=\"" + _prefix + i.ToString(CultureInfo.InvariantCulture) + "\"";
                html = html.Replace(token, "href=\"#\" data-nav=\"" + WebUtility.HtmlEncode(_targets[i]) + "\"", StringComparison.Ordinal);
            }

            return html;
        }
    }

    /// <summary>
    /// Points the local images of one document at <see cref="AssetHandler"/> (<c>mdasset://</c>), within
    /// the limits of <see cref="LocalPath"/>: image types only, never a UNC or device path, a cap per
    /// image and a cap for the document. The bytes are not read here — the WebView asks for them when it
    /// draws the page — but each image is opened, by the same <see cref="LocalPath.TryOpenImage"/> the
    /// handler uses, so that one that would be refused is a placeholder in the markup rather than a
    /// broken picture, and so that the document's budget can be added up.
    /// <para>
    /// The addresses are not put into the HTML where the sanitizer would see them: <c>mdasset</c> is not
    /// a scheme it lets through, so that an address an author wrote by hand is removed. Each image is
    /// given a token in the markup instead (random per render, so a document cannot forge one) and
    /// <see cref="Restore"/> swaps the real address in once the sanitizer is done. The render's own
    /// <see cref="Token"/> is also the one the handler is told to accept (<see cref="AssetGrant"/>).
    /// </para>
    /// </summary>
    private sealed class ImageRefs(string baseDir, CancellationToken cancellationToken)
    {
        private readonly List<string> _addresses = [];
        private long _remaining = LocalPath.MaxDocumentImageBytes;

        public string BaseDir { get; } = baseDir;

        /// <summary>Random per render: part of every address this render makes, and what the handler checks.</summary>
        // Decided: a fresh token for every render. A reload (file watcher, F5) therefore gives every image
        // a new address, so the WebView asks for each of them again and the browser cache cannot help; the
        // bytes come from the file each time, never through the page. That is deliberate: reusing the token
        // would let the cache work, but an image that was edited would stay as it was, and showing the
        // edited picture matters more than saving a file read.
        public string Token { get; } = Guid.NewGuid().ToString("N");

        /// <summary>The grant the handler needs to serve this render's images, or null when it has none.</summary>
        public AssetGrant? Grant =>
            _addresses.Count > 0 ? new AssetGrant(Token, BaseDir) : null;

        private string Prefix => "mdimg-" + Token + "-";

        public void Rewrite(LinkInline image)
        {
            cancellationToken.ThrowIfCancellationRequested(); // images are the slow part of a document that has many

            var url = image.Url;
            // data: images are the author's own and are vetted by the sanitizer.
            if (string.IsNullOrWhiteSpace(url) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return;

            var decoded = LocalPathOf(url, out var isLocal);
            if (!isLocal) return; // a remote image is kept as it is, by decision (SEC-5)
            if (decoded is null)
            {
                Skip(image, "blocked"); // //server/share/x.png, \\?\C:\x.png and the like
                return;
            }

            switch (LocalPath.Check(decoded, BaseDir, LocalPath.MaxImageBytes, _remaining, out var length))
            {
                case LocalPath.ImageOutcome.Allowed:
                    _remaining -= length;
                    image.Url = Prefix + _addresses.Count.ToString(CultureInfo.InvariantCulture);
                    _addresses.Add(AssetHandler.UrlFor(Token, decoded));
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

        /// <summary>Puts the addresses into the sanitized <paramref name="html"/>.</summary>
        public string Restore(string html)
        {
            // The quote that closes the attribute is part of what is replaced, so that the token for
            // image 1 cannot match the start of the token for image 10.
            for (var i = 0; i < _addresses.Count; i++)
                html = html.Replace("\"" + Prefix + i.ToString(CultureInfo.InvariantCulture) + "\"", "\"" + _addresses[i] + "\"", StringComparison.Ordinal);

            return html;
        }

        /// <summary>
        /// The decoded filesystem path an image URL stands for. <paramref name="isLocal"/> is false for
        /// anything that is not a local file (http:, data:, ...); the result is null when it is local
        /// but must not be read (UNC, device).
        /// </summary>
        private static string? LocalPathOf(string url, out bool isLocal)
        {
            isLocal = true;
            var trimmed = url.TrimStart();

            // Refuse before the URL parser gets to interpret it: on Windows "//server/share/x.png" is
            // parsed as an absolute file: URI, and merely looking for such a file starts an SMB session.
            if (LocalPath.IsUncOrDevice(trimmed)) return null;

            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            {
                if (!absolute.IsFile)
                {
                    isLocal = false;
                    return null;
                }

                if (absolute.IsUnc) return null;
                var local = absolute.LocalPath;
                return LocalPath.IsUncOrDevice(local) ? null : local;
            }

            var splitIndex = url.IndexOfAny(['#', '?']);
            var path = splitIndex >= 0 ? url[..splitIndex] : url;
            return Uri.UnescapeDataString(path);
        }

        private static void Skip(LinkInline image, string reason)
        {
            image.GetAttributes().AddProperty("data-md-skipped", reason);
            image.Url = string.Equals(reason, "too-large", StringComparison.Ordinal) ? Placeholders.TooLarge : Placeholders.Blocked;
        }
    }

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
