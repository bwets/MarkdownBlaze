using System.Collections.Concurrent;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>
/// A stand-in for the renderer's thread. The thread that creates it is "the UI thread": work posted from
/// there runs at once (as Blazor's dispatcher does), work posted from any other thread waits in a queue
/// until the test pumps it — so the test decides when the UI thread gets to
/// it, and anything that touches state from the wrong thread is caught by <see cref="CheckAccess"/>.
/// </summary>
internal sealed class QueueDispatcher : IUiDispatcher, IDisposable
{
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);

    /// <summary>How many times something asked whether it was on the UI thread and was not.</summary>
    public int OffThreadChecks { get; private set; }

    /// <summary>Work that has been posted from another thread and not run yet.</summary>
    public int Pending => _queue.Count;

    public bool CheckAccess()
    {
        var onThread = Environment.CurrentManagedThreadId == _uiThread;
        if (!onThread) OffThreadChecks++;
        return onThread;
    }

    public Task InvokeAsync(Action work)
    {
        if (Environment.CurrentManagedThreadId == _uiThread)
        {
            work();
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(() =>
        {
            work();
            done.SetResult();
        });
        return done.Task;
    }

    public Task InvokeAsync(Func<Task> work)
    {
        if (Environment.CurrentManagedThreadId == _uiThread) return work();

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(() => _ = work().ContinueWith(_ => done.SetResult(), TaskScheduler.Default));
        return done.Task;
    }

    /// <summary>Runs queued work on the calling thread (which must be the UI thread) until <paramref name="done"/> is true.</summary>
    /// <exception cref="TimeoutException">The condition was still false after <paramref name="timeoutSeconds"/>.</exception>
    public void Pump(Func<bool> done, int timeoutSeconds = 20)
    {
        Assert.Equal(_uiThread, Environment.CurrentManagedThreadId);
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            while (_queue.TryDequeue(out var work)) work();
            if (done()) return;
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition was not met while pumping the UI queue.");
            _ = _signal.Wait(10);
        }
    }

    /// <summary>Pumps until <paramref name="task"/> has completed.</summary>
    public void Pump(Task task, int timeoutSeconds = 20) => Pump(() => task.IsCompleted, timeoutSeconds);

    /// <summary>Runs whatever is queued right now and returns.</summary>
    public void Drain()
    {
        while (_queue.TryDequeue(out var work)) work();
    }

    public void Dispose() => _signal.Dispose();

    private void Enqueue(Action work)
    {
        _queue.Enqueue(work);
        _ = _signal.Release();
    }
}
