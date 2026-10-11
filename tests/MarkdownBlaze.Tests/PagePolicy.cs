using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MarkdownBlaze.Security;

namespace MarkdownBlaze.Tests;

/// <summary>
/// What a document may name on the page — its classes, ids and roles, and how deep it may nest — written down
/// a second time, here, on purpose: the tests that check a rendered page against it must not be checking the
/// sanitizer against its own lists.
/// </summary>
internal static class PagePolicy
{
    /// <summary>The classes the renderer emits, exactly (see the sanitizer's comment for how they were collected).</summary>
    public static readonly string[] ClassNames =
    [
        "admonition", "contains-task-list", "footnote-back-ref", "footnote-ref", "footnotes", "math", "mermaid", "tag",
        "task-list-item", "wikilink",
    ];

    /// <summary>Prefixes of the classes the renderer emits: the fence's language and the callouts' parts.</summary>
    public static readonly string[] ClassPrefixes = ["language-", "admonition-"];

    /// <summary>The ids the app's markup and script use.</summary>
    public static readonly string[] ReservedIds = ["app", "shortcuts-title", "whats-new-title", "global-overlay"];

    /// <summary>Prefixes of the ids the app uses (<c>md-tree</c>, <c>md-splitter</c>, <c>tab-contents</c>, ...).</summary>
    public static readonly string[] ReservedIdPrefixes = ["md-", "tab-"];

    /// <summary>The ARIA roles that describe content, apart from <c>doc-*</c>.</summary>
    public static readonly string[] Roles =
    [
        "note", "img", "figure", "presentation", "none", "list", "listitem", "table", "row", "cell", "columnheader",
        "rowheader", "group", "region", "heading", "link", "separator",
    ];

    public static bool IsAllowedClass(string name) =>
        ClassNames.Contains(name, StringComparer.Ordinal) || ClassPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    public static bool IsReservedId(string id) =>
        ReservedIds.Contains(id, StringComparer.OrdinalIgnoreCase)
            || ReservedIdPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static bool IsAllowedRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase) || role.StartsWith("doc-", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses the body HTML of a page (the way the page's engine would, which does not recurse) so that it can be looked at.</summary>
    public static IElement Parse(string html) => new HtmlParser().ParseDocument("<!doctype html><html><body>" + html).Body!;

    /// <summary>
    /// Everything a page may not do about the app's own names, as a list of complaints (empty when it is clean): a
    /// class outside the list, a reserved id, a role outside the list, nesting deeper than a browser shows.
    /// </summary>
    /// <param name="html">The body HTML a render produced.</param>
    public static List<string> Violations(string html)
    {
        var complaints = new List<string>();
        var body = Parse(html);

        foreach (var element in body.QuerySelectorAll("[class]"))
        {
            foreach (var name in element.GetAttribute("class")!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(name => !IsAllowedClass(name)))
                complaints.Add($"<{element.LocalName}> has the class '{name}'");
        }

        foreach (var element in body.QuerySelectorAll("[id]"))
        {
            var id = element.GetAttribute("id")!;
            if (IsReservedId(id.Trim())) complaints.Add($"<{element.LocalName}> has the reserved id '{id}'");
        }

        foreach (var element in body.QuerySelectorAll("[role]"))
        {
            foreach (var role in element.GetAttribute("role")!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(role => !IsAllowedRole(role)))
                complaints.Add($"<{element.LocalName}> has the role '{role}'");
        }

        var depth = NestingLimit.DepthOf(body);
        if (depth > NestingLimit.MaxDepth) complaints.Add("the page is " + depth.ToString(System.Globalization.CultureInfo.InvariantCulture) + " elements deep");

        return complaints;
    }
}
