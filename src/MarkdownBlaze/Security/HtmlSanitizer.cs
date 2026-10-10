using AngleSharp.Dom;
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
    /// authors write in their own HTML: an ARIA role only names what an element is, it cannot run anything.
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
    /// sanitizer has run (see MarkdownService.LinkTargets), so one an author wrote never survives.
    /// </summary>
    private static readonly string[] DataAttributes = ["data-processed", "data-tag"];

    private const string DataMdPrefix = "data-md-";

    /// <summary>
    /// data: is allowed through the library's own scheme check so that an author's small inline image
    /// survives, and then narrowed in Narrow() to images in `src` — never in `href`.
    /// </summary>
    private static readonly string[] ImageDataTypes =
        ["png", "jpeg", "jpg", "gif", "webp", "bmp", "x-icon", "avif", "svg+xml"];

    private static readonly Ganss.Xss.HtmlSanitizer Inner = Create();

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
        // (Security/AssetHandler.cs). The renderer swaps those addresses in after this has run, so any
        // that reach the sanitizer were written by an author, in src or in href, and are removed.
        Replace(sanitizer.AllowedSchemes, ExternalLinks.AllowedSchemes.Append("data"));
        Replace(sanitizer.AllowedCssProperties, CssProperties);
        sanitizer.AllowedClasses.Clear(); // empty = every class
        sanitizer.PostProcessNode += (_, e) => Narrow(e);
        sanitizer.PostProcessDom += (_, e) => DropOtherInputs(e);
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
        // The library keeps per-call state on the instance; rendering can happen on a file-watcher
        // thread as well as the renderer's, so calls are taken one at a time.
        lock (Inner) return Inner.Sanitize(html);
    }

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

        if (element.HasAttribute("href") && IsDataUrl(element.GetAttribute("href")))
            _ = element.RemoveAttribute("href");

        if (element.HasAttribute("src") && !IsAllowedImageSource(element.GetAttribute("src")))
            _ = element.RemoveAttribute("src");

        // Task-list checkboxes are display-only in a viewer, so they are always disabled. (Inputs of any
        // other kind are removed in DropOtherInputs, once the whole document has been walked.)
        if (string.Equals(element.LocalName, "input", StringComparison.Ordinal) && IsCheckbox(element))
            element.SetAttribute("disabled", string.Empty);
    }

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
    private static string Normalised(string? url)
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
