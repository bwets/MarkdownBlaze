namespace MarkdownBlaze.Models;

/// <summary>A navigation was asked for a file that is no longer there.</summary>
internal sealed class MissingFileEventArgs(string path) : EventArgs
{
    /// <summary>The full path that was asked for.</summary>
    public string Path { get; } = path;
}
