namespace MarkdownBlaze.Services;

/// <summary>How far the system's bars and the display cutout reach into each edge of the page, in CSS pixels.</summary>
/// <param name="Top">The status bar.</param>
/// <param name="Right">A cutout or bar at the right in landscape.</param>
/// <param name="Bottom">The gesture or navigation bar.</param>
/// <param name="Left">A cutout or bar at the left in landscape.</param>
internal sealed record WindowInsets(double Top, double Right, double Bottom, double Left)
{
    /// <summary>Nothing covers the page: the desktop window.</summary>
    public static WindowInsets None { get; } = new(0, 0, 0, 0);
}
