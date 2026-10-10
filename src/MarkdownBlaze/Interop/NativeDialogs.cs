using MarkdownBlaze.Services;
using Photino.NET;

namespace MarkdownBlaze.Interop;

/// <summary>
/// Holds the app's <see cref="PhotinoWindow"/> so Blazor components can use native features such as the
/// OS "open file" dialog. The window does not exist yet when the container is built, so it is handed over
/// once, at startup, with <see cref="Attach"/>; until then (and in tests) every dialog answers "nothing chosen".
/// </summary>
internal sealed class NativeDialogs
{
    private PhotinoWindow? _window;

    /// <summary>Gives the dialogs the window they belong to. The app has one window, so this is done once.</summary>
    /// <param name="window">The main window.</param>
    /// <exception cref="InvalidOperationException">A window is attached already.</exception>
    public void Attach(PhotinoWindow window)
    {
        if (Interlocked.CompareExchange(ref _window, window, comparand: null) is not null)
            throw new InvalidOperationException("The dialogs belong to a window already.");
    }

    /// <summary>Shows the native open-file dialog filtered to supported types; returns the chosen path or null.</summary>
    public async Task<string?> PickFileAsync(string? startDir)
    {
        var window = _window;
        if (window is null) return null;

        var filters = new (string, string[])[]
        {
            ("Markdown", MarkdownService.SupportedExtensions.Select(ext => "*" + ext).ToArray()),
            ("All files", ["*.*"]),
        };

        var files = await window.ShowOpenFileAsync("Open file", startDir, multiSelect: false, filters).ConfigureAwait(false);
        return files is { Length: > 0 } ? files[0] : null;
    }

    /// <summary>Shows the native folder picker; returns the chosen folder or null.</summary>
    public async Task<string?> PickFolderAsync(string? startDir)
    {
        var window = _window;
        if (window is null) return null;

        var folders = await window.ShowOpenFolderAsync("Open folder", startDir, multiSelect: false).ConfigureAwait(false);
        return folders is { Length: > 0 } ? folders[0] : null;
    }
}
