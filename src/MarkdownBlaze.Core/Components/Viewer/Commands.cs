using MarkdownBlaze.Interop;
using MarkdownBlaze.Services;

namespace MarkdownBlaze.Components.Viewer;

/// <summary>
/// The registry of what the viewer can be told to do, and the single source for everything that names a
/// key: the keyboard handler runs the command a key combination is registered for, the toolbar's buttons
/// run commands by id, the page is told which combinations to report (<see cref="Combos"/>), and the
/// shortcuts overlay is drawn from <see cref="All"/> — so the help can never describe a key that does
/// nothing, or leave out one that does.
/// </summary>
internal sealed class Commands
{
    public const string ToggleSidebar = "sidebar";
    public const string Back = "back";
    public const string Forward = "forward";
    public const string Refresh = "refresh";
    public const string OpenFile = "open-file";
    public const string OpenFolder = "open-folder";
    public const string ContainingFolder = "containing-folder";
    public const string NextView = "view-next";
    public const string PreviousView = "view-previous";
    public const string NextTheme = "theme-next";
    public const string PreviousTheme = "theme-previous";
    public const string ZoomIn = "zoom-in";
    public const string ZoomOut = "zoom-out";
    public const string ZoomReset = "zoom-reset";
    public const string PrintBar = "print-bar";
    public const string Print = "print";
    public const string FullScreen = "fullscreen";
    public const string Help = "help";
    public const string Dismiss = "dismiss";

    private readonly Dictionary<string, Command> _byId;
    private readonly Dictionary<string, Command> _byCombo = new(StringComparer.Ordinal);

    public Commands(ViewerState state, ViewerActions actions, NavigationService nav)
    {
        var zoomStep = ZoomLimits.Default.Step;
        All =
        [
            new(ToggleSidebar, "Toggle sidebar", "F1 Ctrl+B", ["f1", "ctrl+b"], Sync(state.ToggleSidebar)),
            new(Back, "Back", "Alt+←", ["alt+left"], nav.BackAsync),
            new(Forward, "Forward", "Alt+→", ["alt+right"], nav.ForwardAsync),
            new(Refresh, "Refresh", "F5 Ctrl+R", ["f5", "ctrl+r"], nav.ReloadAsync),
            new(OpenFile, "Open file", "Ctrl+O", ["ctrl+o"], actions.OpenFileAsync),
            new(OpenFolder, "Open folder", "Ctrl+Shift+O", ["ctrl+shift+o"], actions.PickFolderAsync),
            new(ContainingFolder, "Open containing folder", "", [], Sync(actions.OpenContainingFolder)),
            new(NextView, "Next view", "F2", ["f2"], () => actions.CycleViewAsync(1)),
            new(PreviousView, "Previous view", "Shift+F2", ["shift+f2"], () => actions.CycleViewAsync(-1)),
            new(NextTheme, "Next document theme", "F9", ["f9"], () => actions.CycleContentThemeAsync(1)),
            new(PreviousTheme, "Previous document theme", "Shift+F9", ["shift+f9"], () => actions.CycleContentThemeAsync(-1)),
            new(ZoomIn, "Zoom the document in", "Ctrl+Wheel Ctrl++", [], () => actions.ZoomByAsync(zoomStep)),
            new(ZoomOut, "Zoom the document out", "Ctrl+-", [], () => actions.ZoomByAsync(1 / zoomStep)),
            new(ZoomReset, "Reset zoom", "Ctrl+0", [], actions.ResetZoomAsync),
            new(PrintBar, "Print…", "", [], Sync(actions.OpenPrintBar)),
            new(Print, "Print (pick a theme, then Ctrl+P again)", "Ctrl+P", ["ctrl+p"], actions.PrintShortcutAsync),
            new(FullScreen, "Fullscreen", "F11", ["f11"], Sync(actions.ToggleFullScreen)),
            new(Help, "Show this help", "?", ["?"], Sync(actions.ToggleHelp)),
            new(Dismiss, "Close popup / leave fullscreen", "Esc", ["escape"], actions.DismissAsync),
        ];

        _byId = All.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var command in All)
        {
            foreach (var combo in command.Combos) _byCombo.Add(combo, command);
        }
    }

    /// <summary>Every command, in the order the shortcuts overlay lists them.</summary>
    public IReadOnlyList<Command> All { get; }

    /// <summary>The commands that have a shortcut — what the overlay shows.</summary>
    public IEnumerable<Command> WithShortcuts => All.Where(c => c.Keys.Length > 0);

    /// <summary>The key combinations the page should report (the zoom keys it handles itself are not among them).</summary>
    public IEnumerable<string> Combos => _byCombo.Keys;

    /// <summary>Runs a command by id; an id nobody registered is a mistake in the caller.</summary>
    public Task RunAsync(string id) => _byId[id].Run();

    /// <summary>Runs the command a key combination is registered for; false when there is none.</summary>
    public bool TryRunCombo(string combo, out Task running)
    {
        if (_byCombo.TryGetValue(combo, out var command))
        {
            running = command.Run();
            return true;
        }

        running = Task.CompletedTask;
        return false;
    }

    private static Func<Task> Sync(Action action) => () =>
    {
        action();
        return Task.CompletedTask;
    };
}
