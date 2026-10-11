using System.Collections.Frozen;
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
/// serves from disk (see <see cref="AssetHandler"/>) rather than being carried in the page — both by
/// <see cref="ReferenceResolver"/>, on the sanitized HTML.
/// </summary>
internal sealed partial class MarkdownService : IDocumentRenderer
{
    private static readonly ILogger Logger = Log.For(nameof(MarkdownService));

    /// <summary>
    /// The largest file that is rendered. Parsing, sanitising and handing a multi-megabyte page to the
    /// WebView takes long enough to look like a hang; a log file opened as Markdown by mistake is the
    /// usual culprit, and a message beats a frozen window.
    /// </summary>
    public const long MaxFileBytes = 32L * 1024 * 1024;

    /// <summary>
    /// How many links of one document are looked at on disk (does the file exist? is it a folder?). Each
    /// is a file-system call, and a document with fifty thousand of them is one the reader did not write
    /// for reading; a link past the limit is inert, marked <c>data-md-blocked="limit"</c>.
    /// </summary>
    public const int MaxResolvedLinks = 5000;

    /// <summary>
    /// How many local pictures of one document are opened to see whether they may be shown; a picture past
    /// the limit is a placeholder that says there are too many, marked <c>data-md-skipped="limit"</c>.
    /// </summary>
    public const int MaxImages = 2000;

    /// <summary>
    /// How deeply Markdown may nest — lists in quotes in lists, emphasis in links — before Markdig refuses
    /// the document with an <see cref="ArgumentException"/> (<see cref="IsNestingLimit"/>). Markdig's own
    /// default for the version in use is the same number; it is written down so that a newer Markdig
    /// cannot raise it without this line changing, and the renderer recurses once per level. Raw HTML is
    /// not Markdown nesting and is limited separately, to <see cref="NestingLimit.MaxDepth"/>.
    /// </summary>
    public const int MaxMarkdownNesting = 128;

    /// <summary>The Markdown pipeline. Internal so that a test can see what it writes before the sanitizer has had its say.</summary>
    internal static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder { MaximumNestingDepth = MaxMarkdownNesting }
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

    /// <summary>
    /// <see cref="RenderFile"/> on a thread of its own (see <see cref="RenderWorker"/>), so that the window
    /// keeps answering while a big document renders, and the render has the stack a deeply nested one needs.
    /// </summary>
    public Task<FileRender> RenderFileAsync(string markdownFilePath, CancellationToken cancellationToken) =>
        RenderWorker.RunAsync(() => RenderFile(markdownFilePath, cancellationToken), cancellationToken);

    /// <summary>
    /// Whether <paramref name="exception"/> is Markdig giving up on a document because its Markdown is
    /// nested too deeply (an <see cref="ArgumentException"/> with no parameter name, whose text says so).
    /// Such a document is not an error in the app but a property of the document, and is reported as one.
    /// </summary>
    /// <param name="exception">What a render threw.</param>
    public static bool IsNestingLimit(Exception exception) =>
        exception is ArgumentException { ParamName: null }
            && exception.Message.Contains("too deeply nested", StringComparison.OrdinalIgnoreCase);

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

        using var writer = new StringWriter();
        var htmlRenderer = new HtmlRenderer(writer);
        Pipeline.Setup(htmlRenderer);
        _ = htmlRenderer.Render(document);
        writer.Flush();
        cancellationToken.ThrowIfCancellationRequested();

        // The document is untrusted, and so is the raw HTML Markdig lets through in it: nothing reaches
        // the page without passing the sanitizer. It is done here, in the one place every render goes
        // through, so text dropped onto the window and the What's new page are covered as well. The HTML
        // is parsed once, cut down to a depth that is safe to walk (a browser would flatten it anyway),
        // sanitized, and only then are the links and pictures resolved — Markdown's and the author's
        // own HTML alike, by the one piece of code (ReferenceResolver). The in-app link targets
        // (data-nav) and the picture addresses (mdasset://) are things only that stage may put on the
        // page: the sanitizer has removed every one an author wrote before it runs. Serialized once.
        using var cleaned = HtmlSanitizer.Clean(writer.ToString());
        var references = new ReferenceResolver(baseDir, cancellationToken);
        if (cleaned.Body is { } bodyElement) references.Resolve(bodyElement);
        cancellationToken.ThrowIfCancellationRequested();
        var body = HtmlSanitizer.Serialize(cleaned);

        var headings = ExtractHeadings(document);
        var title = DeriveTitle(headings, path);
        var elapsed = (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogRendered(Logger, file, elapsed, body.Length);

        return new RenderResult(path, file, title, body, headings, LanguageOf(document), references.Grant);
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
}
