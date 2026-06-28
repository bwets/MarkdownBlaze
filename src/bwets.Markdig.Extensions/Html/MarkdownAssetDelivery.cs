namespace bwets.Markdig.Extensions.Html;

/// <summary>How asset content is delivered to the rendered page.</summary>
public enum MarkdownAssetDelivery
{
    /// <summary>Embed the content directly in the page (works anywhere, larger HTML).</summary>
    Inline,

    /// <summary>Reference assets by file name; the host must write <see cref="MarkdownAsset"/> files next to the page.</summary>
    External,
}
