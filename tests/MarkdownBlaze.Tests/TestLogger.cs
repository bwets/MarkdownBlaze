using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Tests;

/// <summary>A logger that keeps what it is given, so a test can say that a failure was reported.</summary>
internal sealed class TestLogger : ILogger
{
    private readonly Lock _lock = new();
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    /// <summary>Everything logged so far, oldest first.</summary>
    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
    {
        get
        {
            lock (_lock) return [.. _entries];
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_lock) _entries.Add((logLevel, formatter(state, exception), exception));
    }
}
