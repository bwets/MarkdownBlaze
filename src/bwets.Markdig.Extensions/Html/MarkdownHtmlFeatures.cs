using System.Text;
using Markdig;

namespace bwets.Markdig.Extensions.Html;

/// <summary>An ordered set of enabled <see cref="HtmlMarkdownFeature"/>s, composed into a page.</summary>
public sealed class MarkdownHtmlFeatures
{
    private readonly List<HtmlMarkdownFeature> _features = [];

    public MarkdownHtmlFeatures Add(HtmlMarkdownFeature feature)
    {
        _features.Add(feature);
        return this;
    }

    public IReadOnlyList<HtmlMarkdownFeature> Features => _features;

    /// <summary>Applies every feature's Markdig pipeline configuration.</summary>
    public void Configure(MarkdownPipelineBuilder pipeline)
    {
        foreach (var feature in _features)
        {
            feature.Configure(pipeline);
        }
    }

    /// <summary>All distinct assets across the enabled features (host writes these when delivering externally).</summary>
    public IReadOnlyList<MarkdownAsset> Assets =>
    [
        .. _features.SelectMany(f => f.Assets)
            .GroupBy(a => a.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()),
    ];

    /// <summary>Builds the <c>&lt;head&gt;</c> content (style assets + extra head markup) for the features.</summary>
    public string RenderHead(MarkdownAssetDelivery delivery)
    {
        var sb = new StringBuilder();
        foreach (var feature in _features)
        {
            foreach (var asset in feature.Assets.Where(a => a.Kind == MarkdownAssetKind.Style))
            {
                sb.AppendLine(RenderAsset(asset, delivery));
            }

            if (!string.IsNullOrEmpty(feature.Head))
            {
                sb.AppendLine(feature.Head);
            }
        }

        return sb.ToString();
    }

    /// <summary>Builds the end-of-body content (script assets + init markup) for the features.</summary>
    public string RenderBodyEnd(MarkdownAssetDelivery delivery)
    {
        var sb = new StringBuilder();
        foreach (var feature in _features)
        {
            foreach (var asset in feature.Assets.Where(a => a.Kind == MarkdownAssetKind.Script))
            {
                sb.AppendLine(RenderAsset(asset, delivery));
            }

            if (!string.IsNullOrEmpty(feature.BodyEnd))
            {
                sb.AppendLine(feature.BodyEnd);
            }
        }

        return sb.ToString();
    }

    private static string RenderAsset(MarkdownAsset asset, MarkdownAssetDelivery delivery)
    {
        if (delivery == MarkdownAssetDelivery.External)
        {
            return asset.Kind == MarkdownAssetKind.Script
                ? $"<script src=\"{asset.FileName}\"></script>"
                : $"<link rel=\"stylesheet\" href=\"{asset.FileName}\" />";
        }

        return asset.Kind == MarkdownAssetKind.Script
            ? "<script>" + EscapeScript(asset.Content) + "</script>"
            : "<style>" + asset.Content + "</style>";
    }

    // Prevent an embedded "</script>" literal (e.g. inside a grammar) from closing the inline script tag.
    private static string EscapeScript(string content) =>
        content.Replace("</script", "<\\/script");
}
