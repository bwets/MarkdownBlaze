using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using MarkdownBlaze.Models;
using MarkdownBlaze.Platform;
using MarkdownBlaze.Security;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Services;

/// <summary>
/// Holds the viewer state: the back/forward stack, the current document (rendered HTML + headings +
/// title), the global history, file-watch reloads and link handling. Raises <see cref="Changed"/>
/// whenever the UI should refresh.
/// <para>
/// The back/forward stack is not shown anywhere — it exists only to drive the two arrows. The list
/// of it used to be a sidebar tab; the global history is the one worth browsing.
/// </para>
/// <para>
/// <b>Threading contract: every member is called on the UI thread, and every event is raised on it.</b>
/// The service is not locked; it is single-threaded by construction. The things that happen elsewhere —
/// the file watcher's timer, the history pruning on a background thread, the render itself on the thread
/// pool — hand their result to the <see cref="IUiDispatcher"/> before they touch any state, and the
/// mutators assert in debug builds that they are where they should be.
/// </para>
/// <para>
/// Rendering is asynchronous. A navigation updates the back/forward stack at once and renders on a thread
/// of its own (<see cref="RenderWorker"/>); the current document (<see cref="CurrentHtml"/> and its
/// companions, and <see cref="RenderToken"/>) changes together, when the render is done. A newer navigation,
/// reload or drop cancels the render that is still running, and its result is never shown.
/// </para>
/// <para>
/// A render that takes longer than <see cref="RenderDeadline"/> is given up on: the page says so, and the
/// result is never shown. The thread cannot be stopped — a library call is not interruptible — so it goes
/// on until it reaches a point where it looks at its cancellation token, or ends. A render that was
/// replaced by a newer one is in the same position, and the same is true of it once it has been going
/// longer than the deadline. At most <see cref="MaxAbandonedRenders"/> such threads are tolerated, and while
/// that many are still going a new document is not started (the page says so) instead of piling another on.
/// </para>
/// </summary>
internal sealed partial class NavigationService : IDisposable
{
    private readonly IDocumentRenderer _md;
    private readonly HistoryStore _history;
    private readonly PreferencesStore _prefs;
    private readonly StartupArguments _startup;
    private readonly FileWatcher _watcher;
    private readonly AssetScope _assets;
    private readonly IShellLauncher _shell;
    private readonly ILogger _log;
    private readonly List<string> _session = [];
    private int _index = -1;
    private IUiDispatcher _dispatcher = ImmediateDispatcher.Instance;

    /// <summary>The render in flight, if any; cancelled by whatever supersedes it.</summary>
    private CancellationTokenSource _renderCts = new();

    /// <summary>True from the start of a render until its result has been applied or discarded.</summary>
    private bool _renderPending;

    /// <summary>The document being shown, or about to be: the one to re-read on a reload. Null for dropped text.</summary>
    private string? _target;

    /// <summary>A navigation whose document has not been put in the history yet (it is recorded with its title, once rendered).</summary>
    private bool _unrecorded;

    public NavigationService(
        IDocumentRenderer md,
        HistoryStore history,
        PreferencesStore prefs,
        StartupArguments startup,
        AssetScope assets,
        IShellLauncher shell,
        ILogger<NavigationService> log)
        : this(md, history, prefs, startup, assets, shell, new FileWatcher(), log)
    { }

    /// <summary>As above, over a file watcher of the caller's choosing — the tests drive it by hand.</summary>
    internal NavigationService(
        IDocumentRenderer md,
        HistoryStore history,
        PreferencesStore prefs,
        StartupArguments startup,
        AssetScope assets,
        IShellLauncher shell,
        FileWatcher watcher,
        ILogger log)
    {
        _md = md;
        _history = history;
        _prefs = prefs;
        _startup = startup;
        _assets = assets;
        _shell = shell;
        _watcher = watcher;
        _log = log;
        _watcher.Changed += OnWatcherChanged;
        _history.Changed += OnHistoryChanged;
    }

