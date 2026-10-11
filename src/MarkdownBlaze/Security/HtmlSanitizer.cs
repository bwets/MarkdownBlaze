using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Ganss.Xss;

namespace MarkdownBlaze.Security;

/// <summary>
/// Cleans the HTML a document renders to before it reaches the page.
/// <para>
/// Markdown lets a document carry raw HTML, and Markdig passes it through verbatim:
/// <c>&lt;img src=x onerror=…&gt;</c>, <c>&lt;svg onload=…&gt;</c> and <c>&lt;a href="javascript:…"&gt;</c>
/// would all run in the page, which can call back into .NET and reach the network. So the output is
/// run through an allow-list: only the tags Markdig and its extensions emit, plus the harmless HTML
/// authors actually write in Markdown, survive. Everything else — scripts, frames, forms, SVG, event
/// handlers, styles — is removed, not escaped.
/// </para>
/// <para>
/// The allow-list covers what an element is called by as well as what it is: the page's own
/// markup shares its namespace with the document's. A CSS class that the app's stylesheet gives a
/// meaning to (<c>menu-backdrop</c> is a full-screen layer that takes every click, <c>rate-bar</c> and
/// <c>splash</c> look like the app asking for something), an <c>id</c> the app looks elements up by, and
/// an ARIA <c>role</c> that makes a screen reader announce a dialog are all things a document could
/// write to impersonate or cover the app, so only the classes the renderer itself emits, the ids that
/// are not the app's and the roles that describe content are let through (see <see cref="Classes"/>,
/// <see cref="ReservedIds"/>, <see cref="Roles"/>).
/// </para>
/// <para>
/// This is the first of two layers. The Content-Security-Policy in <c>wwwroot/index.html</c> is the
/// second, for whatever this gets wrong.
/// </para>
/// </summary>
internal static class HtmlSanitizer
{
    private static readonly string[] Tags =
    [
        "a", "abbr", "b", "blockquote", "br", "caption", "cite", "code", "col", "colgroup", "dd", "del",
        "details", "div", "dl", "dt", "em", "figcaption", "figure", "h1", "h2", "h3", "h4", "h5", "h6",
        "hr", "i", "img", "input", "ins", "kbd", "li", "mark", "ol", "p", "pre", "q", "s", "samp",
        "section", "small", "span", "strong", "sub", "summary", "sup", "table", "tbody", "td", "tfoot",
        "th", "thead", "tr", "u", "ul", "var",
    ];

    /// <summary>
    /// `type` and `start` are what Markdig writes on task-list checkboxes and ordered lists. `style` is
    /// here only so that `text-align` can be kept (see CssProperties): every other property is stripped,
    /// because CSS can reach out through url() and can cover the page with an overlay. `role` is what
    /// footnote markup carries in Markdig versions that emit it (`doc-noteref`, `doc-endnotes`) and what
    /// authors write in their own HTML; which roles are kept is decided in Narrow() (see Roles).
    /// </summary>
    private static readonly string[] Attributes =
    [
        "href", "src", "alt", "title", "width", "height", "align", "class", "id", "colspan", "rowspan",
        "open", "checked", "disabled", "lang", "dir", "type", "start", "style", "role",
    ];

    /// <summary>
    /// Markdig writes <c>style="text-align:center"</c> on table cells for aligned columns, so a
    /// <c>style</c> attribute cannot simply be dropped. Only this one property is kept; it cannot name
    /// a URL, so nothing else about <c>style</c> needs trusting.
    /// </summary>
    private static readonly string[] CssProperties = ["text-align"];

    /// <summary>
    /// data-processed is Mermaid's marker, data-tag is what the Obsidian-tags extension writes, and the
    /// data-md-* family is the app's own. Deliberately not here: <c>data-nav</c>, the attribute the page
    /// follows as an in-app link. Only the renderer may put it on a link, and it does so after the
    /// sanitizer has run (see ReferenceResolver), so one an author wrote never survives.
    /// </summary>
    private static readonly string[] DataAttributes = ["data-processed", "data-tag"];

    private const string DataMdPrefix = "data-md-";

    /// <summary>
    /// data: is allowed through the library's own scheme check so that an author's small inline image
    /// survives, and then narrowed in Narrow() to images in `src` — never in `href`.
    /// </summary>
    private static readonly string[] ImageDataTypes =
        ["png", "jpeg", "jpg", "gif", "webp", "bmp", "x-icon", "avif", "svg+xml"];

