using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Services;

/// <summary>
/// A rolling log file under <see cref="AppPaths.LogFolder"/> — the one place a "it just does nothing"
/// report can be looked up. Deliberately small (no logging package for something this size): lines are
/// appended to <c>markdownblaze.log</c>; once it passes the size cap it becomes <c>markdownblaze.1.log</c>
/// (the older ones move up, the oldest is deleted), so the folder never grows beyond a few megabytes.
/// <para>
/// Every line carries the process id, because "Open in new window" starts another process that writes to
/// the same file. Logging must never take the app down: a file that cannot be written is skipped.
/// </para>
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private const string BaseName = "markdownblaze";

    /// <summary>
    /// Next Line, Line Separator and Paragraph Separator: Unicode's line breaks other than CR and LF, which some
    /// viewers break a line at. Written as numbers, because a separator typed into a source file ends its line.
    /// </summary>
    private const char NextLine = (char)0x0085;
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    private readonly string _folder;
    private readonly long _maxBytes;
    private readonly int _keep;
    private readonly int _pid = Environment.ProcessId;
    private readonly Lock _lock = new();

    /// <summary>Creates a provider that writes under <paramref name="folder"/>.</summary>
    /// <param name="folder">Where the files go; created on the first write.</param>
    /// <param name="maxBytes">The size at which the current file is rolled.</param>
    /// <param name="keep">How many rolled files are kept besides the current one.</param>
    public FileLoggerProvider(string folder, long maxBytes = 1024 * 1024, int keep = 3)
    {
        _folder = folder;
        _maxBytes = maxBytes;
        _keep = keep;
    }

    private string CurrentFile => Path.Combine(_folder, BaseName + ".log");

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        // Every line is written and closed as it is logged; there is nothing left to flush.
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [")
            .Append(_pid.ToString(CultureInfo.InvariantCulture))
            .Append("] ")
            .Append(Abbreviation(level))
            .Append(' ')
            .Append(category)
            .Append(": ")
            .AppendLine(OneLine(message));
        if (exception is not null) _ = line.AppendLine(OneLine(exception.ToString()));

        lock (_lock)
        {
            try
            {
                _ = Directory.CreateDirectory(_folder);
                RollIfNeeded();
                // ReadWrite sharing: another MarkdownBlaze process may be appending at the same time.
                using var stream = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Write(Encoding.UTF8.GetBytes(line.ToString()));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // There is nowhere left to report a failure of the log itself, and the app must not fall
                // over because its diagnostics could not be written (read-only profile, full disk).
                Debug.WriteLine($"FileLoggerProvider: could not write the log: {ex.Message}");
            }
        }
    }

    private void RollIfNeeded()
    {
        var current = new FileInfo(CurrentFile);
        if (!current.Exists || current.Length < _maxBytes) return;

        // markdownblaze.2.log -> .3.log, .1.log -> .2.log, current -> .1.log; whatever would become
        // .{keep+1} is overwritten, which is how the oldest goes.
        for (var n = _keep - 1; n >= 1; n--)
        {
            var from = Path.Combine(_folder, $"{BaseName}.{n.ToString(CultureInfo.InvariantCulture)}.log");
            if (File.Exists(from))
                File.Move(from, Path.Combine(_folder, $"{BaseName}.{(n + 1).ToString(CultureInfo.InvariantCulture)}.log"), overwrite: true);
        }

        File.Move(CurrentFile, Path.Combine(_folder, $"{BaseName}.1.log"), overwrite: true);
    }

    /// <summary>
    /// Writes <paramref name="text"/> so that it cannot be more than the one line it is given. The messages
    /// carry things that came out of documents — the name of a dropped file, a title, an address — and a
    /// name with a line break in it would otherwise start a line of its own, and could look like anything:
    /// a timestamp, a process id, a level and a category followed by whatever the document's author wanted
    /// the reader of the log to believe. So a line break is written as the two characters <c>\n</c> (or
    /// <c>\r</c>), in the message and in the text of an exception alike, here and nowhere else. (Unicode's
    /// other line separators are escaped the same way, since some viewers break lines on them.)
    /// </summary>
    /// <param name="text">The message, or the text of an exception.</param>
    internal static string OneLine(string text)
    {
        var written = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            _ = c switch
            {
                '\r' => written.Append("\\r"),
                '\n' => written.Append("\\n"),
                NextLine or LineSeparator or ParagraphSeparator => written.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)),
                _ => written.Append(c),
            };
        }

        return written.ToString();
    }

    private static string Abbreviation(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        LogLevel.None => "---",
        _ => "???",
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
