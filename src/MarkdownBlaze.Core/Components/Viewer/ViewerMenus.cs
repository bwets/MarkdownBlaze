using MarkdownBlaze.Models;
using MarkdownBlaze.Platform;
using MarkdownBlaze.Services;
using Microsoft.AspNetCore.Components.Web;

namespace MarkdownBlaze.Components.Viewer;

/// <summary>
/// Builds the popup menus: the toolbar's view and document-theme choices, the More menu, and the menu a
/// right click (or a long press) opens on a document. A menu is a <see cref="ContextMenuModel"/> put into
/// <see cref="ViewerState"/>; the <c>ContextMenu</c> component draws it.
/// </summary>
internal sealed class ViewerMenus(ViewerState state, ViewerActions actions, Commands commands, NavigationService nav, IShellLauncher shell)
{
    /// <summary>
    /// Anchored under the button that opened it rather than to the right edge like the More menu:
    /// the zoom control and two more buttons sit between this one and the edge of the window. In a narrow
    /// window the buttons are crowded to the right, and the menu goes to the edge like the others.
    /// </summary>
    public void OpenView(MouseEventArgs e) => state.OpenMenu(new ContextMenuModel(
        "View",
        [
            ViewChoice("Continuous", ViewMode.Continuous, state.PageFit),
            ViewChoice("Pages — full width", ViewMode.Page, PageFit.Width),
            ViewChoice("Pages — whole page", ViewMode.Page, PageFit.Page),
        ],
        Right: state.Narrow,
        X: Math.Max(8, e.ClientX - 24)));

    /// <summary>
    /// Anchored to the right edge, like the More menu beside it: this button sits at the end of
    /// the toolbar, so a menu opened at the pointer would hang off the side of the window.
    /// </summary>
    public void OpenTheme() => state.OpenMenu(new ContextMenuModel(
        "Document theme",
        [.. ContentThemes.All.Select(ThemeChoice)],
        Right: true));

    /// <summary>
    /// In a narrow window the toolbar keeps the sidebar, the way back and forth, the view, the theme and this
    /// button, so what the rest of the toolbar does — the files, printing, zoom — is in this menu instead.
    /// </summary>
    public void OpenMore()
    {
        List<MenuItem> items = [];
        if (state.Narrow)
        {
            items.Add(new("Open file", () => commands.RunAsync(Commands.OpenFile)));
            items.Add(new("Open folder", () => commands.RunAsync(Commands.OpenFolder)));
            items.Add(new("Refresh", () => commands.RunAsync(Commands.Refresh)));
            items.Add(new("Print…", () => commands.RunAsync(Commands.PrintBar)));
            if (shell.CanRevealInFileManager) items.Add(new("Open containing folder", () => commands.RunAsync(Commands.ContainingFolder)));
            items.Add(new("Zoom in", () => commands.RunAsync(Commands.ZoomIn)));
            items.Add(new("Zoom out", () => commands.RunAsync(Commands.ZoomOut)));
            items.Add(new("Reset zoom", () => commands.RunAsync(Commands.ZoomReset)));
        }

        // The keys have no use on a device that has none (and come back with one: the window says which it is).
        if (!state.NoKeyboard) items.Add(new("Keyboard shortcuts", Sync(() => state.SetShortcutsOpen(open: true))));
        items.Add(new("What's new", Sync(actions.OpenWhatsNew)));
        items.Add(new("Settings", Sync(state.RequestSettings)));

        state.OpenMenu(new ContextMenuModel("More", items, Right: true));
    }

    public void OpenRow(MouseEventArgs e, string path)
    {
        List<MenuItem> items =
        [
            new(
                "Open",
                () =>
                {
                    state.CloseDrawer();
                    return nav.NavigateAsync(path, anyType: true);
                }),
        ];
        if (shell.CanOpenInNewWindow) items.Add(new("Open in new window", Sync(() => shell.OpenInNewWindow(path))));
        items.Add(new("Copy filename", () => actions.CopyNameAsync(path)));
        if (shell.CanRevealInFileManager) items.Add(new("Open containing folder", Sync(() => shell.RevealInFileManager(path))));

        state.OpenMenu(new ContextMenuModel("Document", items, Right: false, X: e.ClientX, Y: e.ClientY));
    }

    private MenuItem ViewChoice(string label, ViewMode mode, PageFit fit) =>
        new(label, () => actions.SetViewAsync(mode, fit), state.IsView(mode, fit));

    private MenuItem ThemeChoice(ContentTheme theme) =>
        new(theme.Label, () => actions.SetContentThemeAsync(theme.Id), state.IsContentTheme(theme.Id));

    private static Func<Task> Sync(Action action) => () =>
    {
        action();
        return Task.CompletedTask;
    };
}
