namespace MarkdownBlaze.Interop;

/// <summary>How far, and in what steps, the document may be zoomed. The toolbar and the page share these.</summary>
internal sealed record ZoomLimits(double Min, double Max, double Step)
{
    public static ZoomLimits Default { get; } = new(0.4, 4, 1.1);
}
