namespace MarkdownBlaze.Services;

/// <summary>
/// The dispatcher used until a UI one is attached: everything runs where it is called. Before the
/// viewer exists nothing but the thread that built the container is running.
/// </summary>
internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public static ImmediateDispatcher Instance { get; } = new();

    public bool CheckAccess() => true;

    public Task InvokeAsync(Action work)
    {
        work();
        return Task.CompletedTask;
    }

    public Task InvokeAsync(Func<Task> work) => work();
}
