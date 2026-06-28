namespace bwets.Markdig.Extensions.Html;

/// <summary>Reads files embedded in this assembly's <c>Assets</c> folder.</summary>
internal static class EmbeddedAsset
{
    public static string Read(string fileName)
    {
        var assembly = typeof(EmbeddedAsset).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (name == null)
        {
            return string.Empty;
        }

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream == null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