    /// <summary>How long a render may take before the page says it is rendering.</summary>
    internal TimeSpan RenderingDelay { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// How long a render may take, in all, before it is given up on. A reader waits a few seconds for a
    /// large document; a render that is still going after half a minute is not a large document but a
    /// pathological one (tens of thousands of headings, links or nested lists parse in quadratic time),
    /// and the window should say so instead of looking frozen.
    /// </summary>
    internal static readonly TimeSpan DefaultRenderDeadline = TimeSpan.FromSeconds(30);

    /// <summary>The deadline in force; the tests shorten it.</summary>
    internal TimeSpan RenderDeadline { get; set; } = DefaultRenderDeadline;

    /// <summary>
    /// How many renders that were given up on may still be running before a new one is refused. Each is a
    /// thread burning a core on a document nobody is waiting for any more.
    /// </summary>
    internal const int MaxAbandonedRenders = 2;

    /// <summary>The renders nobody is waiting for any more whose thread has not ended yet (see <see cref="RenderRun.IsStuck"/> for which of them count).</summary>
    private readonly ConcurrentDictionary<RenderRun, byte> _unwanted = new();

    /// <summary>
    /// How many renders have been given up on and are still running: the ones that took longer than the
    /// deadline, and the ones that were replaced and have outlasted it since. The tests wait for it to fall.
    /// </summary>
    internal int AbandonedRenders => _unwanted.Keys.Count(run => run.IsStuck(RenderDeadline));

    /// <summary>
    /// How long to wait before trying again to read a document that has vanished or cannot be read: an
    /// editor that saves by deleting the file and renaming another into its place leaves a gap of a few
    /// milliseconds in which there is no file.
    /// </summary>
    internal TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    public string? CurrentPath { get; private set; }
    public string CurrentTitle { get; private set; } = "MarkdownBlaze";
    public string CurrentHtml { get; private set; } = "";
    public IReadOnlyList<Heading> CurrentHeadings { get; private set; } = [];

    /// <summary>The open document's language tag, or null to leave it at the app's own (English).</summary>
    public string? CurrentLanguage { get; private set; }

    /// <summary>Changes every time the page's document is replaced; the viewer keys the document element on it.</summary>
    public int RenderToken { get; private set; }

    /// <summary>True when a render has been running long enough (<see cref="RenderingDelay"/>) for the page to say so.</summary>
    public bool IsRendering { get; private set; }

    /// <summary>
    /// True when the file on screen has been deleted: what is shown is the last good render of it, kept
    /// (greyed title) until the file comes back or the reader goes elsewhere.
    /// </summary>
    public bool CurrentFileMissing { get; private set; }
    public bool CanBack => _index > 0;
    public bool CanForward => _index >= 0 && _index < _session.Count - 1;
    public IReadOnlyList<HistoryEntry> GlobalHistory => _history.Entries;

    /// <summary>Removes a single file from the persisted global history.</summary>
    public void RemoveFromHistory(string path) => _history.Remove(path);

    /// <summary>
    /// Empties the persisted global history. Settings asks for this here rather than reaching for the
    /// history store itself, so that the viewer and Settings change the history through the same door.
    /// </summary>
    public void ClearHistory() => _history.Clear();

    public event EventHandler? Changed;

    /// <summary>Raised when the document on the page is a different one than before (not for a reload of the same file).</summary>
    public event EventHandler? DocumentOpened;

    /// <summary>Raised when a navigation was asked for a file that does not exist (a stale history row, a deleted file).</summary>
    public event EventHandler<MissingFileEventArgs>? FileMissing;

    /// <summary>
    /// Makes <paramref name="dispatcher"/> the way back to the UI thread. The viewer attaches itself as
    /// soon as it exists; until then everything runs where it is called.
    /// </summary>
    public void Attach(IUiDispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>
    /// Opens whatever the app was started with, once, when the viewer is created. The viewer lives as long
    /// as the window does (Settings is drawn over it, not instead of it), so this is called exactly once.
    /// </summary>
    public async Task InitializeAsync()
    {
        AssertUiThread();

        // Clean stale (deleted/moved) files out of the global history off the startup path.
        _history.PruneMissingInBackground();

        var path = _md.GetStartupFilePath(_startup.Values);
        if (path is null)
        {
            RaiseChanged();
            return;
        }

        await NavigateAsync(path).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a document and makes it the current entry of the back/forward stack. The task completes when
    /// it has been rendered and shown, or has been superseded.
    /// </summary>
    /// <param name="path">The file to open.</param>
    /// <param name="anyType">
    /// Open it whatever it is called. Without this only the supported document types are opened: a link or
    /// a drop that names anything else is not followed. A file the reader picked in the file dialog's
    /// "All files" filter — and so also the history row for one — is the exception.
    /// </param>
    public Task NavigateAsync(string path, bool anyType = false)
    {
        AssertUiThread();
        if (string.IsNullOrWhiteSpace(path)) return Task.CompletedTask;

        if (!File.Exists(path))
        {
            // A stale history row, a link to a file that has since been moved. Silence is the one answer
            // that tells the reader nothing, so the viewer is told.
            LogMissing(_log, path);
            FileMissing?.Invoke(this, new MissingFileEventArgs(Path.GetFullPath(path)));
            return Task.CompletedTask;
        }

        if (!anyType && !MarkdownService.IsSupported(path))
        {
            LogNotSupported(_log, path);
            return Task.CompletedTask;
        }

        var full = Path.GetFullPath(path);

        if (_index < _session.Count - 1)
            _session.RemoveRange(_index + 1, _session.Count - _index - 1);

        if (_session.Count == 0 || !PathEquals(_session[^1], full))
            _session.Add(full);

        _index = _session.Count - 1;
        return ShowEntryAsync();
    }

    /// <summary>
    /// Renders Markdown dropped into the window that has no resolvable file path (WebView2 does not
    /// expose one). <see cref="CurrentPath"/> stays null, so it is not file-watched and "open
    /// containing folder" is inert; relative images/links cannot be resolved without a base folder.
    /// </summary>
    public Task OpenContentAsync(string fileName, string markdown)
    {
        AssertUiThread();
        return ShowAsync(path: null, _ => new FileRender(RenderStatus.Rendered, _md.RenderText(fileName, markdown)), keepLastGood: false);
    }

    public Task BackAsync()
    {
        AssertUiThread();
        if (!CanBack) return Task.CompletedTask;
        _index--;
        return ShowEntryAsync();
    }

    public Task ForwardAsync()
    {
        AssertUiThread();
        if (!CanForward) return Task.CompletedTask;
        _index++;
        return ShowEntryAsync();
    }

    /// <summary>
    /// Reads the open document again. If the file cannot be read at that moment — it was deleted and not
    /// yet replaced, or the editor still has it locked — the render on screen stays, and it is tried once
    /// more shortly; only if that fails too does the page change.
    /// </summary>
    public Task ReloadAsync()
    {
        AssertUiThread();
        if (_target is not { } path) return Task.CompletedTask;
        return ShowAsync(path, ct => _md.RenderFile(path, ct), keepLastGood: PathEquals(CurrentPath, path));
    }

    private Task ShowEntryAsync()
    {
        var path = _session[_index];
        _unrecorded = true;
        return ShowAsync(path, ct => _md.RenderFile(path, ct), keepLastGood: false);
    }

    /// <summary>Starts a render of <paramref name="path"/>, replacing any that is still running.</summary>
    private async Task ShowAsync(string? path, Func<CancellationToken, FileRender> render, bool keepLastGood)
    {
        if (AbandonedRenders >= MaxAbandonedRenders)
        {
            await RefuseAsync(path, keepLastGood).ConfigureAwait(false);
            return;
        }

        var ct = BeginRender();
        _target = path;
        if (path is not null) _watcher.Watch(path);
        Background.Observe(ShowRenderingAfterDelayAsync(ct), _log);

        try
        {
            var outcome = await RenderWithinDeadlineAsync(path, render, ct).ConfigureAwait(false);
            if (outcome is not null && keepLastGood && outcome.Status is RenderStatus.Missing or RenderStatus.Unreadable)
            {
                LogRetrying(_log, path);
                await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
                outcome = await RenderWithinDeadlineAsync(path, render, ct).ConfigureAwait(false);
            }

            // Null is a render that took too long. The page says so; its result will never be shown.
            var shown = outcome ?? new FileRender(RenderStatus.TimedOut);
            await _dispatcher.InvokeAsync(() => Apply(path, shown, keepLastGood, ct)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer navigation, reload or drop: its result is not wanted, and the page
            // already belongs to whatever replaced this render.
            LogSuperseded(_log, path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Decided: this is the one catch-all that is kept, written with a filter so that CA1031 accepts
            // it. Rendering parses and sanitises a document nobody vetted, with libraries whose failures
            // are not all documented, and a list of their exception types would be a guess. Whatever goes
            // wrong must not leave the page saying "Rendering…" for ever, or surface only as an
            // unobserved task: the reader gets the sentence, the log gets the cause. Cancellation is
            // excluded by the filter, because that is the normal way for a render to end.
            await _dispatcher.InvokeAsync(() => Apply(path, new FileRender(StatusOf(ex, path)), keepLastGood: false, ct)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Two threads are still grinding through documents nobody is waiting for, and a third would be
    /// another core gone, so nothing is started. A reload of what is on screen simply leaves it there; a
    /// navigation replaces the page, because the back/forward stack has already moved on to it.
    /// </summary>
    private async Task RefuseAsync(string? path, bool keepLastGood)
    {
        LogBusy(_log, path);
        if (keepLastGood) return;

        var ct = BeginRender();
        _target = path;
        if (path is not null) _watcher.Watch(path);
        await _dispatcher.InvokeAsync(() => Apply(path, new FileRender(RenderStatus.Busy), keepLastGood: false, ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// What a render that threw is reported as. One failure is a fact about the document and not a fault:
    /// Markdig refusing it for being nested too deeply. It gets its own sentence and no stack trace in the log.
    /// </summary>
    private RenderStatus StatusOf(Exception ex, string? path)
    {
        if (MarkdownService.IsNestingLimit(ex))
        {
            LogTooDeep(_log, path);
            return RenderStatus.TooDeep;
        }

        LogRenderFailed(_log, ex, path);
        return RenderStatus.Unreadable;
    }

    /// <summary>
    /// Runs one render on a thread of its own and waits for it for at most <see cref="RenderDeadline"/>.
    /// Returns its outcome, or null when the deadline came first — and then the render is abandoned: its
    /// thread is asked to stop at its next look at the token and counted until it ends, and whatever it
    /// produces is dropped. The same happens to a render that is waited for no longer because a newer
    /// one has taken its place (it is counted once it has outlasted the deadline as well). A render that
    /// failed throws what it threw.
    /// </summary>
    private async Task<FileRender?> RenderWithinDeadlineAsync(string? path, Func<CancellationToken, FileRender> render, CancellationToken ct)
    {
        var run = new RenderRun(path, ct);
        var work = RenderWorker.RunAsync(() => render(run.Token), run.Token);
        _ = work.ContinueWith(_ => OnRenderEnded(run), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        using var stopWaiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var deadline = Task.Delay(RenderDeadline, stopWaiting.Token);
        var first = await Task.WhenAny(work, deadline).ConfigureAwait(false);
        await stopWaiting.CancelAsync().ConfigureAwait(false); // the timer is not needed any more

        if (ct.IsCancellationRequested)
        {
            Abandon(run, timedOut: false);
            ct.ThrowIfCancellationRequested();
        }

        if (ReferenceEquals(first, work)) return await work.ConfigureAwait(false);

        LogTimedOut(_log, path, RenderDeadline.TotalSeconds);
        Abandon(run, timedOut: true);
        return null;
    }

    /// <summary>Stops waiting for a run: it is asked to stop, and counted for as long as its thread goes on.</summary>
    private void Abandon(RenderRun run, bool timedOut)
    {
        if (!run.GiveUp(timedOut)) return;

        _unwanted[run] = 0;
        if (run.HasEnded) _ = _unwanted.TryRemove(run, out _); // it ended between the two lines above
    }

    /// <summary>The thread of a render has ended, one way or another.</summary>
    private void OnRenderEnded(RenderRun run)
    {
        run.End(out var elapsed);
        if (!_unwanted.TryRemove(run, out _)) return;

        // Only a render that had been going on for too long is worth a line: the others were replaced and
        // stopped at their next look at the token.
        if (run.TimedOut || elapsed > RenderDeadline) LogAbandonedEnded(_log, run.Path, elapsed.TotalSeconds);
    }

    /// <summary>Marks a render as started and cancels the one before it.</summary>
    private CancellationToken BeginRender()
    {
        _renderCts.Cancel();
        _renderCts = new CancellationTokenSource();
        _renderPending = true;
        IsRendering = false;
        return _renderCts.Token;
    }

    /// <summary>After <see cref="RenderingDelay"/>, if the render is still running, tells the page so.</summary>
    private async Task ShowRenderingAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(RenderingDelay, ct).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested || !_renderPending || IsRendering) return;
                IsRendering = true;
                RaiseChanged();
            })
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The render was superseded before the delay was up. A render that simply finished first
            // does not cancel anything; Apply clears _renderPending, which is what the check above reads.
        }
    }

    /// <summary>Puts a finished render on the page. Runs on the UI thread.</summary>
    private void Apply(string? path, FileRender outcome, bool keepLastGood, CancellationToken ct)
    {
        AssertUiThread();
        if (ct.IsCancellationRequested) return; // a newer render owns the page now

        _renderPending = false;
        IsRendering = false;

        if (outcome.Status == RenderStatus.Missing && keepLastGood)
        {
            // Deleted, and still gone after the second look: the page keeps showing what it had, marked as
            // such, rather than being replaced by an error.
            CurrentFileMissing = true;
            RaiseChanged();
            return;
        }

        var previous = CurrentPath;
        CurrentFileMissing = false;

        switch (outcome.Status)
        {
            case RenderStatus.Rendered:
                Show(path, outcome.Result!);
                break;
            case RenderStatus.TooLarge:
                ShowMessage(
                    path,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"This file is too large to open ({outcome.Length / 1024.0 / 1024.0:0.#} MB; the limit is {MarkdownService.MaxFileBytes / 1024 / 1024} MB)."));
                break;
            case RenderStatus.TooDeep:
                ShowMessage(path, "This document is nested too deeply to display.");
                break;
            case RenderStatus.TimedOut:
                ShowMessage(
                    path,
                    string.Create(CultureInfo.InvariantCulture, $"This document could not be rendered within {RenderDeadline.TotalSeconds:0.#} seconds."),
                    detail: path is null ? null : Path.GetFileName(path));
                break;
            case RenderStatus.Busy:
                ShowMessage(path, "Still rendering an earlier document. Try again in a moment.", detail: path is null ? null : Path.GetFileName(path));
                break;
            default:
                ShowMessage(path, "Unable to open this file.");
                break;
        }

        if (path is not null && _unrecorded)
        {
            _history.Record(path, CurrentTitle);
            _unrecorded = false;
        }

        var changed = !string.Equals(previous, CurrentPath, StringComparison.OrdinalIgnoreCase);
        if (changed && CurrentPath is not null) CountDocumentForRating(CurrentPath);

        RaiseChanged();
        if (changed) DocumentOpened?.Invoke(this, EventArgs.Empty);
    }

    private void Show(string? path, RenderResult result)
    {
        CurrentPath = path;
        CurrentTitle = result.Title;
        CurrentHtml = result.BodyHtml;
        CurrentHeadings = result.Headings;
        CurrentLanguage = result.Language;

        // The images of the render now on the page may be fetched, and only those: the grant of the
        // document it replaces stops working at this line. A render with no images, and dropped text
        // (which has no folder, so it has nothing but rooted paths to offer), come with their own grant
        // or with none — the previous one is not carried over.
        _assets.Set(result.Assets);
        RenderToken++;
    }

    /// <summary>Shows a sentence in place of a document that could not be shown.</summary>
    /// <param name="path">The document, if it has a path.</param>
    /// <param name="message">A fixed sentence, which may carry numbers.</param>
    /// <param name="detail">The name of the file, when the sentence is about one; it is text and is encoded.</param>
    private void ShowMessage(string? path, string message, string? detail = null)
    {
        CurrentPath = path;
        CurrentTitle = path is null ? "MarkdownBlaze" : Path.GetFileNameWithoutExtension(path);
        // Built here from a fixed sentence and numbers; a file name is the one thing from outside, and a
        // file name can hold anything the file system allows (on Linux, "<" and "&"), so it is encoded.
        CurrentHtml = detail is null
            ? $"<p><em>{message}</em></p>"
            : $"<p><em>{message}</em></p><p><code>{WebUtility.HtmlEncode(detail)}</code></p>";
        CurrentHeadings = [];
        CurrentLanguage = null;
        _assets.Clear(); // a sentence has no pictures
        RenderToken++;
    }

    /// <summary>
    /// Documents are counted until the rating request is due, and not at all once it has been answered.
    /// The bundled manual does not count: reading it is not yet using the app. This is a navigation
    /// event, not a render of the page — the viewer shows the request when DocumentOpened says it is due.
    /// </summary>
    private void CountDocumentForRating(string path)
    {
        if (!RatingPrompt.IsPending(_prefs.Current) || MarkdownService.IsUserManual(path)) return;

        _prefs.Current.DocumentsOpened++;
        _prefs.Save();
    }

    private void RaiseChanged()
    {
        AssertUiThread();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>History pruned in the background (missing files removed) → refresh the sidebar, on the UI thread.</summary>
    private void OnHistoryChanged(object? sender, EventArgs e) => Post(RaiseChanged);

    /// <summary>The watcher's timer thread says the file changed: the reload happens on the UI thread.</summary>
    private void OnWatcherChanged(object? sender, EventArgs e) => Post(() => Background.Observe(ReloadAsync(), _log));

    /// <summary>
    /// The page reports what was clicked, and the page shows an untrusted document — so what it reports
    /// is a request, checked here rather than believed.
    /// </summary>
    public void HandleLink(string kind, string value) => Post(() =>
    {
        switch (kind)
        {
            case "nav":
                if (IsFollowable(value, CurrentPath, fromLink: true)) Background.Observe(NavigateAsync(value), _log);
                break;
            case "ext":
                OpenExternal(value);
                break;
        }
    });

    /// <summary>A file was dropped onto the window: "path" opens it if it is a document that exists; "text" renders the contents.</summary>
    public void HandleFileDrop(string kind, string a, string b) => Post(() =>
    {
        switch (kind)
        {
            case "path":
                // engine gave a real path → open like any other file
                if (IsFollowable(a, CurrentPath, fromLink: false)) Background.Observe(NavigateAsync(a), _log);
                break;
            case "text":
                // only the file's contents are available
                Background.Observe(OpenContentAsync(a, b), _log);
                break;
        }
    });

    /// <summary>Runs <paramref name="work"/> on the UI thread, whichever thread the notification came in on.</summary>
    private void Post(Action work) => Background.Observe(_dispatcher.InvokeAsync(work), _log);

    [Conditional("DEBUG")]
    private void AssertUiThread() =>
        Debug.Assert(_dispatcher.CheckAccess(), "NavigationService is single-threaded: this member was called off the UI thread.");

    /// <summary>
    /// Whether a path reported by the page may be opened: a supported document type that exists.
    /// Links get one more rule — a UNC path is followed only on the server the current document came
    /// from, so that a link written by the author cannot make Windows connect to a server of their
    /// choosing (and hand it the reader's credentials) just by being clicked.
    /// </summary>
    internal static bool IsFollowable(string? path, string? currentDocument, bool fromLink)
    {
        if (string.IsNullOrWhiteSpace(path) || !MarkdownService.IsSupported(path)) return false;

        if (fromLink)
        {
            // Decided: a document opened from a network share links to its neighbours with UNC paths, so
            // UNC cannot be refused outright; the same server as the current document is the narrowest
            // rule that keeps those links working and still stops a link from naming another server.
            if (LocalPath.IsUncOrDevice(path))
            {
                var server = LocalPath.UncServer(path);
                if (server is null
                    || !string.Equals(server, LocalPath.UncServer(currentDocument), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (!LocalPath.IsSafe(path))
            {
                return false;
            }
        }

        return File.Exists(path);
    }

    // ---- external actions --------------------------------------------------------------------------

    /// <summary>
    /// Opens a link in the OS default handler — if it is one of the schemes in
    /// <see cref="ExternalLinks"/>. Anything else is ignored, and never reaches the shell launcher.
    /// </summary>
    /// <param name="url">The address as the page reported it.</param>
    public void OpenExternal(string url)
    {
        if (!ExternalLinks.TryGetOpenable(url, out var uri))
        {
            LogRefusedLink(_log, url);
            return;
        }

        _ = _shell.OpenExternal(uri);
    }

    private static bool PathEquals(string? a, string? b) =>
        a is not null
            && b is not null
            && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _history.Changed -= OnHistoryChanged;
        _watcher.Changed -= OnWatcherChanged;
        _renderCts.Cancel();
        _renderCts.Dispose();
        _watcher.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Path} does not exist; nothing to open")]
    private static partial void LogMissing(ILogger log, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Path} is not a supported document type; not opened")]
    private static partial void LogNotSupported(ILogger log, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Path} could not be read; trying once more shortly")]
    private static partial void LogRetrying(ILogger log, string? path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The render of {Path} was superseded")]
    private static partial void LogSuperseded(ILogger log, string? path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rendering {Path} failed")]
    private static partial void LogRenderFailed(ILogger log, Exception ex, string? path);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Path} is nested too deeply to be rendered")]
    private static partial void LogTooDeep(ILogger log, string? path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rendering {Path} took longer than {Seconds} s; it is given up on and its result will not be shown")]
    private static partial void LogTimedOut(ILogger log, string? path, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The render of {Path} that was given up on has ended, {Seconds} s after it started")]
    private static partial void LogAbandonedEnded(ILogger log, string? path, double seconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Not rendering {Path}: renders that were given up on are still running")]
    private static partial void LogBusy(ILogger log, string? path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Refused to open {Url}: its scheme is not on the allow-list")]
    private static partial void LogRefusedLink(ILogger log, string url);
}
