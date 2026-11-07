using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using MarkdownBlaze.Security;

namespace MarkdownBlaze.Services.Storage;

/// <summary>
/// What may be written into a mirror of a granted folder, and where. The names come from a documents
/// provider, which is another app: a name is a request, not an instruction. The rules are the same on every
/// platform (they are not <see cref="Path.GetInvalidFileNameChars"/>, which differs between Windows and Unix), so that
/// what the tests establish holds on the device.
/// </summary>
internal static class MirrorNames
{
    /// <summary>The longest name a file system of Android takes, in UTF-8 bytes.</summary>
    private const int MaxNameBytes = 255;

    /// <summary>A folder that holds thousands of third-party files and nothing a reader opens.</summary>
    private const string NodeModules = "node_modules";

    /// <summary>
    /// Whether a provider's name may be used as one segment of a path. Refused: empty, <c>.</c> and <c>..</c>,
    /// a <c>/</c> or <c>\</c> (a separator would make one name into several), a NUL or any other control
    /// character, a name that is only spaces or ends in a space or a dot (Windows would read it as something
    /// else, and a name like that is never a document), and a name too long for the file system.
    /// </summary>
    public static bool IsSafeName([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrEmpty(name) || string.Equals(name, ".", StringComparison.Ordinal) || string.Equals(name, "..", StringComparison.Ordinal)) return false;
        if (string.IsNullOrWhiteSpace(name) || name[^1] is ' ' or '.') return false;
        if (Encoding.UTF8.GetByteCount(name) > MaxNameBytes) return false;

        foreach (var c in name)
            if (c is '/' or '\\' || char.IsControl(c)) return false;

        return true;
    }

    /// <summary>
    /// The full path of <paramref name="relativePath"/> (<c>/</c>-separated) below <paramref name="root"/>, or
    /// null when any segment is not a safe name or the result is not inside the root. The second check cannot
    /// fail after the first, and is made anyway: this is the one thing standing between a provider's name and
    /// a write outside the mirror.
    /// </summary>
    /// <param name="root">An absolute path to the mirror folder.</param>
    /// <param name="relativePath">The path below it, as the provider lists it.</param>
    public static string? Resolve(string root, string? relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        if (string.IsNullOrEmpty(relativePath)) return null;

        var segments = relativePath.Split('/');
        foreach (var segment in segments)
            if (!IsSafeName(segment)) return null;

        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(segments)));
        return IsInside(fullRoot, full) ? full : null;
    }

    /// <summary>Whether <paramref name="full"/> is a path below <paramref name="fullRoot"/> (both already absolute and normalised).</summary>
    public static bool IsInside(string fullRoot, string full)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return full.Length > prefix.Length && full.StartsWith(prefix, comparison);
    }

    /// <summary>
    /// Whether the mirror goes into a folder: not a hidden one (<c>.git</c>, <c>.obsidian</c>, anything that starts
    /// with a dot), not <c>node_modules</c>, and a name that is safe to write.
    /// </summary>
    public static bool ShouldEnterFolder(string? name) =>
        IsSafeName(name) && !name.StartsWith('.') && !string.Equals(name, NodeModules, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the mirror copies a file: a safe, not hidden name; a Markdown document (the types
    /// <see cref="MarkdownService.IsSupported"/> opens) or a picture the page may show (<see cref="LocalPath.IsImage"/>);
    /// and no more than a document is read at (<see cref="MarkdownService.MaxFileBytes"/>) or a picture is shown at (<see cref="LocalPath.MaxImageBytes"/>). A size the
    /// provider does not know (zero or less) is let through and left to the copy to cap.
    /// </summary>
    public static bool ShouldMirrorFile(string? name, long size)
    {
        if (!IsSafeName(name) || name.StartsWith('.')) return false;
        if (MarkdownService.IsSupported(name)) return size <= MarkdownService.MaxFileBytes;
        return LocalPath.IsImage(name) && size <= LocalPath.MaxImageBytes;
    }

    /// <summary>
    /// A stable name for a granted folder's mirror, made from the folder's address: the same address always
    /// gives the same folder, and an address nobody has granted any more gives one nothing refers to, which is how
    /// a mirror is told to be an orphan. Only the first 8 bytes of the hash are kept; it names a folder, it does not
    /// protect anything.
    /// </summary>
    /// <param name="treeAddress">The tree's <c>content://</c> address.</param>
    public static string MirrorId(string treeAddress)
    {
        ArgumentException.ThrowIfNullOrEmpty(treeAddress);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(treeAddress));
        return "t" + Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    /// <summary>
    /// The name of the folder inside a mirror that stands for the granted folder: the folder's own name when that
    /// is safe to write, so the tree's root reads as the folder the reader chose, and <c>folder</c> when it is not.
    /// </summary>
    /// <param name="folderName">The provider's name for the granted folder.</param>
    public static string RootFolderName(string? folderName) =>
        IsSafeName(folderName) && !folderName.StartsWith('.') ? folderName : "folder";

    /// <summary>A short, safe folder name for a single file opened from <paramref name="address"/> (its own folder keeps two files of the same name apart).</summary>
    public static string SingleFolderName(string address)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(address));
        return Convert.ToHexStringLower(hash.AsSpan(0, 6));
    }
}
