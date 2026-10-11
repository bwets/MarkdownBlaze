using Bunit;
using MarkdownBlaze.Components.Sidebar;
using MarkdownBlaze.Components.Viewer;
using MarkdownBlaze.Interop;
using MarkdownBlaze.Platform;
using MarkdownBlaze.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarkdownBlaze.Tests;

/// <summary>
/// The Contents tab drawn for real: a folder with a document at the top and one in a sub-folder, the
/// reader moving between them. What is asserted is the one thing the sidebar must get right — exactly
/// one panel is the open document's, and it is the right one — because a panel that keeps the marking
/// of a document that is no longer open sends the reader to the wrong outline.
/// </summary>
public sealed class FolderTreeTests : IDisposable
{
    private readonly TempFolder _data = new();
    private readonly TempFolder _docs = new();
    private readonly BunitContext _context = new();
    private readonly PreferencesStore _prefs;
    private readonly HistoryStore _history;
    private readonly NavigationService _nav;
    private readonly FileWatcher _watcher = new();
    private readonly FakeShellLauncher _shell = new();
    private readonly string _top;
    private readonly string _nested;
    private readonly string _folder;

    public FolderTreeTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;

        _top = Path.Combine(_docs.Path, "Enterprise Architecture.md");
        _folder = Path.Combine(_docs.Path, "personas");
        _nested = Path.Combine(_folder, "manoj.md");
        _ = Directory.CreateDirectory(_folder);
        File.WriteAllText(_top, "# Enterprise Architecture\n\n## Scope\n\n## Approach\n");
        File.WriteAllText(_nested, "# Manoj, CIO\n\n## Who he is\n\n## What he cares about\n");

        _prefs = new PreferencesStore(_data.Path, debounce: TimeSpan.FromMinutes(5));
        _history = new HistoryStore(_data.Path, debounce: TimeSpan.FromMinutes(5));
        _nav = new NavigationService(new MarkdownService(), _history, _prefs, new StartupArguments([]), new AssetScope(), _shell, _watcher, NullLogger.Instance);

        var services = _context.Services;
        _ = services.AddSingleton(_prefs);
        _ = services.AddSingleton(_history);
        _ = services.AddSingleton(_nav);
        _ = services.AddSingleton<IShellLauncher>(_shell);
        _ = services.AddSingleton<MarkdownService>();
        _ = services.AddSingleton<FileTreeService>();
        _ = services.AddSingleton<NativeDialogs>();
        _ = services.AddSingleton(new WindowStateService(_prefs, _history));
        _ = services.AddSingleton(NullLogger<ViewerInterop>.Instance);
        _ = services.AddScoped<ViewerInterop>();
        _ = services.AddScoped<ViewerState>();
        _ = services.AddScoped<ViewerActions>();
        _ = services.AddScoped<ViewerMenus>();
        _ = services.AddScoped<Commands>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _nav.Dispose();
        _watcher.Dispose();
        _prefs.Dispose();
        _history.Dispose();
        _docs.Dispose();
        _data.Dispose();
    }

    /// <summary>
    /// Open the top document, open the one in the sub-folder from the tree, go back: at every step one
    /// panel, and only one, is the open document's — and the outline shown is that document's.
    /// </summary>
    [Fact]
    public async Task Only_the_open_document_keeps_the_current_marking_as_the_reader_moves()
    {
        var state = _context.Services.GetRequiredService<ViewerState>();
        var tree = _context.Services.GetRequiredService<FileTreeService>();

        await OpenAsync(_top).ConfigureAwait(true);
        tree.SetRootFromFile(_top);
        Refresh(state, tree);
        var cut = _context.Render<FolderTree>();
        AssertCurrent(cut, "Enterprise Architecture.md", firstHeading: "Enterprise Architecture");

        tree.Toggle(_folder);
        Refresh(state, tree);
        var nestedShown = cut.WaitForAssertionAsync(() =>
            Assert.Contains(cut.FindAll(".panel-file"), p => Title(p).EndsWith("manoj.md", StringComparison.Ordinal)));
        await nestedShown.ConfigureAwait(true);

        await OpenAsync(_nested).ConfigureAwait(true);
        Refresh(state, tree);
        AssertCurrent(cut, "manoj.md", firstHeading: "Manoj, CIO");

        await _nav.BackAsync().ConfigureAwait(true);
        await WaitForCurrentAsync(_top).ConfigureAwait(true);
        Refresh(state, tree);
        AssertCurrent(cut, "Enterprise Architecture.md", firstHeading: "Enterprise Architecture");
    }

    /// <summary>What the viewer does on every change: the rows are worked out once, and whoever draws them is told.</summary>
    private void Refresh(ViewerState state, FileTreeService tree)
    {
        state.SetRows(new TreeRows(
            tree.Nodes(_nav.CurrentPath),
            tree.Detached(_nav.CurrentPath),
            _nav.CurrentHeadings.Count == 0 ? 1 : _nav.CurrentHeadings.Min(h => h.Level)));
        state.NotifyChanged();
    }

    private async Task OpenAsync(string path)
    {
        await _nav.NavigateAsync(path).ConfigureAwait(true);
        await WaitForCurrentAsync(path).ConfigureAwait(true);
    }

    private async Task WaitForCurrentAsync(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!string.Equals(_nav.CurrentPath, path, StringComparison.OrdinalIgnoreCase) || _nav.CurrentHeadings.Count == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, $"{path} did not become the open document.");
            await Task.Delay(20).ConfigureAwait(true);
        }
    }

    private static string Title(AngleSharp.Dom.IElement panel) => panel.QuerySelector(".panel-head")!.GetAttribute("title")!;

    private static void AssertCurrent(IRenderedComponent<FolderTree> cut, string fileName, string firstHeading)
    {
        cut.WaitForAssertion(() =>
        {
            var current = cut.FindAll(".panel-file.current");
            var marked = string.Join(", ", current.Select(p => Path.GetFileName(Title(p))));
            Assert.True(current.Count == 1, $"Expected one current panel ({fileName}); marked: [{marked}]");
            Assert.EndsWith(fileName, Title(current[0]), StringComparison.Ordinal);
            _ = Assert.Single(cut.FindAll("#" + TreePanel.CurrentPanelId));
            Assert.Equal(firstHeading, current[0].QuerySelector(".outline-row")!.TextContent.Trim());
            Assert.Empty(cut.FindAll(".panel-file:not(.current) .outline-row"));
        });
    }
}
