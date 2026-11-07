namespace MarkdownBlaze.Platform;

/// <summary>
/// Printing, for a host whose web view cannot print the page by itself. The page does the preparation
/// (the print theme, the plain flow instead of sheets, the diagrams drawn again) and then, instead of
/// calling <c>window.print()</c>, asks the host through <see cref="PrintAsync"/>; it puts everything
/// back when that returns.
/// </summary>
internal interface IPrinter
{
    /// <summary>
    /// Prints the page as it now is, and returns when the job has been handed on or cancelled.
    /// </summary>
    /// <returns>
    /// True when the host printed. False leaves the printing to the page's own <c>window.print()</c>, which
    /// is all the desktop's web view needs.
    /// </returns>
    ValueTask<bool> PrintAsync();
}
