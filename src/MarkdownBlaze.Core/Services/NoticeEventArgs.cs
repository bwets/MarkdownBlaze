namespace MarkdownBlaze.Services;

/// <summary>A short message a host has for the reader.</summary>
/// <param name="message">What to say.</param>
internal sealed class NoticeEventArgs(string message) : EventArgs
{
    /// <summary>What to say.</summary>
    public string Message { get; } = message;
}
