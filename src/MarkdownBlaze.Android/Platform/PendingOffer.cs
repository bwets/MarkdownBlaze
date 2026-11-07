namespace MarkdownBlaze.Platform;

/// <summary>What the host keeps of a single-file document whose folder it offered, so that <em>Allow</em> can point the chooser at it and tell whether the folder picked holds the document.</summary>
/// <param name="Document">What was known of the document when it was opened.</param>
/// <param name="Offer">The folder offered.</param>
internal sealed record PendingOffer(DocumentInfo Document, Services.Storage.FolderOffer Offer);
