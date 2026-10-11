using System.Runtime.CompilerServices;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>SEC-11: renders run on a thread of their own with a stack of 64 MB, so that recursion in a library has headroom.</summary>
public sealed class RenderWorkerTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Recurse(int levels, ref long carried) => levels == 0 ? 0 : 1 + Recurse(levels - 1, ref carried) + (int)(carried & 0);

    [Fact]
    public async Task The_work_runs_on_a_background_thread_of_its_own_and_not_on_the_pool()
    {
        var (name, isBackground, onPool, id) = await RenderWorker.RunAsync(
            () => (Thread.CurrentThread.Name, Thread.CurrentThread.IsBackground, Thread.CurrentThread.IsThreadPoolThread, Environment.CurrentManagedThreadId),
            CancellationToken.None);

        Assert.Equal("MarkdownBlaze render", name);
        Assert.True(isBackground);
        Assert.False(onPool);
        Assert.NotEqual(Environment.CurrentManagedThreadId, id);
    }

    [Fact]
    public async Task Each_render_gets_a_fresh_thread()
    {
        var first = await RenderWorker.RunAsync(() => Environment.CurrentManagedThreadId, CancellationToken.None);
        var second = await RenderWorker.RunAsync(() => Environment.CurrentManagedThreadId, CancellationToken.None);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task The_thread_has_room_for_a_recursion_far_deeper_than_a_pool_thread_survives()
    {
        // 300,000 frames are some 15-30 MB: on the pool's 1.5 MB that would end the process, so it is only
        // ever asked of the render thread — asking the test's own would crash the run.
        long carried = 0;

        var levels = await RenderWorker.RunAsync(() => Recurse(300_000, ref carried), CancellationToken.None);

        Assert.Equal(300_000, levels);
        Assert.Equal(64 * 1024 * 1024, RenderWorker.StackBytes);
    }

    [Fact]
    public async Task A_result_and_an_exception_are_handed_to_the_task_as_task_run_would()
    {
        Assert.Equal(42, await RenderWorker.RunAsync(() => 42, CancellationToken.None));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RenderWorker.RunAsync<int>(() => throw new InvalidOperationException("boom"), CancellationToken.None));
        Assert.Equal("boom", failure.Message);
    }

    [Fact]
    public async Task A_token_that_is_cancelled_before_the_start_means_the_work_never_runs()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var ran = false;

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RenderWorker.RunAsync(() => ran = true, cancelled.Token));

        Assert.False(ran);
    }

    [Fact]
    public async Task Work_that_throws_operation_canceled_makes_a_cancelled_task()
    {
        using var source = new CancellationTokenSource();
        var task = RenderWorker.RunAsync(
            () =>
            {
                source.Cancel();
                source.Token.ThrowIfCancellationRequested();
                return 0;
            },
            source.Token);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
    }

    [Fact]
    public async Task A_token_cancelled_while_the_work_runs_does_not_stop_the_work_it_has_to_look_for_itself()
    {
        using var source = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        using var go = new ManualResetEventSlim();
        var task = RenderWorker.RunAsync(
            () =>
            {
                started.Set();
                go.Wait(CancellationToken.None);
                return "finished";
            },
            source.Token);
        started.Wait(CancellationToken.None);

        await source.CancelAsync();
        Assert.False(task.IsCompleted); // the thread goes on until it ends by itself
        go.Set();

        Assert.Equal("finished", await task.ConfigureAwait(true));
    }
}
