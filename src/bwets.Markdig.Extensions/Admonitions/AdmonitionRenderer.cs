using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace bwets.Markdig.Extensions.Admonitions;

/// <summary>Renders an <see cref="AdmonitionBlock"/> as a styled callout.</summary>
public sealed class AdmonitionRenderer : HtmlObjectRenderer<AdmonitionBlock>
{
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["note"] = "note", ["seealso"] = "note",
        ["abstract"] = "abstract", ["summary"] = "abstract", ["tldr"] = "abstract",
        ["info"] = "info", ["todo"] = "info", ["important"] = "info",
        ["tip"] = "tip", ["hint"] = "tip",
        ["success"] = "success", ["check"] = "success", ["done"] = "success",
        ["question"] = "question", ["help"] = "question", ["faq"] = "question",
        ["warning"] = "warning", ["caution"] = "warning", ["attention"] = "warning",
        ["failure"] = "danger", ["fail"] = "danger", ["missing"] = "danger",
        ["danger"] = "danger", ["error"] = "danger",
        ["bug"] = "bug",
        ["example"] = "example",
        ["quote"] = "quote", ["cite"] = "quote",
    };

    private static readonly Dictionary<string, string> Icons = new()
    {
        ["note"] = "📝", ["abstract"] = "📑", ["info"] = "ℹ️", ["tip"] = "💡",
        ["success"] = "✅", ["question"] = "❓", ["warning"] = "⚠️", ["danger"] = "🚨",
        ["bug"] = "🐛", ["example"] = "🧪", ["quote"] = "💬",
    };

    protected override void Write(HtmlRenderer renderer, AdmonitionBlock obj)
    {
        var canonical = Synonyms.TryGetValue(obj.Kind, out var c) ? c : "note";
        var icon = Icons.TryGetValue(canonical, out var i) ? i : "📌";
        var tag = obj.Collapsible ? "details" : "div";
        var titleTag = obj.Collapsible ? "summary" : "div";

        renderer.EnsureLine();
        renderer.Write("<").Write(tag).Write(" class=\"admonition admonition-").Write(canonical).Write("\"");
        if (obj.Collapsible && obj.Open)
        {
            renderer.Write(" open");
        }

        renderer.Write(">");

        var showTitle = obj.Collapsible || obj.Title != string.Empty;
        if (showTitle)
        {
            var title = string.IsNullOrEmpty(obj.Title) ? Capitalize(obj.Kind) : obj.Title!;
            renderer.Write("<").Write(titleTag).Write(" class=\"admonition-title\">");
            renderer.Write("<span class=\"admonition-icon\">").Write(icon).Write("</span>");
            renderer.WriteEscape(title);
            renderer.Write("</").Write(titleTag).Write(">");
        }

        renderer.Write("<div class=\"admonition-body\">");
        renderer.WriteChildren(obj);
        renderer.Write("</div>");
        renderer.Write("</").Write(tag).WriteLine(">");
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
