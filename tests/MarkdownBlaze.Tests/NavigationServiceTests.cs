using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarkdownBlaze.Tests;

/// <summary>
/// The back/forward stack, reloads, missing files, and the rule that the service's state is only ever
/// touched on the UI thread (here: the thread of the test, with a queue standing in for the renderer).
/// </summary>
public sealed class NavigationServiceTests : IDisposable
{
    /// <summary>
    /// Why the tests that lock a file with FileShare.None are skipped off Windows: Windows enforces that against
    /// every other reader, but a Unix lock is only advisory, so a reader that does not look for it opens the
    /// file regardless and the test would observe a file that is not locked at all.
    /// </summary>
    private const string LockNeedsWindows = "Windows enforces FileShare.None as a mandatory lock; Unix file locks are advisory, so the file would not look locked";

    private readonly TempFolder _data = new();
    private readonly TempFolder _docs = new();
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var item in _disposables) item.Dispose();
        _data.Dispose();
        _docs.Dispose();
    }

    // ---- back and forward ---------------------------------------------------------------------------------

    [Fact]
    public void Navigating_builds_the_back_and_forward_stack()
    {
        var sut = Create();
        var (a, b, c) = (Doc("a.md"), Doc("b.md"), Doc("c.md"));

        Go(sut, a);
        Assert.False(sut.Nav.CanBack);
        Go(sut, b);
        Go(sut, c);
        Assert.True(sut.Nav.CanBack);
        Assert.False(sut.Nav.CanForward);
        Assert.Equal(c, sut.Nav.CurrentPath, ignoreCase: true);

        sut.Run(sut.Nav.BackAsync());
        Assert.Equal(b, sut.Nav.CurrentPath, ignoreCase: true);
        Assert.True(sut.Nav.CanBack);
        Assert.True(sut.Nav.CanForward);

        sut.Run(sut.Nav.BackAsync());
        Assert.Equal(a, sut.Nav.CurrentPath, ignoreCase: true);
        Assert.False(sut.Nav.CanBack);

        sut.Run(sut.Nav.BackAsync()); // nothing before the first: stays put
        Assert.Equal(a, sut.Nav.CurrentPath, ignoreCase: true);

        sut.Run(sut.Nav.ForwardAsync());
        Assert.Equal(b, sut.Nav.CurrentPath, ignoreCase: true);
        Assert.True(sut.Nav.CanForward);
    }

    [Fact]
    public void Navigating_after_going_back_drops_what_was_forward()
    {
        var sut = Create();
        var (a, b, c, d) = (Doc("a.md"), Doc("b.md"), Doc("c.md"), Doc("d.md"));
        Go(sut, a);
        Go(sut, b);
        Go(sut, c);
        sut.Run(sut.Nav.BackAsync()); // at b, with c ahead

        Go(sut, d);

        Assert.False(sut.Nav.CanForward); // c is gone
        sut.Run(sut.Nav.BackAsync());
        Assert.Equal(b, sut.Nav.CurrentPath, ignoreCase: true);
        sut.Run(sut.Nav.BackAsync());
        Assert.Equal(a, sut.Nav.CurrentPath, ignoreCase: true);
        Assert.False(sut.Nav.CanBack);
    }

    [Fact]
    public void Opening_the_document_that_is_already_open_adds_nothing_to_the_stack()
    {
        var sut = Create();
        var (a, b) = (Doc("a.md"), Doc("b.md"));
        Go(sut, a);
        Go(sut, b);

        Go(sut, b);

        sut.Run(sut.Nav.BackAsync());
        Assert.Equal(a, sut.Nav.CurrentPath, ignoreCase: true);
        Assert.False(sut.Nav.CanBack); // two entries, not three
    }

    [Fact]
    public void A_document_is_recorded_in_the_history_once_it_is_shown_with_its_title()
    {
        var sut = Create();
        Go(sut, Doc("a.md", "# The title of A"));

        var entry = Assert.Single(sut.Nav.GlobalHistory);
        Assert.Equal("The title of A", entry.Title);
    }

    // ---- reload -------------------------------------------------------------------------------------------

    [Fact]
    public void Reloading_reads_the_file_again_and_keeps_the_place_in_the_stack()
    {
        var sut = Create();
        var (a, b) = (Doc("a.md", "# A"), Doc("b.md", "# B one"));
        Go(sut, a);
        Go(sut, b);
        var token = sut.Nav.RenderToken;
        var opened = 0;
        sut.Nav.DocumentOpened += (_, _) => opened++;
        File.WriteAllText(b, "# B two");

        sut.Run(sut.Nav.ReloadAsync());

        Assert.Equal("B two", sut.Nav.CurrentTitle);
        Assert.NotEqual(token, sut.Nav.RenderToken); // the page replaces its document
        Assert.Equal(b, sut.Nav.CurrentPath, ignoreCase: true);
        Assert.True(sut.Nav.CanBack); // index and stack are as they were
        Assert.False(sut.Nav.CanForward);
        Assert.Equal(0, opened); // the same document, not a newly opened one
        Assert.Equal(2, sut.History.Entries.Count);
    }

    [Fact]
    public void A_reload_with_nothing_open_does_nothing()
    {
        var sut = Create();
        var changes = 0;
        sut.Nav.Changed += (_, _) => changes++;

        sut.Run(sut.Nav.ReloadAsync());

        Assert.Equal(0, changes);
        Assert.Null(sut.Nav.CurrentPath);
    }

    // ---- threading ----------------------------------------------------------------------------------------

    [Fact]
    public void A_change_reported_by_the_file_watcher_on_another_thread_is_handled_on_the_ui_thread()
    {
        var sut = Create();
        var a = Doc("a.md", "# One");
        Go(sut, a);
        File.WriteAllText(a, "# Two");
        var changedOn = new List<int>();
        sut.Nav.Changed += (_, _) => changedOn.Add(Environment.CurrentManagedThreadId);

        OnAnotherThread(sut.Watcher.RaiseChanged); // the watcher's timer thread

        Assert.Equal("One", sut.Nav.CurrentTitle); // nothing has happened yet: the work waits for the UI thread
        Assert.True(sut.Dispatcher.Pending > 0);

        sut.Dispatcher.Pump(() => string.Equals(sut.Nav.CurrentTitle, "Two", StringComparison.Ordinal));

        Assert.Equal(0, sut.Dispatcher.OffThreadChecks); // every mutator ran where it should
        Assert.All(changedOn, id => Assert.Equal(Environment.CurrentManagedThreadId, id));
    }

    [Fact]
    public void Watcher_changes_arriving_while_a_navigation_is_in_flight_do_not_corrupt_the_stack()
    {
        var sut = Create();
        var (a, b, c) = (Doc("a.md", "# A"), Doc("b.md", "# B"), Doc("c.md", "# C"));
        Go(sut, a);

        var navigating = sut.Nav.NavigateAsync(b); // renders on the thread pool; its result waits for the pump
        OnAnotherThread(() =>
        {
            for (var i = 0; i < 50; i++) sut.Watcher.RaiseChanged();
        });
        // Each reload supersedes the render before it, navigation included, and re-renders the same target:
        // so what is waited for is the page, not the task of the render that was cancelled.
        sut.Dispatcher.Pump(() => navigating.IsCompleted && sut.Nav.CurrentPath is not null && PathIs(sut.Nav.CurrentPath, b));
        // and again, now while the second navigation runs
        var second = sut.Nav.NavigateAsync(c);
        OnAnotherThread(() =>
        {
            for (var i = 0; i < 50; i++) sut.Watcher.RaiseChanged();
        });
        sut.Dispatcher.Pump(() => second.IsCompleted && PathIs(sut.Nav.CurrentPath, c));
        sut.Dispatcher.Drain();

        Assert.Equal(c, sut.Nav.CurrentPath, ignoreCase: true);
        Assert.Equal("C", sut.Nav.CurrentTitle);
        Assert.True(sut.Nav.CanBack);
        Assert.False(sut.Nav.CanForward);
        sut.Run(sut.Nav.BackAsync());
        Assert.Equal(b, sut.Nav.CurrentPath, ignoreCase: true);
        sut.Run(sut.Nav.BackAsync());
        Assert.Equal(a, sut.Nav.CurrentPath, ignoreCase: true);
        Assert.False(sut.Nav.CanBack);
        Assert.Equal(0, sut.Dispatcher.OffThreadChecks);
    }

    [Fact]
    public void A_navigation_cancels_the_render_before_it_and_only_the_last_is_shown()
    {
        var sut = Create();
        var (a, b) = (Doc("a.md", "# A"), Doc("b.md", "# B"));

        var first = sut.Nav.NavigateAsync(a);
        var second = sut.Nav.NavigateAsync(b);
        sut.Dispatcher.Pump(() => first.IsCompleted && second.IsCompleted);

        Assert.Equal(b, sut.Nav.CurrentPath, ignoreCase: true);
        Assert.Equal("B", sut.Nav.CurrentTitle);
        _ = Assert.Single(sut.History.Entries); // a was never shown, so it was never recorded
    }

    [Fact]
    public void A_history_change_on_a_background_thread_is_announced_on_the_ui_thread()
    {
        var sut = Create();
        var gone = Doc("gone.md");
        sut.History.Record(gone, "Gone");
        File.Delete(gone);
        var changedOn = new List<int>();
        sut.Nav.Changed += (_, _) => changedOn.Add(Environment.CurrentManagedThreadId);

        sut.History.PruneMissingInBackground();
        sut.Dispatcher.Pump(() => changedOn.Count > 0);

        Assert.All(changedOn, id => Assert.Equal(Environment.CurrentManagedThreadId, id));
        Assert.Empty(sut.Nav.GlobalHistory);
    }

    // ---- files that are not there, and files that are not documents ---------------------------------------

    [Fact]
    public void Asking_for_a_file_that_does_not_exist_changes_nothing_and_says_so()
    {
        var sut = Create();
        var a = Doc("a.md");
        Go(sut, a);
        var missing = Path.Combine(_docs.Path, "gone.md");
        var reported = new List<string>();
        sut.Nav.FileMissing += (_, e) => reported.Add(e.Path);

        sut.Run(sut.Nav.NavigateAsync(missing));

        Assert.Equal([missing], reported, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(a, sut.Nav.CurrentPath, ignoreCase: true);
        Assert.False(sut.Nav.CanBack);
    }

    [Fact]
    public void Only_supported_document_types_are_opened_unless_the_reader_chose_the_file()
    {
        var sut = Create();
        var a = Doc("a.md");
        var log = Doc("server.log", "plain text, not Markdown");
        Go(sut, a);

        sut.Run(sut.Nav.NavigateAsync(log));
        Assert.Equal(a, sut.Nav.CurrentPath, ignoreCase: true); // refused

        sut.Run(sut.Nav.NavigateAsync(log, anyType: true)); // picked in the file dialog's "All files"
        Assert.Equal(log, sut.Nav.CurrentPath, ignoreCase: true);
    }

    [Fact]
    public void A_file_over_the_size_cap_shows_a_message_instead_of_freezing_the_window()
    {
        var sut = Create();
        var huge = Path.Combine(_docs.Path, "huge.md");
        using (var stream = File.Create(huge))
            stream.SetLength(MarkdownService.MaxFileBytes + 1);

        Go(sut, huge);

        Assert.Contains("too large", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.Contains("32 MB", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.Equal("huge", sut.Nav.CurrentTitle);
    }

    // ---- editors that save by replacing the file -----------------------------------------------------------

    [Fact]
    public void A_save_that_deletes_and_renames_never_shows_an_error_and_ends_on_the_new_content()
    {
        var sut = Create();
        sut.Nav.RetryDelay = TimeSpan.FromMilliseconds(400);
        var doc = Doc("doc.md", "# Version 1");
        Go(sut, doc);
        var seen = new List<(string Html, bool Missing)>();
        sut.Nav.Changed += (_, _) => seen.Add((sut.Nav.CurrentHtml, sut.Nav.CurrentFileMissing));

        // What vim, VS Code and Obsidian do: write the new content beside the file, remove the file, and
        // move the new one into its place - leaving a gap in which there is no file at all.
        var temp = Path.Combine(_docs.Path, "doc.md.tmp");
        File.WriteAllText(temp, "# Version 2");
        File.Delete(doc);
        var replacing = Task.Run(async () =>
        {
            await Task.Delay(120).ConfigureAwait(false);
            File.Move(temp, doc);
        });

        _ = sut.Nav.ReloadAsync(); // the watcher's debounce lands in the gap
        // (the real watcher on the folder sees the save too and reloads as well; the page is what is waited for)
        sut.Dispatcher.Pump(() => replacing.IsCompleted && string.Equals(sut.Nav.CurrentTitle, "Version 2", StringComparison.Ordinal));

        Assert.Equal("Version 2", sut.Nav.CurrentTitle);
        Assert.False(sut.Nav.CurrentFileMissing);
        Assert.DoesNotContain(seen, s => s.Html.Contains("Unable", StringComparison.Ordinal) || s.Missing);
    }

    [SkippableFact]
    public void A_file_that_is_locked_for_a_moment_does_not_replace_the_page_with_an_error()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), LockNeedsWindows);
        var sut = Create();
        sut.Nav.RetryDelay = TimeSpan.FromMilliseconds(400);
        var doc = Doc("doc.md", "# Version 1");
        Go(sut, doc);
        File.WriteAllText(doc, "# Version 2");
        var seen = new List<string>();
        sut.Nav.Changed += (_, _) => seen.Add(sut.Nav.CurrentHtml);

        using var editor = new FileStream(doc, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        _ = sut.Nav.ReloadAsync();
        _ = Task.Run(async () =>
        {
            await Task.Delay(120).ConfigureAwait(false);
            await editor.DisposeAsync().ConfigureAwait(false); // the editor lets go before the second look
        });
        sut.Dispatcher.Pump(() => string.Equals(sut.Nav.CurrentTitle, "Version 2", StringComparison.Ordinal));

        Assert.Equal("Version 2", sut.Nav.CurrentTitle);
        Assert.DoesNotContain(seen, html => html.Contains("Unable", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void A_file_that_stays_locked_shows_the_error_only_after_the_second_look()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), LockNeedsWindows);
        var sut = Create();
        sut.Nav.RetryDelay = TimeSpan.FromMilliseconds(300);
        var doc = Doc("doc.md", "# Version 1");
        Go(sut, doc);
        using var editor = new FileStream(doc, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var seen = new List<string>();
        sut.Nav.Changed += (_, _) => seen.Add(sut.Nav.CurrentHtml);

        var reload = sut.Nav.ReloadAsync();
        sut.Dispatcher.Pump(() => seen.Count > 0, 5);
        _ = Assert.Single(seen); // exactly one change, and it is the final one
        sut.Dispatcher.Pump(reload);

        Assert.Contains("Unable to open this file", sut.Nav.CurrentHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_is_really_deleted_stays_on_screen_marked_as_deleted_until_it_comes_back()
    {
        var sut = Create();
        sut.Nav.RetryDelay = TimeSpan.FromMilliseconds(100);
        var doc = Doc("doc.md", "# Version 1");
        Go(sut, doc);

        File.Delete(doc);
        sut.Run(sut.Nav.ReloadAsync());

        Assert.True(sut.Nav.CurrentFileMissing);
        Assert.Equal("Version 1", sut.Nav.CurrentTitle); // the last good render is kept
        Assert.DoesNotContain("Unable", sut.Nav.CurrentHtml, StringComparison.Ordinal);

        File.WriteAllText(doc, "# Version 2");
        sut.Run(sut.Nav.ReloadAsync());

        Assert.False(sut.Nav.CurrentFileMissing);
        Assert.Equal("Version 2", sut.Nav.CurrentTitle);
    }

    [SkippableFact]
    public void A_render_that_runs_long_is_reported_as_rendering()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), LockNeedsWindows);
        var sut = Create();
        sut.Nav.RenderingDelay = TimeSpan.FromMilliseconds(50);
        sut.Nav.RetryDelay = TimeSpan.FromSeconds(3);
        var doc = Doc("doc.md", "# Version 1");
        Go(sut, doc);
        using var editor = new FileStream(doc, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var reload = sut.Nav.ReloadAsync(); // cannot read, and waits before the second try
        sut.Dispatcher.Pump(() => sut.Nav.IsRendering, 10);
        Assert.True(sut.Nav.IsRendering);

        editor.Dispose();
        sut.Dispatcher.Pump(reload);
        Assert.False(sut.Nav.IsRendering);
        Assert.Equal("Version 1", sut.Nav.CurrentTitle);
    }

    [Fact]
    public void A_quick_render_is_never_reported_as_rendering()
    {
        var sut = Create();
        sut.Nav.RenderingDelay = TimeSpan.FromMilliseconds(300);
        var rendering = false;
        sut.Nav.Changed += (_, _) => rendering |= sut.Nav.IsRendering;

        Go(sut, Doc("a.md"));
        Thread.Sleep(500);
        sut.Dispatcher.Drain();

        Assert.False(rendering);
        Assert.False(sut.Nav.IsRendering);
    }

    // ---- dropped text -----------------------------------------------------------------------------------

    [Fact]
    public void Dropped_text_is_shown_without_a_path_and_is_not_reloaded()
    {
        var sut = Create();
        Go(sut, Doc("a.md", "# A"));

        sut.Run(sut.Nav.OpenContentAsync("notes.md", "# Dropped"));

        Assert.Null(sut.Nav.CurrentPath);
        Assert.Equal("Dropped", sut.Nav.CurrentTitle);
        var token = sut.Nav.RenderToken;
        sut.Run(sut.Nav.ReloadAsync()); // there is no file to read again
        Assert.Equal(token, sut.Nav.RenderToken);
    }

    // ---- the images of the document on the page (ARCH-5) ------------------------------------------------

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void The_render_on_the_page_is_the_one_whose_images_may_be_fetched()
    {
        var sut = Create();
        _ = _docs.File("pic.png", Png);
        var withImage = Doc("with.md", "![a](pic.png)");
        var without = Doc("without.md", "# Nothing to see");

        Assert.Null(sut.Assets.Current);

        Go(sut, withImage);
        var first = sut.Assets.Current;
        Assert.NotNull(first);
        Assert.Equal(Path.GetDirectoryName(withImage), first.BaseFolder, ignoreCase: true);

        sut.Run(sut.Nav.ReloadAsync()); // a new render of the same document: a new token, the old one is dead
        var second = sut.Assets.Current;
        Assert.NotNull(second);
        Assert.NotEqual(first.Token, second.Token, StringComparer.Ordinal);
        Assert.False(sut.Assets.TryGetBaseFolder(first.Token, out _));
        Assert.True(sut.Assets.TryGetBaseFolder(second.Token, out _));

        Go(sut, without); // a page with no pictures leaves nothing to fetch
        Assert.Null(sut.Assets.Current);
    }

    [Fact]
    public void Dropped_text_and_messages_do_not_inherit_the_previous_documents_grant()
    {
        var sut = Create();
        _ = _docs.File("pic.png", Png);
        Go(sut, Doc("with.md", "![a](pic.png)"));
        Assert.NotNull(sut.Assets.Current);

        sut.Run(sut.Nav.OpenContentAsync("notes.md", "# Dropped, no pictures"));
        Assert.Null(sut.Assets.Current);

        Go(sut, Doc("again.md", "![a](pic.png)"));
        Assert.NotNull(sut.Assets.Current);

        // a sentence in place of a document ("too large to open") has no pictures either
        var huge = Doc("huge.md");
        using (var stream = new FileStream(huge, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(MarkdownService.MaxFileBytes + 1);
        }

        Go(sut, huge);
        Assert.Contains("too large", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.Null(sut.Assets.Current);
    }

    // ---- counting documents for the rating request ----------------------------------------------------------

    [SkippableFact]
    public void A_document_counts_towards_the_rating_request_when_it_is_opened_and_not_when_it_is_reloaded()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "the rating request is a Microsoft Store feature");
        var sut = Create();
        var opened = 0;
        sut.Nav.DocumentOpened += (_, _) => opened++;
        var (a, b) = (Doc("a.md", "# A"), Doc("b.md", "# B"));

        Go(sut, a);
        Go(sut, b);
        sut.Run(sut.Nav.ReloadAsync());
        Go(sut, b); // opened again: still the same document
        sut.Run(sut.Nav.BackAsync());

        Assert.Equal(3, opened); // a, b, and a again
        Assert.Equal(3, sut.Prefs.Current.DocumentsOpened);
    }

    [SkippableFact]
    public void Once_the_request_is_answered_documents_are_no_longer_counted()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "the rating request is a Microsoft Store feature");
        var sut = Create();
        sut.Prefs.Current.RatingAnswered = RatingPrompt.Campaign;

        Go(sut, Doc("a.md"));

        Assert.Equal(0, sut.Prefs.Current.DocumentsOpened);
    }

    // ---- links that leave the app ---------------------------------------------------------------------------

    /// <summary>The page reports what was clicked; what it reports is a request, and the shell is only asked for what passes the allow-list.</summary>
    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("FILE:///C:/x.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("search-ms:query=x&crumb=location:\\\\attacker\\share")]
    [InlineData("ms-windows-store://review/?PFN=x")]
    [InlineData("markdown://document/C:/x.md")]
    [InlineData("C:\\x.exe")]
    [InlineData("\\\\server\\share\\x.lnk")]
    [InlineData("//server/share/x.lnk")]
    [InlineData("")]
    public void A_link_with_a_scheme_that_is_not_allowed_never_reaches_the_shell(string url)
    {
        var sut = Create();

        sut.Nav.HandleLink("ext", url);

        Assert.Empty(sut.Shell.Opened);
        Assert.Empty(sut.Shell.NewWindows);
        Assert.Empty(sut.Shell.Revealed);
        Assert.Empty(sut.Shell.StoreReviews);
    }

    [Theory]
    [InlineData("https://example.com/a b", "https://example.com/a%20b")]
    [InlineData("  HTTP://Example.com  ", "http://example.com/")]
    [InlineData("mailto:someone@example.com", "mailto:someone@example.com")]
    public void An_allowed_link_reaches_the_shell_in_its_normalised_form(string url, string expected)
    {
        var sut = Create();

        sut.Nav.HandleLink("ext", url);

        Assert.Equal(expected, Assert.Single(sut.Shell.Opened).AbsoluteUri);
    }

    [Fact]
    public void A_link_that_names_a_kind_nobody_knows_does_nothing()
    {
        var sut = Create();

        sut.Nav.HandleLink("shell", "https://example.com/");
        sut.Nav.HandleLink("", "https://example.com/");

        Assert.Empty(sut.Shell.Opened);
    }

    // ---- the history, from Settings and from the viewer --------------------------------------------------------

    [Fact]
    public void Clearing_the_history_goes_through_the_navigation_service()
    {
        var sut = Create();
        Go(sut, Doc("a.md"));
        Go(sut, Doc("b.md"));
        Assert.Equal(2, sut.Nav.GlobalHistory.Count);
        var changed = 0;
        sut.Nav.Changed += (_, _) => changed++;

        sut.Nav.ClearHistory();
        sut.Dispatcher.Drain();

        Assert.Empty(sut.Nav.GlobalHistory);
        Assert.Empty(sut.History.Entries);
        Assert.True(changed > 0, "the sidebar has to be told");
    }

    // ---- helpers ---------------------------------------------------------------------------------------

    private static bool PathIs(string? actual, string expected) => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    private string Doc(string name, string content = "# Title") => _docs.File(name, content);
    private static void Go(Sut sut, string path) => sut.Run(sut.Nav.NavigateAsync(path));

    /// <summary>Runs <paramref name="work"/> on a thread of its own and waits for it: how the file watcher's timer calls in.</summary>
    private static void OnAnotherThread(Action work)
    {
        var thread = new Thread(() => work());
        thread.Start();
        thread.Join();
    }

    private Sut Create()
    {
        var sut = new Sut(_data.Path);
        _disposables.Add(sut);
        return sut;
    }

    /// <summary>The service under test with everything it needs, built on the calling thread (its "UI thread").</summary>
    private sealed class Sut : IDisposable
    {
        public Sut(string dataFolder)
        {
            Dispatcher = new QueueDispatcher();
            Assets = new AssetScope();
            Shell = new FakeShellLauncher();
            Watcher = new FileWatcher();
            History = new HistoryStore(dataFolder, debounce: TimeSpan.FromMinutes(5));
            Prefs = new PreferencesStore(dataFolder, debounce: TimeSpan.FromMinutes(5));
            Nav = new NavigationService(new MarkdownService(), History, Prefs, new StartupArguments([]), Assets, Shell, Watcher, NullLogger.Instance);
            Nav.Attach(Dispatcher);
        }

        public QueueDispatcher Dispatcher { get; }
        public AssetScope Assets { get; }
        public FakeShellLauncher Shell { get; }
        public FileWatcher Watcher { get; }
        public HistoryStore History { get; }
        public PreferencesStore Prefs { get; }
        public NavigationService Nav { get; }

        /// <summary>Lets the UI queue run until <paramref name="task"/> has finished.</summary>
        public void Run(Task task)
        {
            Dispatcher.Pump(task);
            task.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Nav.Dispose();
            History.Dispose();
            Prefs.Dispose();
            Dispatcher.Dispose();
        }
    }
}
