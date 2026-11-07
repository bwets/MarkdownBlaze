using Android.Content;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Platform;

/// <summary>
/// <see cref="IFilePicker"/> for Android: the system's own choosers. A folder is chosen through the Storage Access Framework,
/// kept, and answered as the path of its mirror (<see cref="AndroidStorage"/>), so the viewer's folder flow works unchanged.
/// A file cannot be answered as a path — the chooser gives a <c>content://</c> address — so it goes the way an intent's document
/// does: <see cref="DocumentOpener"/> puts it in the <see cref="HostInbox"/> (inside a granted folder with its tree, otherwise
/// alone) and this answers "nothing chosen", which the viewer takes as there being nothing more for it to do.
/// </summary>
internal sealed partial class AndroidFilePicker : IFilePicker
{
    private static readonly ILogger Logger = Log.For(nameof(AndroidFilePicker));
    private readonly AndroidStorage _storage;
    private readonly DocumentOpener _opener;

    /// <summary>Creates the picker.</summary>
    /// <param name="storage">The granted folders.</param>
    /// <param name="opener">Opens a chosen document.</param>
    public AndroidFilePicker(AndroidStorage storage, DocumentOpener opener)
    {
        _storage = storage;
        _opener = opener;
    }

    /// <inheritdoc />
    public async Task<string?> PickFileAsync(string? startDir)
    {
        // The chooser has no starting folder to be told about, and Markdown has no media type every provider
        // knows, so all files are offered.
        try
        {
            using var intent = new Intent(Intent.ActionOpenDocument);
            _ = intent.AddCategory(Intent.CategoryOpenable);
            _ = intent.SetType("*/*");

            var result = await ActivityResults.LaunchAsync(intent).ConfigureAwait(false);
            if (result?.Data is { } uri) await _opener.OpenAsync(uri).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            LogPickFailed(Logger, ex);
        }

        return null;
    }

    /// <inheritdoc />
    public Task<string?> PickFolderAsync(string? startDir) => _storage.PickFolderAsync();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The file chooser failed")]
    private static partial void LogPickFailed(ILogger log, Exception ex);
}
