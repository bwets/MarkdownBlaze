namespace bwets.Markdig.Extensions.Html;

/// <summary>A CSS or JS asset that a feature needs, which the host may inline or write as a file.</summary>
public sealed class MarkdownAsset
{
    public MarkdownAsset(string fileName, MarkdownAssetKind kind, string content)
    {
        FileName = fileName;
        Kind = kind;
        Content = content;
    }

    public string FileName { get; }

    public MarkdownAssetKind Kind { get; }

    public string Content { get; }
}