    /// <summary>
    /// The classes a document may carry: exactly what the pipeline emits (collected over the sample
    /// wiki, the test fixtures, the README and the manual), and nothing else. A class is the one thing
    /// the app's stylesheet and a document share, so every class the stylesheet gives the app's own
    /// chrome a meaning to (<c>menu-backdrop</c>, <c>rate-bar</c>, <c>splash</c>, <c>ctx</c>, ...) is one
    /// a document could use to cover it or to look like it — in raw HTML, in a Markdig generic attribute
    /// (<c>{.menu-backdrop}</c>) or a custom container (<c>::: menu-backdrop</c>). The element stays;
    /// only the class goes.
    /// </summary>
    private static readonly string[] ClassNames =
    [
        "admonition", "contains-task-list", "footnote-back-ref", "footnote-ref", "footnotes", "math", "mermaid",
        "tag", "task-list-item", "wikilink",
    ];

    private static readonly FrozenSet<string> Classes = ClassNames.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// <c>language-*</c> is the fence's language (and what highlight.js reads); <c>admonition-*</c> is the
    /// kind, title, icon and body of a callout. The app's own classes never start with either.
    /// </summary>
    private static readonly string[] ClassPrefixes = ["language-", "admonition-"];

    /// <summary>
    /// The ids the app's markup and script use, which a document must not repeat: the first element with
    /// an id wins <c>getElementById</c>, a stylesheet rule on <c>#md-tree</c> applies to every element
    /// that has it, and an <c>aria-labelledby</c> points at whichever carries the id. Found by looking at
    /// every <c>id</c> in the components and scripts: the sidebar (<c>md-tree</c>, <c>md-splitter</c>,
    /// <c>md-current-doc</c>) and its tabs (<c>tab-contents</c>, <c>tab-history</c>) are the <c>md-</c> and
    /// <c>tab-</c> prefixes, <c>shortcuts-title</c> and <c>whats-new-title</c> label the two dialogs,
    /// <c>global-overlay</c> is the container Fluent UI draws its menus and dialogs into (seen by listing every
    /// id of the running page; Fluent's other ids are eight random hex digits, new for every session, which a
    /// document cannot know), and <c>app</c> is the root element's name. The price: a heading whose generated
    /// id starts with <c>md-</c> or
    /// <c>tab-</c> (a heading called "MD notes" or "Tab stops") loses its id, so a link to it from the
    /// same page, and its entry in the outline, do not scroll to it. That is accepted; the alternative
    /// is to rename ids, which would break every link a document makes to its own headings.
    /// </summary>
    private static readonly string[] ReservedIdPrefixes = ["md-", "tab-"];

