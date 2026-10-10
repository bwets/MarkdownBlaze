using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkdownBlaze.Services;

public sealed class Preferences
{
    public bool SidebarPinned { get; set; } = true;
    /// <summary>
    /// The width the reader dragged the sidebar to; null until they have. The default is not a
    /// number kept here: app.js works it out, so that it is always wider than the left toolbar.
    /// </summary>
    public double? SidebarWidth { get; set; }
    public int SidebarTab { get; set; }

    /// <summary>
    /// Whether the guided tour has been through — finished or skipped, it is the same thing: the
    /// tour is offered once and never forced. Settings clears this to run it again.
    /// </summary>
    public bool TourSeen { get; set; }

    /// <summary>How many times the tour has been put off with <em>Later</em>.</summary>
    public int TourDeferrals { get; set; }

    /// <summary>
    /// How often the tour may be put off. After that it is still offered once more, but only to be
    /// taken or skipped — an offer that can be postponed for ever is a nag.
    /// </summary>
    public const int MaxTourDeferrals = 2;

    /// <summary>
    /// Set once the tour has been offered in this run of the app, so that putting it off really
    /// does put it off until the next launch. Not saved.
    /// </summary>
    [JsonIgnore] public bool TourOfferedThisRun { get; set; }

    /// <summary>Documents opened so far — counted only until the rating request has had its answer.</summary>
    public int DocumentsOpened { get; set; }

    /// <summary>
    /// The round of <see cref="RatingPrompt"/> the reader has answered or dismissed; 0 for none.
    /// </summary>
    public int RatingAnswered { get; set; }
    public string ThemeMode { get; set; } = "Dark"; // System | Light | Dark

    /// <summary>Default skin for the rendered document — an id from <see cref="ContentThemes"/>.</summary>
    public string ContentTheme { get; set; } = ContentThemes.Auto;

    /// <summary>How the document is laid out: "continuous" (one flowing column) or "page" (sheets).</summary>
    public string ViewMode { get; set; } = ViewModes.Continuous;

    /// <summary>In page view, what a sheet is sized against: "width" or "page".</summary>
    public string PageFit { get; set; } = ViewModes.FitWidth;

    /// <summary>Reader's zoom on the document itself; 1 is natural size. The app never scales.</summary>
    public double DocumentZoom { get; set; } = 1;

    // Window placement. Width/Height/X/Y are the *restored* (non-maximized) bounds; the app starts
    // maximized by default and on first run (no saved bounds). X/Y null → center on screen.
    public bool WindowMaximized { get; set; } = true;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 860;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
}

/// <summary>Persists user preferences to the local app-data folder (shared with the desktop app).</summary>
public sealed class PreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public PreferencesStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkdownBlaze");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "preferences.json");
        Current = Load();
    }

    public Preferences Current { get; }

    public void Save()
    {
        try { File.WriteAllText(_filePath, JsonSerializer.Serialize(Current, JsonOptions)); }
        catch { /* best-effort */ }
    }

    private Preferences Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var prefs = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(_filePath));
                if (prefs is not null) return prefs;
            }
        }
        catch { /* corrupt prefs are non-fatal */ }
        return new Preferences();
    }
}
