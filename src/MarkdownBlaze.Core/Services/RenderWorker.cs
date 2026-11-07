namespace MarkdownBlaze.Services;

/// <summary>
/// Runs a render on a thread of its own, with a stack far larger than the pool's.
/// <para>
/// Rendering parses and walks a document nobody vetted, with libraries (Markdig, AngleSharp) that recurse
/// once per level of nesting. <see cref="Security.NestingLimit"/> and Markdig's own limit keep that recursion
/// shallow, but a stack overflow cannot be caught — the process is gone, and with it the reader's
/// unsaved view of everything else — so the render is also given headroom: 64 MB of address space,
/// reserved and not committed, where a pool thread has 1.5 MB. It is a thread per render rather than a
/// pooled one because the size of a stack is fixed when the thread is made.
/// </para>
/// </summary>
internal static class RenderWorker
{
    /// <summary>The stack reserved for a render.</summary>
    public const int StackBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Starts <paramref name="work"/> on a new thread and gives back a task for its result. The task is
    /// cancelled when <paramref name="cancellationToken"/> is cancelled before the work starts or when the
    /// work throws <see cref="OperationCanceledException"/>, as <see cref="Task.Run(Func{Task}, CancellationToken)"/> would.
    /// A token cancelled while the work runs does not stop the work: it has to look at the token itself,
    /// and the thread goes on until it does (it is a background thread, so it never keeps the app alive).
    /// </summary>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="work">What to run.</param>
    /// <param name="cancellationToken">Whether the result is still wanted.</param>
    public static Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(
            () =>
            {
                try
                {
                    _ = completion.TrySetResult(work());
                }
                catch (OperationCanceledException ex)
                {
                    _ = completion.TrySetCanceled(ex.CancellationToken.IsCancellationRequested ? ex.CancellationToken : cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Decided: everything is handed to the task, which is what a caller awaiting it sees (and
                    // what Task.Run does), written with a filter so that CA1031 accepts it. Nothing may escape
                    // a thread of its own: it would end the process.
                    _ = completion.TrySetException(ex);
                }
            },
            StackBytes)
        {
            IsBackground = true,
            Name = "MarkdownBlaze render",
        };
        thread.Start();
        return completion.Task;
    }
}