    private static readonly FrozenSet<string> ReservedIds =
        new[] { "app", "shortcuts-title", "whats-new-title", "global-overlay" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The roles that describe content. <c>doc-*</c> (the digital-publishing roles footnotes carry) and
    /// these. Deliberately not: <c>dialog</c>, <c>alert</c>, <c>alertdialog</c>, <c>menu</c>, <c>button</c>,
    /// <c>tab</c> and the rest of the widget roles, with which a document could make a screen reader
    /// announce something that looks like the app speaking.
    /// </summary>
    private static readonly string[] RoleNames =
    [
        "note", "img", "figure", "presentation", "none", "list", "listitem", "table", "row", "cell",
        "columnheader", "rowheader", "group", "region", "heading", "link", "separator",
    ];

    private static readonly FrozenSet<string> Roles = RoleNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private const string DocumentRolePrefix = "doc-";

    /// <summary>
    /// The sanitizer keeps state on the instance while it works, so a call needs one to itself; a lock
    /// around one shared instance would make a render wait for any other that is still going — and a
    /// render that was given up on can be going for a long time. A few instances are made, as many as
    /// there are renders at once, and handed out one call at a time.
    /// </summary>
    private static readonly ConcurrentBag<Ganss.Xss.HtmlSanitizer> Pool = [];

    private static Ganss.Xss.HtmlSanitizer Create()
    {
        // The parameterless constructor keeps the library's own lists of which attributes hold URLs
        // (href, src, srcset, ...) — those are what get their scheme checked — and the lists below
        // replace its allow-lists with ours.
        var sanitizer = new Ganss.Xss.HtmlSanitizer
        {
            AllowCssCustomProperties = false,
            AllowDataAttributes = true, // narrowed to the list above in Narrow()
        };
        Replace(sanitizer.AllowedTags, Tags);
        Replace(sanitizer.AllowedAttributes, Attributes);
        // Deliberately not here: mdasset, the scheme the app serves a document's own pictures under
        // (Security/AssetHandler.cs). The renderer puts those addresses on after this has run, so any
        // that reach the sanitizer were written by an author, in src or in href, and are removed.
        Replace(sanitizer.AllowedSchemes, ExternalLinks.AllowedSchemes.Append("data"));
        Replace(sanitizer.AllowedCssProperties, CssProperties);
        sanitizer.AllowedClasses.Clear(); // empty = every class; narrowed to Classes in Narrow(), which also knows prefixes
        sanitizer.PostProcessNode += (_, e) => Narrow(e);
        sanitizer.PostProcessDom += (_, e) => DropOtherInputs(e);
        sanitizer.FilterUrl += (_, e) => KeepFileAddress(e);
        return sanitizer;
    }

    private static void Replace(ISet<string> set, IEnumerable<string> values)
    {
        set.Clear();
        foreach (var value in values) _ = set.Add(value);
    }

    /// <summary>The sanitized form of <paramref name="html"/>, a fragment of body HTML.</summary>
    public static string Sanitize(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        using var document = Clean(html);
        return Serialize(document);
    }

    /// <summary>
    /// Parses <paramref name="html"/>, cuts its nesting down to <see cref="NestingLimit.MaxDepth"/> and
    /// sanitizes the result, in that order — the order is the point. The parser builds a tree of any
    /// depth without recursing, but the sanitizer's own walk (<c>Node.Normalize</c>) recurses once per
    /// level and would overflow the stack, so nothing deeper than a browser would show gets that far.
    /// <para>
    /// The result is still a tree, so that the renderer can go on with it (see ReferenceResolver) and
    /// serialize once, with <see cref="Serialize"/>. One thing in it is not yet what the page may have:
    /// a link or picture that names a file by an address with the <c>file:</c> scheme or a drive letter
    /// (see <see cref="KeepFileAddress"/>), which the next stage turns into an in-app link, a picture
    /// of the app's own or an inert link, and always does.
    /// </para>
    /// </summary>
    /// <param name="html">A fragment of body HTML.</param>
    public static IHtmlDocument Clean(string html)
    {
        var sanitizer = Pool.TryTake(out var pooled) ? pooled : Create();
        try
        {
            var document = sanitizer.HtmlParserFactory().ParseDocument("<!doctype html><html><body>" + html);
            if (document.Body is not { } body) return document;

            _ = NestingLimit.Apply(body);

            // A <body onload=…> or <html …> in the text merges its attributes into the page's own elements.
            foreach (var element in new IElement?[] { document.DocumentElement, document.Head, body })
            {
                if (element is null) continue;
                foreach (var attribute in element.Attributes.ToList())
                    _ = element.RemoveAttribute(attribute.Name);
            }

            return sanitizer.SanitizeDom(document, body);
        }
        finally
        {
            Pool.Add(sanitizer);
        }
    }

    /// <summary>The body of a document made by <see cref="Clean"/>, as markup, written the way the library writes it.</summary>
    /// <param name="document">The document.</param>
    public static string Serialize(IHtmlDocument document) =>
        document.Body is null
            ? string.Empty
            : document.Body.ChildNodes.ToHtml(Ganss.Xss.HtmlSanitizer.DefaultOutputFormatter);

    /// <summary>
    /// What the library cannot express as a list: prefixes, per-tag rules and per-attribute URL rules.
    /// </summary>
    private static void Narrow(PostProcessNodeEventArgs e)
    {
        if (e.Node is not IElement element) return;

        foreach (var attribute in element.Attributes.ToList())
        {
            if (attribute.Name.StartsWith("data-", StringComparison.OrdinalIgnoreCase)
                && !attribute.Name.StartsWith(DataMdPrefix, StringComparison.OrdinalIgnoreCase)
                && !DataAttributes.Contains(attribute.Name, StringComparer.OrdinalIgnoreCase))
            {
                _ = element.RemoveAttribute(attribute.Name);
            }
        }

        NarrowClasses(element);
        NarrowId(element);
        NarrowRole(element);

        if (element.HasAttribute("href") && IsDataUrl(element.GetAttribute("href")))
            _ = element.RemoveAttribute("href");

        if (element.HasAttribute("src") && !IsAllowedImageSource(element.GetAttribute("src")))
            _ = element.RemoveAttribute("src");

        // Task-list checkboxes are display-only in a viewer, so they are always disabled. (Inputs of any
        // other kind are removed in DropOtherInputs, once the whole document has been walked.)
        if (string.Equals(element.LocalName, "input", StringComparison.Ordinal) && IsCheckbox(element))
            element.SetAttribute("disabled", string.Empty);
    }

    private static void NarrowClasses(IElement element)
    {
        var written = element.GetAttribute("class");
        if (written is null) return;

        var kept = written
            .Split([' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries)
            .Where(IsAllowedClass)
            .ToArray();

        if (kept.Length == 0)
            _ = element.RemoveAttribute("class");
        else
            element.SetAttribute("class", string.Join(' ', kept));
    }

    private static bool IsAllowedClass(string name) =>
        Classes.Contains(name) || ClassPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    private static void NarrowId(IElement element)
    {
        var id = element.GetAttribute("id");
        if (id is null) return;

        var trimmed = id.Trim();
        if (ReservedIds.Contains(trimmed) || ReservedIdPrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            _ = element.RemoveAttribute("id");
    }

    private static void NarrowRole(IElement element)
    {
        var written = element.GetAttribute("role");
        if (written is null) return;

        var kept = written
            .Split([' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries)
            .Where(role => Roles.Contains(role) || role.StartsWith(DocumentRolePrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (kept.Length == 0)
            _ = element.RemoveAttribute("role");
        else
            element.SetAttribute("role", string.Join(' ', kept));
    }

    /// <summary>
    /// Lets the addresses of files through the library's scheme check, on a link or a picture and nowhere
    /// else: <c>file:///C:/x.md</c>, <c>C:/x.md</c>, and the ones the library takes for a scheme only
    /// because there is a colon somewhere in them (Markdig writes <c>C:\x.md</c> as <c>C:%5Cx.md</c>, and
    /// <c>\\?\C:\x.png</c> with its backslashes percent-encoded). A browser reads none of those as having
    /// a scheme, or reads a drive letter as one, so they cannot run anything; but the library removes
    /// them, which would leave a link to a document by its full path with no address at all and a refused
    /// picture with no placeholder. The renderer's next stage (ReferenceResolver) is where they are dealt
    /// with — an existing Markdown document becomes an in-app link, a picture goes through
    /// <see cref="LocalPath"/>, and anything else is made inert — and it is that stage's business to leave
    /// none of them in the page. An address with any other scheme stays refused.
    /// </summary>
    private static void KeepFileAddress(FilterUrlEventArgs e)
    {
        if (e.SanitizedUrl is not null) return;
        if (!string.Equals(e.Tag.LocalName, "a", StringComparison.Ordinal) && !string.Equals(e.Tag.LocalName, "img", StringComparison.Ordinal)) return;

        var scheme = SchemeOf(e.OriginalUrl);
        if (scheme is null || IsFileScheme(scheme)) e.SanitizedUrl = e.OriginalUrl.TrimStart();
    }

    /// <summary><c>file:</c>, or a single letter — a drive, which Windows addresses as <c>C:</c> and a URL parser takes for a scheme.</summary>
    /// <param name="scheme">A scheme as <see cref="SchemeOf"/> reads it.</param>
    internal static bool IsFileScheme(string scheme) =>
        scheme.Length == 1 || string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The scheme an address starts with, read the way a browser reads it — leading spaces and control
    /// characters skipped, tabs and newlines ignored wherever they are — or null when it has none, which
    /// is to say when it is a relative address. (The library's own test is cruder: anything before the
    /// first colon that is not followed by a slash or a hash.)
    /// </summary>
    /// <param name="address">The address as written.</param>
    internal static string? SchemeOf(string? address)
    {
        if (string.IsNullOrEmpty(address)) return null;

        var scheme = new StringBuilder();
        foreach (var c in address)
        {
            if (c is '\t' or '\n' or '\r') continue;
            if (scheme.Length == 0 && c <= ' ') continue;

            if (c == ':') return scheme.Length == 0 ? null : scheme.ToString();
            if (!IsSchemeCharacter(c, first: scheme.Length == 0)) return null;
            _ = scheme.Append(c);
        }

        return null;
    }

    private static bool IsSchemeCharacter(char c, bool first) =>
        first ? char.IsAsciiLetter(c) : char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.';

    /// <summary>
    /// Only task-list checkboxes are inputs a document may have.
    /// </summary>
    private static void DropOtherInputs(PostProcessDomEventArgs e)
    {
        foreach (var input in e.Document.QuerySelectorAll("input").Where(input => !IsCheckbox(input)).ToList())
            input.Remove();
    }

    private static bool IsCheckbox(IElement input) =>
        string.Equals(input.GetAttribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Browsers skip leading control characters and spaces, and ignore tabs and newlines anywhere
    /// inside a URL, so "  d\ta\nta:" is a data URL. The comparison has to see what the browser sees.
    /// </summary>
    internal static string Normalised(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        var head = url.Length > 64 ? url[..64] : url;
        return string.Concat(head.Where(c => c > ' '));
    }

    private static bool IsDataUrl(string? url) =>
        Normalised(url).StartsWith("data:", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedImageSource(string? url)
    {
        var normalised = Normalised(url);
        if (!normalised.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true; // the library vetted the scheme
        return ImageDataTypes.Any(type =>
            normalised.StartsWith("data:image/" + type + ";", StringComparison.OrdinalIgnoreCase)
                || normalised.StartsWith("data:image/" + type + ",", StringComparison.OrdinalIgnoreCase));
    }
}
