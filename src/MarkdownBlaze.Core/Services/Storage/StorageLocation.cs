namespace MarkdownBlaze.Services.Storage;

/// <summary>
/// Where a document or a folder is on the device's shared storage: the volume (<c>primary</c> for the
/// built-in storage, the card's id for an SD card) and the path below it, written with <c>/</c> and without
/// a leading or trailing slash (empty for the volume's top). This is what the documents providers of Android
/// call their ids; it is all that is needed to tell whether a document lies inside a folder the reader has
/// granted, and it is worked out from text alone so that it can be tested without a device.
/// </summary>
internal sealed record StorageLocation
{
    private StorageLocation(string volume, string path)
    {
        Volume = volume;
        Path = path;
    }

    /// <summary><c>primary</c>, or the identifier of a removable volume.</summary>
    public string Volume { get; }

    /// <summary>The path below the volume, <c>/</c>-separated; empty for the volume's top.</summary>
    public string Path { get; }

    /// <summary>The last segment of the path (the volume's name for its top).</summary>
    public string Name => Path.Length == 0 ? Volume : Path[(Path.LastIndexOf('/') + 1)..];

    /// <summary>The folder this one is in, or null for the volume's top.</summary>
    public StorageLocation? Parent => Path.Length == 0
        ? null
        : new StorageLocation(Volume, Path.Contains('/', StringComparison.Ordinal) ? Path[..Path.LastIndexOf('/')] : "");

    /// <summary>
    /// Makes a location from a volume and a path, cleaning the path up. Null when it cannot be one: no
    /// volume, a NUL, or a <c>.</c> or <c>..</c> segment — a provider's id is text somebody else chose, and
    /// a path that steps upwards is never one this app has reason to follow.
    /// </summary>
    public static StorageLocation? Create(string? volume, string? path)
    {
        if (string.IsNullOrWhiteSpace(volume) || volume.Contains('\0', StringComparison.Ordinal)) return null;

        var segments = new List<string>();
        foreach (var segment in (path ?? "").Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsDotSegment(segment) || segment.Contains('\0', StringComparison.Ordinal)) return null;
            segments.Add(segment);
        }

        return new StorageLocation(volume, string.Join('/', segments));
    }

    private static bool IsDotSegment(string segment) =>
        string.Equals(segment, ".", StringComparison.Ordinal) || string.Equals(segment, "..", StringComparison.Ordinal);

    /// <summary>
    /// Whether <paramref name="other"/> is on the same volume and below this folder. A location is not inside
    /// itself. Names compare without regard to case, as the shared storage's file system does.
    /// </summary>
    public bool Contains(StorageLocation other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!string.Equals(Volume, other.Volume, StringComparison.OrdinalIgnoreCase)) return false;
        if (Path.Length == 0) return other.Path.Length > 0;
        return other.Path.Length > Path.Length
            && other.Path.StartsWith(Path, StringComparison.OrdinalIgnoreCase)
            && other.Path[Path.Length] == '/';
    }

    /// <summary>The path of <paramref name="other"/> below this folder, or null when it is not inside it.</summary>
    public string? RelativePathOf(StorageLocation other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!Contains(other)) return null;
        return Path.Length == 0 ? other.Path : other.Path[(Path.Length + 1)..];
    }

    /// <summary>How the folder is named to the reader: <c>Documents/wiki</c>, with the volume added for a card.</summary>
    public string Label
    {
        get
        {
            if (Path.Length == 0) return Volume;
            return string.Equals(Volume, StorageUris.PrimaryVolume, StringComparison.OrdinalIgnoreCase) ? Path : Path + " (" + Volume + ")";
        }
    }

    /// <summary>The document id the external-storage provider uses for this location (<c>primary:Documents/wiki</c>).</summary>
    public string DocumentId => Volume + ":" + Path;
}
