using System.Text.Json.Serialization;
using MarkdownBlaze.Services;
using Microsoft.FluentUI.AspNetCore.Components;

namespace MarkdownBlaze.Models;

/// <summary>
/// What the app remembers between launches, as <c>preferences.json</c>. The choices that are one of a few
/// are enums (<see cref="ThemeMode"/>, <see cref="Models.ViewMode"/>, <see cref="Models.PageFit"/>) written
/// as the same strings the file has always held; see <see cref="PreferenceConverters"/>.
/// </summary>
internal sealed class Preferences
{
    public bool SidebarPinned { get; set; } = true;

    /// <summary>
    /// The width the reader dragged the sidebar to; null until they have. The default is not a
    /// number kept here: js/sidebar.js works it out, so that it is always wider than the left toolbar.
    /// </summary>
    public double? SidebarWidth { get; set; }
    public int SidebarTab { get; set; }

    /// <summary>
    /// Whether the guided tour has been through — finished or skipped, it is the same thing: the
    /// tour is offered once and never forced. Settings clears this to run it again.
    /// </summary>
    public bool TourSeen { get; set; }

    /// <summary>How many times the tour has been put off with <em>Later</em> (the limit is in <c>ViewerActions</c>, with the tour).</summary>
    public int TourDeferrals { get; set; }

    /// <summary>Documents opened so far — counted only until the rating request has had its answer.</summary>
    public int DocumentsOpened { get; set; }

    /// <summary>
    /// The round of <see cref="RatingPrompt"/> the reader has answered or dismissed; 0 for none.
    /// </summary>
    public int RatingAnswered { get; set; }

    /// <summary>The app theme: System | Light | Dark. A new install follows the system.</summary>
    [JsonConverter(typeof(PreferenceConverters.ThemeModeConverter))]
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    /// <summary>
    /// Default skin for the rendered document — an id from <see cref="ContentThemes"/>. It stays an id
    /// rather than an enum because the set is open (each id is a block of CSS, and a theme is added by
    /// adding both); <see cref="PreferencesStore"/> checks it with <see cref="ContentThemes.Resolve"/> when it loads.
    /// </summary>
    public string ContentTheme { get; set; } = ContentThemes.Auto;

    /// <summary>How the document is laid out: "continuous" (one flowing column) or "page" (sheets).</summary>
    [JsonConverter(typeof(PreferenceConverters.ViewModeConverter))]
    public ViewMode ViewMode { get; set; } = ViewMode.Continuous;

    /// <summary>In page view, what a sheet is sized against: "width" or "page".</summary>
    [JsonConverter(typeof(PreferenceConverters.PageFitConverter))]
    public PageFit PageFit { get; set; } = PageFit.Width;

    /// <summary>Reader's zoom on the document itself; 1 is natural size. The app never scales.</summary>
    public double DocumentZoom { get; set; } = 1;

    /// <summary>
    /// Window placement. Width/Height/X/Y are the *restored* (non-maximized) bounds; the app starts
    /// maximized by default and on first run (no saved bounds). X/Y null → center on screen.
    /// </summary>
    public bool WindowMaximized { get; set; } = true;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 860;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
}
