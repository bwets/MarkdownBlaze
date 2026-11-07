namespace MarkdownBlaze.Platform;

/// <summary>
/// What the viewer asks of the window it is in, apart from showing the page: only full screen, today. A
/// host whose window has no such mode answers "not full screen" and does nothing.
/// </summary>
internal interface IWindowControl
{
    /// <summary>True while the window is borderless fullscreen.</summary>
    bool IsFullScreen { get; }

    /// <summary>Enters full screen, or leaves it when it is on.</summary>
    void ToggleFullScreen();

    /// <summary>Leaves full screen if the window is in it; returns false when there was nothing to do.</summary>
    bool ExitFullScreen();
}
