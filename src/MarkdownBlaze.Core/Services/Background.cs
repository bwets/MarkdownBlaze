using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Services;

/// <summary>
/// Work that is started and not waited for — a re-render asked for by an event, a timer's callback handed
/// to the UI thread. <c>_ = SomethingAsync();</c> throws the task away, and with it any exception it ends
/// in: the failure surfaces much later as an "unobserved task exception", or never, and nothing says which
/// piece of work it was. <see cref="Run"/> and <see cref="Observe"/> keep the task and write a line to the
/// log when it faults.
/// </summary>
internal static partial class Background
{
    private static readonly ILogger Logger = Log.For(nameof(Background));

    /// <summary>
    /// Starts <paramref name="work"/> and logs its failure, if it fails, to <paramref name="log"/>. An exception
    /// thrown before the first await comes out of this call, as it would from any other.
    /// </summary>
    /// <param name="work">What to do.</param>
    /// <param name="log">Where a failure is reported — the logger of the class that asked.</param>
    public static void Run(Func<Task> work, ILogger? log = null) => Observe(work(), log);

    /// <summary>Logs the failure of <paramref name="task"/>, if it fails. A task that is cancelled is not a failure.</summary>
    /// <param name="task">A task nobody is going to await.</param>
    /// <param name="log">Where a failure is reported — the logger of the class that asked.</param>
    public static void Observe(Task task, ILogger? log = null)
    {
        if (task.IsCompletedSuccessfully) return;

        var where = log ?? Logger;
        _ = task.ContinueWith(
            t => LogFaulted(where, t.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Work that nobody was waiting for failed")]
    private static partial void LogFaulted(ILogger log, Exception ex);
}
