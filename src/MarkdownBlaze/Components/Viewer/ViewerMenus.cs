using MarkdownBlaze.Models;
using MarkdownBlaze.Platform;
using MarkdownBlaze.Services;
using Microsoft.AspNetCore.Components.Web;

namespace MarkdownBlaze.Components.Viewer;

/// <summary>
/// Builds the popup menus: the toolbar's view and document-theme choices, the More menu, and the menu a
/// right click opens on a document. A menu is a <see cref="ContextMenuModel"/> put into
/// <see cref="ViewerState"/>; the <c>ContextMenu</c> component draws it.
/// </summary>
internal sealed class ViewerMenus(ViewerState state, ViewerActions actions, NavigationService nav, IShellLauncher shell)
{
    /// <summary>
    /// Anchored under the button that opened it rather than to the right edge like the More menu:
    /// the zoom control and two more buttons sit between this one and the edge of the window.
    /// </summary>
    public void OpenView(MouseEventArgs e) => state.OpenMenu(new ContextMenuModel(
        "View",
        [
            ViewChoice("Continuous", ViewMode.Continuous, state.PageFit),
            ViewChoice("Pages — full width", ViewMode.Page, PageFit.Width),
            ViewChoice("Pages — whole page", ViewMode.Page, PageFit.Page),
        ],
        Right: false,
        X: Math.Max(8, e.ClientX - 24)));

    /// <summary>
    /// Anchored to the right edge, like the More menu beside it: this button sits at the end of
    /// the toolbar, so a menu opened at the pointer would hang off the side of the window.
    /// </summary>
    public void OpenTheme() => state.OpenMenu(new ContextMenuModel(
        "Document theme",
        [.. ContentThemes.All.Select(ThemeChoice)],
        Right: true));

    public void OpenMore() => state.OpenMenu(new ContextMenuModel(
        "More",
        [
            new("Keyboard shortcuts", Sync(() => state.SetShortcutsOpen(open: true))),
            new("What's new", Sync(actions.OpenWhatsNew)),
            new("Settings", Sync(state.RequestSettings)),
        ],
        Right: true));

    public void OpenRow(MouseEventArgs e, string path) => state.OpenMenu(new ContextMenuModel(
        "Document",
        [
            new("Open", () => nav.NavigateAsync(path, anyType: true)),
            new("Open in new window", Sync(() => shell.OpenInNewWindow(path))),
            new("Copy filename", () => actions.CopyNameAsync(path)),
            new("Open containing folder", Sync(() => shell.RevealInFileManager(path))),
        ],
        Right: false,
        X: e.ClientX,
        Y: e.ClientY));

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
