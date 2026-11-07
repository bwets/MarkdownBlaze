using Bunit;
using MarkdownBlaze.Components.Viewer;
using MarkdownBlaze.Interop;
using MarkdownBlaze.Platform;
using MarkdownBlaze.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarkdownBlaze.Tests;

/// <summary>
/// The viewer on a small screen: the sidebar as a drawer in a narrow window, the More menu that holds what the toolbar
/// leaves out, the keys left out of the menus where there is no keyboard, and the system's Back button, which puts away
/// the topmost thing that is open before it goes back in the history. The page that measures the screen is faked (the
/// state is told what it would say).
/// </summary>
public sealed class SmallScreenTests : IDisposable
{
    private readonly TempFolder _data = new();
    private readonly TempFolder _docs = new();
    private readonly BunitContext _context = new();
    private readonly PreferencesStore _prefs;
    private readonly HistoryStore _history;
    private readonly NavigationService _nav;
    private readonly FileWatcher _watcher = new();
    private readonly FakeShellLauncher _shell = new();

    public SmallScreenTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;

        _prefs = new PreferencesStore(_data.Path, debounce: TimeSpan.FromMinutes(5));
        _history = new HistoryStore(_data.Path, debounce: TimeSpan.FromMinutes(5));
        _nav = new NavigationService(
            new MarkdownService(TestAddresses.Desktop), _history, _prefs, new StartupArguments([]), new AssetScope(), _shell, _watcher, NullLogger.Instance);

        var services = _context.Services;
        _ = services.AddSingleton(_prefs);
        _ = services.AddSingleton(_history);
        _ = services.AddSingleton(_nav);
        _ = services.AddSingleton<IShellLauncher>(_shell);
        _ = services.AddSingleton<MarkdownService>();
        _ = services.AddSingleton<FileTreeService>();
        _ = services.AddSingleton(TestAddresses.Desktop);
        _ = services.AddSingleton<IFilePicker, PhotinoFilePicker>();
        _ = services.AddSingleton<IPrinter, PagePrinter>();
        _ = services.AddSingleton<IWindowControl>(new WindowStateService(_prefs, _history));
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

    private ViewerState State => _context.Services.GetRequiredService<ViewerState>();
    private ViewerActions Actions => _context.Services.GetRequiredService<ViewerActions>();
    private ViewerMenus Menus => _context.Services.GetRequiredService<ViewerMenus>();

    private void AssertMenu(params string[] expected) =>
        Assert.Equal(expected, [.. State.Menu!.Items.Select(i => i.Label)], StringComparer.Ordinal);

    // ---- the drawer ----------------------------------------------------------------------------

    /// <summary>On a desktop window the button pins and unpins the sidebar, which is remembered; the drawer is not involved.</summary>
    [Fact]
    public void The_sidebar_button_pins_the_sidebar_in_a_wide_window()
    {
        var pinned = State.SidebarPinned;

        State.ToggleSidebar();

        Assert.NotEqual(pinned, State.SidebarPinned);
        Assert.False(State.DrawerOpen);
    }

    /// <summary>In a narrow window it slides the drawer out and back, and leaves what the desktop remembers alone.</summary>
    [Fact]
    public void The_sidebar_button_slides_the_drawer_in_a_narrow_window_and_remembers_nothing()
    {
        State.SetLayout(narrow: true, noKeyboard: false);
        var pinned = State.SidebarPinned;

        State.ToggleSidebar();
        Assert.True(State.DrawerOpen);
        State.ToggleSidebar();
        Assert.False(State.DrawerOpen);

        Assert.Equal(pinned, State.SidebarPinned);
        Assert.Equal(pinned, _prefs.Current.SidebarPinned);
    }

    /// <summary>Dragging the window wide again has no use for the drawer: it is put away.</summary>
    [Fact]
    public void Widening_the_window_puts_the_drawer_away()
    {
        State.SetLayout(narrow: true, noKeyboard: false);
        State.ToggleSidebar();

        State.SetLayout(narrow: false, noKeyboard: false);

        Assert.False(State.DrawerOpen);
    }

    /// <summary>Esc closes the drawer before it does anything else.</summary>
    [Fact]
    public async Task Escape_closes_the_drawer()
    {
        var commands = _context.Services.GetRequiredService<Commands>();
        State.SetLayout(narrow: true, noKeyboard: false);
        State.ToggleSidebar();

        Assert.True(commands.TryRunCombo("escape", out var running));
        await running.ConfigureAwait(true);

        Assert.False(State.DrawerOpen);
    }

    // ---- Back ----------------------------------------------------------------------------------

    /// <summary>
    /// Back puts away one thing at a time, the topmost first — a menu or a dialog, then the drawer, then the print bar — and only
    /// then goes back in the history; at the first document there is nothing left for it to do, and the app is left.
    /// </summary>
    [Fact]
    public async Task Back_closes_what_is_open_then_goes_back_in_the_history_then_leaves()
    {
        var first = _docs.File("first.md", "# First\n");
        var second = _docs.File("second.md", "# Second\n");
        await OpenAsync(first).ConfigureAwait(true);
        await OpenAsync(second).ConfigureAwait(true);

        State.SetLayout(narrow: true, noKeyboard: true);
        State.OpenPrintBar();
        State.ToggleSidebar();
        Menus.OpenMore();

        Assert.True(await Actions.BackAsync().ConfigureAwait(true));
        Assert.Null(State.Menu);
        Assert.True(State.DrawerOpen);
        Assert.True(State.PrintBarOpen);

        Assert.True(await Actions.BackAsync().ConfigureAwait(true));
        Assert.False(State.DrawerOpen);
        Assert.True(State.PrintBarOpen);

        Assert.True(await Actions.BackAsync().ConfigureAwait(true));
        Assert.False(State.PrintBarOpen);
        Assert.Equal(second, _nav.CurrentPath);

        Assert.True(await Actions.BackAsync().ConfigureAwait(true));
        await WaitForCurrentAsync(first).ConfigureAwait(true);

        Assert.False(await Actions.BackAsync().ConfigureAwait(true));
        Assert.Equal(first, _nav.CurrentPath);
    }

