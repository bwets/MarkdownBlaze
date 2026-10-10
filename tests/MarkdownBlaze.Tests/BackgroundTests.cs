using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Tests;

/// <summary>Work nobody awaits must not lose its failure.</summary>
public class BackgroundTests
{
    [Fact]
    public async Task A_failure_is_logged_with_its_exception()
    {
        var log = new TestLogger();
        var boom = new InvalidOperationException("boom");

        Background.Run(
            async () =>
            {
                await Task.Yield();
                throw boom;
            },
            log);

        await WaitForAsync(() => log.Entries.Count > 0).ConfigureAwait(true);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(boom, entry.Exception);
    }

    [Fact]
    public async Task The_failure_of_one_of_several_awaited_tasks_is_the_one_that_is_logged()
    {
        var log = new TestLogger();
        var task = Task.WhenAll(Task.CompletedTask, Task.FromException(new IOException("disk")));

        Background.Observe(task, log);

        await WaitForAsync(() => log.Entries.Count > 0).ConfigureAwait(true);
        _ = Assert.IsType<IOException>(Assert.Single(log.Entries).Exception);
    }

    [Fact]
    public async Task Work_that_succeeds_or_is_cancelled_logs_nothing()
    {
        var log = new TestLogger();

        Background.Run(() => Task.CompletedTask, log);
        Background.Observe(Task.FromCanceled(new CancellationToken(canceled: true)), log);
        Background.Observe(Task.Run(() => { }), log);
        await Task.Delay(200).ConfigureAwait(true);

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void An_exception_before_the_first_await_comes_out_of_the_call_as_it_would_from_any_other()
    {
        var log = new TestLogger();

        _ = Assert.Throws<InvalidOperationException>(() => Background.Run(() => throw new InvalidOperationException("at once"), log));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the failure was never logged");
            await Task.Delay(10).ConfigureAwait(true);
        }
    }
}
