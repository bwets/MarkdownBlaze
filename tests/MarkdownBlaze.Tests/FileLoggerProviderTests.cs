using System.Globalization;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Tests;

/// <summary>The rolling log file under the app-data folder (here: a scratch folder).</summary>
public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly TempFolder _logs = new();

    public void Dispose() => _logs.Dispose();

    /// <summary>Logs through the core method, which is what the app's own LoggerMessage code ends in.</summary>
    private static void Write(ILogger log, LogLevel level, string message, Exception? exception = null) =>
        log.Log(level, default, message, exception, static (text, _) => text);

    private string LogFile => Path.Combine(_logs.Path, "markdownblaze.log");

    [Fact]
    public void A_line_carries_the_time_the_process_the_level_the_category_and_the_message()
    {
        using var provider = new FileLoggerProvider(_logs.Path);
        var log = provider.CreateLogger("Some.Category");

        Write(log, LogLevel.Warning, "Could not open the thing");

        var line = Assert.Single(File.ReadAllLines(LogFile));
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} [+-]\d\d:\d\d \[\d+\] WRN Some\.Category: Could not open the thing$", line);
        Assert.Contains("[" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exception_is_written_with_its_stack_trace()
    {
        using var provider = new FileLoggerProvider(_logs.Path);
        var log = provider.CreateLogger("X");

        try
        {
            throw new InvalidOperationException("the cause");
        }
        catch (InvalidOperationException ex)
        {
            Write(log, LogLevel.Error, "It failed", ex);
        }

        var text = File.ReadAllText(LogFile);
        Assert.Contains("ERR X: It failed", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: the cause", text, StringComparison.Ordinal);
        Assert.Contains(nameof(An_exception_is_written_with_its_stack_trace), text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_folder_is_created_on_the_first_line()
    {
        var folder = Path.Combine(_logs.Path, "a", "b");
        using var provider = new FileLoggerProvider(folder);
        Assert.False(Directory.Exists(folder));

        Write(provider.CreateLogger("X"), LogLevel.Information, "hello");

        Assert.True(File.Exists(Path.Combine(folder, "markdownblaze.log")));
    }

    [Fact]
    public void The_file_is_rolled_at_the_size_cap_and_only_a_few_generations_are_kept()
    {
        using var provider = new FileLoggerProvider(_logs.Path, maxBytes: 600, keep: 2);
        var log = provider.CreateLogger("X");

        for (var i = 0; i < 40; i++)
            Write(log, LogLevel.Information, string.Create(CultureInfo.InvariantCulture, $"line number {i} with some padding to give it a size"));

        var files = Directory.GetFiles(_logs.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["markdownblaze.1.log", "markdownblaze.2.log", "markdownblaze.log"], files);
        Assert.True(new FileInfo(LogFile).Length < 1200); // the current file is the newest, and small
        Assert.Contains("line number 39", File.ReadAllText(LogFile), StringComparison.Ordinal);
    }

    [Fact]
    public void Many_threads_can_log_at_once_without_losing_or_mixing_lines()
    {
        using var provider = new FileLoggerProvider(_logs.Path);
        var log = provider.CreateLogger("X");

        _ = Parallel.For(
            0,
            8,
            thread =>
            {
                for (var i = 0; i < 50; i++)
                    Write(log, LogLevel.Information, string.Create(CultureInfo.InvariantCulture, $"thread {thread} line {i}"));
            });

        var lines = File.ReadAllLines(LogFile);
        Assert.Equal(400, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"INF X: thread \d line \d+$", line));
    }

    [Fact]
    public void A_log_that_cannot_be_written_does_not_throw()
    {
        _ = Directory.CreateDirectory(LogFile); // a folder where the file should be
        using var provider = new FileLoggerProvider(_logs.Path);

        Write(provider.CreateLogger("X"), LogLevel.Error, "nowhere to go");

        Assert.True(Directory.Exists(LogFile));
    }

    [Fact]
    public void The_late_bound_logger_of_a_static_class_follows_the_factory_attached_later()
    {
        var early = Log.For("Early"); // made before anything is attached, as a static field is
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(new FileLoggerProvider(_logs.Path)));
        try
        {
            Log.Attach(factory);

            Write(early, LogLevel.Warning, "reached the file");

            Assert.Contains("WRN Early: reached the file", File.ReadAllText(LogFile), StringComparison.Ordinal);
        }
        finally
        {
            Log.Attach(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        }
    }
}
