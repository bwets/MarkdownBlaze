namespace MarkdownBlaze.Services;

/// <summary>
/// The way back to the thread that owns the UI. File-system notifications, timers and the thread pool
/// all call into the services from other threads; the services' state may only be touched on the UI
/// thread, so anything that arrives from elsewhere is handed to this first. <c>ViewerView</c> implements
/// it with the component's own <c>InvokeAsync</c>, which is the Blazor renderer's dispatcher; the tests
/// implement it with a queue they empty themselves.
/// </summary>
internal interface IUiDispatcher
{
    /// <summary>True when the calling thread is the UI thread.</summary>
    bool CheckAccess();

    /// <summary>
    /// Runs <paramref name="work"/> on the UI thread — at once, if that is where the caller already is.
    /// The task completes when it has run.
    /// </summary>
    Task InvokeAsync(Action work);

    /// <summary>As <see cref="InvokeAsync(Action)"/>, for work that awaits.</summary>
    Task InvokeAsync(Func<Task> work);
}
