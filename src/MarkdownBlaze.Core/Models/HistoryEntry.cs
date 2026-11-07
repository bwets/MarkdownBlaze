namespace MarkdownBlaze.Models;

internal sealed record HistoryEntry(string Path, string Title, DateTimeOffset LastOpenedUtc);
