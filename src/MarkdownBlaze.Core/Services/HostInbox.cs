namespace MarkdownBlaze.Services;

/// <summary>
/// Where a host puts what happens to it while the window is already there: a document to show, a notice for the
/// reader, a mirror that has changed, the app coming back to the front. The viewer takes them from here. It is one
/// object because the host is not on the renderer's thread and, at start-up, not necessarily after the viewer: documents
/// wait here until the viewer asks, and every notification may come from any thread (the viewer hands it on to its own).
/// The desktop host does not register it, and the viewer works without.
/// </summary>
internal sealed class HostInbox
{
    private readonly Queue<IncomingDocument> _documents = new();
    private readonly Lock _lock = new();

    /// <summary>Raised when a document has been posted; the handler takes it with <see cref="TryTake"/>.</summary>
    public event EventHandler? DocumentPosted;

    /// <summary>Raised with a short message for the reader (a toast).</summary>
    public event EventHandler<NoticeEventArgs>? NoticeRaised;

    /// <summary>Raised when the files of a mirror changed behind the tree's back, so the tree should be read again.</summary>
    public event EventHandler? TreeChanged;

    /// <summary>Raised when the app is in front again after being away.</summary>
    public event EventHandler? Resumed;

    /// <summary>Queues a document for the viewer.</summary>
    /// <param name="document">What to show.</param>
    public void Post(IncomingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_lock) _documents.Enqueue(document);
        DocumentPosted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes the oldest document that is waiting.</summary>
    /// <param name="document">The document, when there was one.</param>
    public bool TryTake(out IncomingDocument? document)
    {
        lock (_lock) return _documents.TryDequeue(out document);
    }

    /// <summary>Tells the reader something short.</summary>
    /// <param name="message">What to say.</param>
    public void Notify(string message) => NoticeRaised?.Invoke(this, new NoticeEventArgs(message));

    /// <summary>Says that the mirrored files changed.</summary>
    public void NotifyTreeChanged() => TreeChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Says that the app came back to the front.</summary>
    public void NotifyResumed() => Resumed?.Invoke(this, EventArgs.Empty);
}
