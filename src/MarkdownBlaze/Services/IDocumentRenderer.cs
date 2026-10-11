using MarkdownBlaze.Models;

namespace MarkdownBlaze.Services;

/// <summary>
/// What <see cref="NavigationService"/> needs of the thing that turns a file into a page: the document the
/// app was started with, and the two ways to render one. <see cref="MarkdownService"/> is the one
/// implementation; the interface is there so that a test can put a render in its place that takes as long
/// as the test says — the render deadline and the cap on renders left running cannot be tested against
/// a real document without waiting for it.
/// </summary>
internal interface IDocumentRenderer
{
    /// <summary>The document to open for the command line: the first argument that names a readable supported file, else the manual.</summary>
    /// <param name="args">The command-line arguments (without the executable).</param>
    string? GetStartupFilePath(IEnumerable<string> args);

    /// <summary>Reads and renders a file, saying what became of it. Runs on a render thread, never the UI thread.</summary>
    /// <param name="markdownFilePath">The file.</param>
    /// <param name="cancellationToken">Cancelled when the result is no longer wanted; the render looks at it between its stages.</param>
    FileRender RenderFile(string markdownFilePath, CancellationToken cancellationToken = default);

    /// <summary>Renders Markdown that has no backing file (text dropped onto the window).</summary>
    /// <param name="fileName">The name it was dropped under.</param>
    /// <param name="markdown">The text.</param>
    RenderResult RenderText(string fileName, string markdown);
}
