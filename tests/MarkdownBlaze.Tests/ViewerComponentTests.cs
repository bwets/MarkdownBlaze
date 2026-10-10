using Bunit;
using MarkdownBlaze.Components.Overlays;
using MarkdownBlaze.Components.Viewer;
using MarkdownBlaze.Interop;
using MarkdownBlaze.Platform;
using MarkdownBlaze.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarkdownBlaze.Tests;

/// <summary>
/// ARCH-1, section 8 item 12: the viewer's parts rendered with the real services over a scratch data
/// folder and the page's JavaScript faked. The headless launch of the whole app is not practical, so this
/// is the smoke test: the components build, and the help overlay is the command registry.
/// </summary>
public sealed class ViewerComponentTests : IDisposable
{
    private readonly TempFolder _data = new();
    private readonly BunitContext _context = new();
    private readonly PreferencesStore _prefs;
    private readonly HistoryStore _history;
    private readonly NavigationService _nav;
    private readonly FileWatcher _watcher = new();
    private readonly FakeShellLauncher _shell = new();

    public ViewerComponentTests()
    {
        // Loose: the popups import their focus module, and nothing here cares what it does.
        _context.JSInterop.Mode = JSRuntimeMode.Loose;

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
        _ = services.AddScoped<Commands>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _nav.Dispose();
        _watcher.Dispose();
        _prefs.Dispose();
        _history.Dispose();
        _data.Dispose();
    }

    /// <summary>The shortcuts overlay is drawn from the command registry: the same rows, in the same order, with the same keys.</summary>
    [Fact]
    public void The_shortcuts_overlay_shows_the_command_registry()
    {
        var commands = _context.Services.GetRequiredService<Commands>();
        _context.Services.GetRequiredService<ViewerState>().SetShortcutsOpen(open: true);

        var overlay = _context.Render<ShortcutsDialog>();

        var shown = overlay.FindAll("table.shortcuts-table tr")
            .Select(row => (
                Label: row.QuerySelector("td.sc-action")!.TextContent.Trim(),
                Keys: string.Join('+', row.QuerySelectorAll("kbd").Select(k => k.TextContent))))
            .ToList();
        var expected = commands.All
            .Where(c => c.Keys.Length > 0)
            .Select(c => (c.Label, Keys: string.Join('+', c.Keys.Split(' ', StringSplitOptions.RemoveEmptyEntries).SelectMany(k => k.Split('+')))))
            .ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, shown);
        Assert.NotNull(overlay.Find("[role=dialog][aria-modal=true]"));
    }

    /// <summary>The overlay is not in the page until it is asked for, and goes with Esc (the Dismiss command).</summary>
    [Fact]
    public async Task The_overlay_is_closed_until_asked_for_and_the_dismiss_command_closes_it()
    {
        var state = _context.Services.GetRequiredService<ViewerState>();
        var commands = _context.Services.GetRequiredService<Commands>();

        var overlay = _context.Render<ShortcutsDialog>();
        Assert.Empty(overlay.FindAll("[role=dialog]"));

        await commands.RunAsync(Commands.Help).ConfigureAwait(true);
        await overlay.WaitForAssertionAsync(() => Assert.Single(overlay.FindAll("[role=dialog]"))).ConfigureAwait(true);

        Assert.True(commands.TryRunCombo("escape", out var running));
        await running.ConfigureAwait(true);
        await overlay.WaitForAssertionAsync(() => Assert.Empty(overlay.FindAll("[role=dialog]"))).ConfigureAwait(true);
        Assert.False(state.ShortcutsOpen);
    }

    /// <summary>What the page is told to report is what the registry runs: no key is reported that nothing answers.</summary>
    [Fact]
    public void Every_key_combination_the_page_reports_has_a_command()
    {
        var commands = _context.Services.GetRequiredService<Commands>();

        Assert.NotEmpty(commands.Combos);
        Assert.Equal(commands.Combos.Count(), commands.Combos.Distinct(StringComparer.Ordinal).Count());
        foreach (var combo in commands.Combos)
        {
            Assert.DoesNotContain(combo, char.IsUpper); // spelled as the page spells them: "ctrl+shift+o"
            Assert.Contains(commands.All, c => c.Combos.Contains(combo, StringComparer.Ordinal));
        }
    }
}
