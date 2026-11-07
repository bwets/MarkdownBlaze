using System.Diagnostics;

namespace MarkdownBlaze.Services;

/// <summary>
/// One render's thread, as far as <see cref="NavigationService"/> is concerned: whether anyone still
/// wants what it makes, whether it has ended, and how long it has been going. Its token is cancelled when
/// the render that asked for it is superseded and when the run is given up on, which the thread notices
/// at its next look at it — a library call in the middle of a parse does not.
/// </summary>
internal sealed class RenderRun
{
    private readonly CancellationTokenSource _cancellation;
    private readonly long _started = Stopwatch.GetTimestamp();
    private int _unwanted;
    private int _timedOut;
    private int _ended;

    /// <summary>Makes the run of one render.</summary>
    /// <param name="path">The document, for the log.</param>
    /// <param name="parent">The render's own token; the run's is cancelled with it.</param>
    // The token source is never disposed: the thread may still be looking at its token after the caller
    // has stopped waiting, and a disposed source throws when it is asked. Nothing is lost by that — it
    // holds no handle until it is given a timeout, and it is dropped from its parent's list when the
    // parent is cancelled, which is the next render's first act.
    public RenderRun(string? path, CancellationToken parent)
    {
        Path = path;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(parent);
    }

    /// <summary>The document, for the log.</summary>
    public string? Path { get; }

    /// <summary>What the render is to look at to see that nobody wants its result any more.</summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>How long the thread has been going.</summary>
    public TimeSpan Age => Stopwatch.GetElapsedTime(_started);

    /// <summary>True when the run was given up on for taking too long (and not because a newer render replaced it).</summary>
    public bool TimedOut => Volatile.Read(ref _timedOut) != 0;

    /// <summary>True once the thread has ended.</summary>
    public bool HasEnded => Volatile.Read(ref _ended) != 0;

    /// <summary>Whether somebody stopped waiting for the result.</summary>
    public bool IsUnwanted => Volatile.Read(ref _unwanted) != 0;

    /// <summary>
    /// Stops wanting the result and asks the thread to stop at its next look at the token.
    /// </summary>
    /// <param name="timedOut">Whether it is for having taken too long.</param>
    /// <returns>True the first time, false if it had been given up on already.</returns>
    public bool GiveUp(bool timedOut)
    {
        if (timedOut) Volatile.Write(ref _timedOut, 1);
        if (Interlocked.Exchange(ref _unwanted, 1) != 0) return false;

        _cancellation.Cancel();
        return true;
    }

    /// <summary>Notes that the thread has ended, and for how long it ran.</summary>
    /// <param name="elapsed">How long it ran.</param>
    public void End(out TimeSpan elapsed)
    {
        elapsed = Age;
        Volatile.Write(ref _ended, 1);
    }

    /// <summary>
    /// Whether this is a thread that is burning a core for nothing: nobody wants the result, it has not
    /// ended, and it has been going longer than a render is allowed to (or was given up on for that very reason).
    /// A render that was only replaced a moment ago by a newer one is none of that — it ends at its next
    /// look at the token — and does not count, or a burst of reloads would be refused.
    /// </summary>
    /// <param name="deadline">How long a render may take.</param>
    public bool IsStuck(TimeSpan deadline) => IsUnwanted && !HasEnded && (TimedOut || Age > deadline);
}
