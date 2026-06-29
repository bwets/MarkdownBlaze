namespace MarkdownBlaze;

/// <summary>Prototype-only file logging so we can see what happens behind the WebView.</summary>
public static class Diag
{
    public static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "MarkdownBlaze-photino.log");

    public static readonly string PreviewPath =
        Path.Combine(Path.GetTempPath(), "MarkdownBlaze-photino-preview.html");

    public static void Reset()
    {
        try { File.WriteAllText(LogPath, ""); } catch { /* ignore */ }
    }

    public static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}"); }
        catch { /* ignore */ }
    }
}
