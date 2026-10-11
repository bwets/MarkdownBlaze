using MarkdownBlaze.Models;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Tests;

/// <summary>
/// SEC-12: a render that takes too long is given up on — the page says so and its result is never shown —
/// and the renders that were given up on but are still running are counted, so that a reader who keeps
/// opening documents that hang does not pile up threads. The fake renderer holds back every file whose name
/// has ".slow." in it until it is released.
/// </summary>
public sealed class RenderDeadlineTests : IDisposable
{
    private readonly TempFolder _data = new();
    private readonly TempFolder _docs = new();

    public void Dispose()
    {
        _data.Dispose();
        _docs.Dispose();
    }

    [Fact]
    public void The_deadline_is_thirty_seconds_and_at_most_two_given_up_renders_may_still_run()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), NavigationService.DefaultRenderDeadline);
        Assert.Equal(2, NavigationService.MaxAbandonedRenders);
    }

    [Fact]
    public void A_render_that_outlasts_the_deadline_is_given_up_on_and_the_page_says_so_with_the_file_name()
    {
        using var sut = new Sut(_data.Path);
        var path = Doc("big.slow.md");

        Go(sut, path);

        Assert.Contains("could not be rendered within", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.Contains("big.slow.md", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.False(sut.Nav.IsRendering);
        Assert.Equal(1, sut.Nav.AbandonedRenders);
        Assert.Equal(path, sut.Nav.CurrentPath, ignoreCase: true);
    }

    [Fact]
    public void The_name_of_the_file_is_text_in_the_message()
    {
        using var sut = new Sut(_data.Path);

        Go(sut, Doc("a&b.slow.md"));

        Assert.Contains("a&amp;b.slow.md", sut.Nav.CurrentHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_later_navigation_works_while_the_given_up_render_is_still_running()
    {
        using var sut = new Sut(_data.Path);
        Go(sut, Doc("big.slow.md"));

        Go(sut, Doc("small.md"));

        Assert.Contains("Rendered small", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.Equal("Title of small", sut.Nav.CurrentTitle);
        Assert.Equal(1, sut.Nav.AbandonedRenders); // the first is still going
    }

    [Fact]
    public void The_given_up_render_finishing_changes_nothing_on_the_page_and_is_logged_with_its_duration()
    {
        using var sut = new Sut(_data.Path);
        Go(sut, Doc("big.slow.md"));
        Go(sut, Doc("small.md"));
        var (html, token, title) = (sut.Nav.CurrentHtml, sut.Nav.RenderToken, sut.Nav.CurrentTitle);
        var changes = 0;
        sut.Nav.Changed += (_, _) => changes++;

        sut.Fake.Release();
        sut.Dispatcher.Pump(() => sut.Nav.AbandonedRenders == 0);
        sut.Dispatcher.Drain();

        Assert.Equal(html, sut.Nav.CurrentHtml);
        Assert.Equal(token, sut.Nav.RenderToken);
        Assert.Equal(title, sut.Nav.CurrentTitle);
        Assert.Equal(0, changes);
        var ended = Assert.Single(sut.Log.Entries, entry => entry.Message.Contains("has ended", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, ended.Level);
        Assert.Contains(" s after it started", ended.Message, StringComparison.Ordinal);
        Assert.Contains(sut.Log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("took longer than", StringComparison.Ordinal));
    }

    [Fact]
    public void A_render_that_is_replaced_while_it_runs_counts_as_given_up_only_once_it_has_outlasted_the_deadline()
    {
        using var sut = new Sut(_data.Path);
        sut.Nav.RenderDeadline = TimeSpan.FromMilliseconds(400);
        var first = sut.Nav.NavigateAsync(Doc("big.slow.md"));
        sut.Dispatcher.Pump(() => sut.Fake.Calls == 1);

        Go(sut, Doc("small.md"));

        Assert.Contains("Rendered small", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.Equal(0, sut.Nav.AbandonedRenders); // replaced a moment ago: it will stop at its next look at the token
        sut.Dispatcher.Pump(() => sut.Nav.AbandonedRenders == 1); // and now it has been going longer than a render may
        sut.Fake.Release();
        sut.Dispatcher.Pump(() => sut.Nav.AbandonedRenders == 0);
        sut.Run(first);
        Assert.Contains("Rendered small", sut.Nav.CurrentHtml, StringComparison.Ordinal); // and the old one never replaced it
    }

    [Fact]
    public void A_burst_of_navigations_that_replace_each_other_is_never_refused()
    {
        using var sut = new Sut(_data.Path);
        sut.Nav.RenderDeadline = TimeSpan.FromMinutes(5);
        for (var i = 0; i < 6; i++) _ = sut.Nav.NavigateAsync(Doc("burst" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".slow.md"));

        Go(sut, Doc("last.md"));

        Assert.Contains("Rendered last", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Still rendering", sut.Nav.CurrentHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void While_two_given_up_renders_are_still_running_a_new_navigation_is_not_started()
    {
        using var sut = new Sut(_data.Path);
        Go(sut, Doc("one.slow.md"));
        Go(sut, Doc("two.slow.md"));
        Assert.Equal(2, sut.Nav.AbandonedRenders);
        var started = sut.Fake.Calls;

        Go(sut, Doc("third.md"));

        Assert.Contains("Still rendering an earlier document", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.Contains("third.md", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.Equal(started, sut.Fake.Calls); // no third thread
        Assert.False(sut.Nav.IsRendering);

        // and once they have ended, the same navigation works
        sut.Fake.Release();
        sut.Dispatcher.Pump(() => sut.Nav.AbandonedRenders == 0);
        Go(sut, Doc("third.md"));
        Assert.Contains("Rendered third", sut.Nav.CurrentHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reload_leaves_the_page_alone_while_two_given_up_renders_are_still_running()
    {
        using var sut = new Sut(_data.Path);
        Go(sut, Doc("fine.md"));
        Go(sut, Doc("one.slow.md"));
        Go(sut, Doc("two.slow.md"));
        var (html, token, calls) = (sut.Nav.CurrentHtml, sut.Nav.RenderToken, sut.Fake.Calls);

        sut.Run(sut.Nav.ReloadAsync());

        Assert.Equal(html, sut.Nav.CurrentHtml);
        Assert.Equal(token, sut.Nav.RenderToken);
        Assert.Equal(calls, sut.Fake.Calls);
    }

    [Fact]
    public void A_document_nested_too_deeply_for_markdown_says_so_instead_of_unable_to_open()
    {
        using var sut = new Sut(_data.Path, new MarkdownService());
        sut.Nav.RenderDeadline = NavigationService.DefaultRenderDeadline; // Markdig refuses it in a moment; the short deadline is not what is tested
        var path = Doc("deep.md", string.Concat(Enumerable.Repeat(">", 10_000)) + " x");

        Go(sut, path);

        Assert.Contains("nested too deeply", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Unable to open", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(sut.Log.Entries, entry => entry.Level >= LogLevel.Error); // a fact about the document, not a fault
    }

    [Fact]
    public void Any_other_failure_of_a_render_is_still_unable_to_open_and_logged_as_an_error()
    {
        using var sut = new Sut(_data.Path, new ThrowingRenderer());

        Go(sut, Doc("a.md"));

        Assert.Contains("Unable to open this file", sut.Nav.CurrentHtml, StringComparison.Ordinal);
        Assert.Contains(sut.Log.Entries, entry => entry.Level == LogLevel.Error);
    }

    // ---- helpers ---------------------------------------------------------------------------------------

    private string Doc(string name, string content = "# Title") => _docs.File(name, content);

    /// <summary>
    /// Opens a document and waits for the page. Only a document the fake holds back is held to the short
    /// deadline: one that renders at once gets a generous one, because on a loaded machine (the macOS
    /// runner, for one) merely starting the render's thread took longer than 200 ms, and a document that
    /// "timed out" that way was counted as a render still running — which refused the navigation the test
    /// was actually about.
    /// </summary>
    private static void Go(Sut sut, string path)
    {
        var deadline = sut.Nav.RenderDeadline;
        if (!path.Contains(".slow.", StringComparison.OrdinalIgnoreCase)) sut.Nav.RenderDeadline = TimeSpan.FromMinutes(5);
        try
        {
            sut.Run(sut.Nav.NavigateAsync(path));
        }
        finally
        {
            sut.Nav.RenderDeadline = deadline;
        }
    }

    private sealed class ThrowingRenderer : IDocumentRenderer
    {
        public string? GetStartupFilePath(IEnumerable<string> args) => null;
        public FileRender RenderFile(string markdownFilePath, CancellationToken cancellationToken = default) => throw new InvalidOperationException("the renderer broke");
        public RenderResult RenderText(string fileName, string markdown) => throw new InvalidOperationException("the renderer broke");
    }

    /// <summary>The service under test with everything it needs, built on the calling thread (its "UI thread").</summary>
    private sealed class Sut : IDisposable
    {
        private readonly FakeDocumentRenderer? _fake;
        private readonly FileWatcher _watcher = new();

        /// <summary>Makes the service over the fake renderer, or over <paramref name="own"/> when the test brings its own.</summary>
        public Sut(string dataFolder, IDocumentRenderer? own = null)
        {
            _fake = own is null ? new FakeDocumentRenderer { Blocks = path => path.Contains(".slow.", StringComparison.OrdinalIgnoreCase) } : null;
            Dispatcher = new QueueDispatcher();
            Log = new TestLogger();
            History = new HistoryStore(dataFolder, debounce: TimeSpan.FromMinutes(5));
            Prefs = new PreferencesStore(dataFolder, debounce: TimeSpan.FromMinutes(5));
            var renderer = own ?? _fake!;
            var (startup, assets, shell) = (new StartupArguments([]), new AssetScope(), new FakeShellLauncher());
            Nav = new NavigationService(renderer, History, Prefs, startup, assets, shell, _watcher, Log) { RenderDeadline = TimeSpan.FromMilliseconds(200) };
            Nav.Attach(Dispatcher);
        }

        public FakeDocumentRenderer Fake => _fake ?? throw new InvalidOperationException("this test brought its own renderer");
        public QueueDispatcher Dispatcher { get; }
        public TestLogger Log { get; }
        public HistoryStore History { get; }
        public PreferencesStore Prefs { get; }
        public NavigationService Nav { get; }

        public void Run(Task task)
        {
            Dispatcher.Pump(task);
            task.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _fake?.Dispose();
            Nav.Dispose();
            _watcher.Dispose();
            History.Dispose();
            Prefs.Dispose();
            Dispatcher.Dispose();
        }
    }
}
