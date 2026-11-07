namespace MarkdownBlaze.Interop;

/// <summary>A sheet of paper as the page view draws it: A4 in millimetres, plus the on-screen gap between sheets.</summary>
internal sealed record SheetPage(double WidthMm, double HeightMm, double MarginMm, int GapPx)
{
    public static SheetPage A4 { get; } = new(210, 297, 15, 16);
}