    // ---- the menus -----------------------------------------------------------------------------

    /// <summary>A wide window keeps the toolbar's buttons on the toolbar, so its More menu is what it always was.</summary>
    [Fact]
    public void The_more_menu_of_a_wide_window_is_unchanged()
    {
        Menus.OpenMore();

        AssertMenu("Keyboard shortcuts", "What's new", "Settings");
    }

    /// <summary>A narrow window moves what the toolbar cannot fit into the More menu, and a phone has no keys to list.</summary>
    [Fact]
    public void The_more_menu_of_a_narrow_window_holds_what_the_toolbar_leaves_out_and_no_keys_without_a_keyboard()
    {
        State.SetLayout(narrow: true, noKeyboard: true);

        Menus.OpenMore();

        AssertMenu("Open file", "Open folder", "Refresh", "Print…", "Open containing folder", "Zoom in", "Zoom out", "Reset zoom", "What's new", "Settings");
        Assert.True(State.Menu!.Right);
    }

    /// <summary>A tablet with a keyboard attached is told so, and the shortcuts come back.</summary>
    [Fact]
    public void The_shortcuts_come_back_when_a_keyboard_is_attached()
    {
        State.SetLayout(narrow: false, noKeyboard: true);
        Menus.OpenMore();
        Assert.DoesNotContain(State.Menu!.Items, i => string.Equals(i.Label, "Keyboard shortcuts", StringComparison.Ordinal));

        State.SetLayout(narrow: false, noKeyboard: false);
        Menus.OpenMore();

        Assert.Contains(State.Menu!.Items, i => string.Equals(i.Label, "Keyboard shortcuts", StringComparison.Ordinal));
    }

    /// <summary>The view menu goes to the edge in a narrow window, where the buttons are crowded to the right.</summary>
    [Fact]
    public void The_view_menu_goes_to_the_edge_in_a_narrow_window()
    {
        Menus.OpenView(new MouseEventArgs { ClientX = 300 });
        Assert.False(State.Menu!.Right);

        State.SetLayout(narrow: true, noKeyboard: false);
        Menus.OpenView(new MouseEventArgs { ClientX = 300 });
        Assert.True(State.Menu!.Right);
    }

    /// <summary>A long press asks for the same menu as a right click; a host that cannot do what an entry says does not offer it.</summary>
    [Fact]
    public void The_row_menu_leaves_out_what_the_host_cannot_do()
    {
        var click = new MouseEventArgs { ClientX = 40, ClientY = 90 };

        Menus.OpenRow(click, "C:/docs/a.md");
        AssertMenu("Open", "Open in new window", "Copy filename", "Open containing folder");
        Assert.Equal((40, 90), (State.Menu!.X, State.Menu.Y));

        _shell.CanOpenInNewWindow = false;
        _shell.CanRevealInFileManager = false;
        Menus.OpenRow(click, "C:/docs/a.md");
        AssertMenu("Open", "Copy filename");
    }

    // ---- the host's window ---------------------------------------------------------------------

    /// <summary>With no viewer to ask, Back is the system's.</summary>
    [Fact]
    public async Task Back_goes_to_the_system_when_the_viewer_has_not_taken_it()
    {
        var window = new HostWindow();

        Assert.False(await window.HandleBackAsync().ConfigureAwait(true));

        window.SetBackHandler(() => Task.FromResult(true));
        Assert.True(await window.HandleBackAsync().ConfigureAwait(true));

        window.SetBackHandler(handler: null);
        Assert.False(await window.HandleBackAsync().ConfigureAwait(true));
    }

    /// <summary>The viewer is told when the bars move, and what it reads is the latest.</summary>
    [Fact]
    public void The_viewer_is_told_when_the_bars_move()
    {
        var window = new HostWindow();
        var raised = 0;
        window.InsetsChanged += (_, _) => raised++;
        Assert.Equal(WindowInsets.None, window.Insets);

        window.SetInsets(new WindowInsets(24, 0, 16, 8));

        Assert.Equal(1, raised);
        Assert.Equal(new WindowInsets(24, 0, 16, 8), window.Insets);
    }

    /// <summary>The system bars' icons are recoloured when the app turns dark or light, and not for a report that says nothing new.</summary>
    [Fact]
    public void The_appearance_is_reported_only_when_it_changes()
    {
        var window = new HostWindow();
        var raised = 0;
        window.AppearanceChanged += (_, _) => raised++;

        window.SetDark(dark: false);
        window.SetDark(dark: true);
        window.SetDark(dark: true);
        window.SetDark(dark: false);

        Assert.Equal(2, raised);
        Assert.False(window.Dark);
    }

    // ---- helpers -------------------------------------------------------------------------------

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
}
