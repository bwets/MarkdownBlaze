using System.Text.Json;

namespace MarkdownBlaze.Services;

public sealed class Preferences
{
    public bool SidebarPinned { get; set; } = true;
    public double SidebarWidth { get; set; } = 280;
    public int SidebarTab { get; set; }
    public string ThemeMode { get; set; } = "Dark"; // System | Light | Dark
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
