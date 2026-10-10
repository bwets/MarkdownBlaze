using System.Collections.Frozen;
using System.Globalization;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Security;

/// <summary>
/// The one place that decides whether a path taken from a document may be touched on disk, and what
/// of it may be shown. A document is not trusted, so a path in it is a request, not an instruction:
/// <c>![](../../.ssh/id_rsa)</c> must not put a private key in the page, and
/// <c>![](//attacker/share/x.png)</c> must not make Windows open an SMB session — which authenticates
/// with the reader's NTLM credentials — merely because the document was opened.
/// </summary>
internal static partial class LocalPath
{
    private static readonly ILogger Logger = Log.For(nameof(LocalPath));

    /// <summary>The only kinds of file that are shown as images. Never <c>application/octet-stream</c>.</summary>
    public static readonly FrozenSet<string> ImageExtensions =
        new[] { ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".bmp", ".ico", ".avif" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The largest single image that is shown.</summary>
    public const long MaxImageBytes = 20L * 1024 * 1024;

    /// <summary>The most image data one document may show, all its images together (checked when it is rendered).</summary>
    public const long MaxDocumentImageBytes = 64L * 1024 * 1024;

    /// <summary>
    /// True for a UNC path (<c>\\server\share</c>, <c>//server/share</c>) or a device path
    /// (<c>\\?\C:\</c>, <c>\\.\pipe\x</c>), whichever way the separators are written. Both separators
    /// are accepted on Windows, so <c>\/</c> and <c>/\</c> count as well.
    /// </summary>
    public static bool IsUncOrDevice(string? path) =>
        path is { Length: >= 2 } && IsSeparator(path[0]) && IsSeparator(path[1]);

    /// <summary>
    /// Whether <paramref name="path"/> is a plain local path: not empty, no control characters, and not a
    /// UNC or device path. It says nothing about whether the path exists or what it holds.
    /// </summary>
    public static bool IsSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (IsUncOrDevice(path.TrimStart())) return false;
        foreach (var c in path)
            if (char.IsControl(c)) return false; // NUL and friends truncate or confuse the Win32 layer

        return true;
    }

    /// <summary>
    /// The server of a UNC path (<c>\\server\share\x</c> → <c>server</c>), or null when
    /// <paramref name="path"/> is not one — a device path (<c>\\?\</c>, <c>\\.\</c>) has no server.
    /// </summary>
    public static string? UncServer(string? path)
    {
        if (!IsUncOrDevice(path) || path!.Length < 3 || path[2] is '?' or '.' || IsSeparator(path[2])) return null;
        var end = path.AsSpan(2).IndexOfAny('/', '\\');
        return end < 0 ? path[2..] : path.Substring(2, end);
    }

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>The media type of an image extension, matched without regard to case.</summary>
    private static readonly FrozenDictionary<string, string> MediaTypes = CreateMediaTypes();

    private static FrozenDictionary<string, string> CreateMediaTypes()
    {
        KeyValuePair<string, string>[] types =
        [
            new(".png", "image/png"),
            new(".jpg", "image/jpeg"),
            new(".jpeg", "image/jpeg"),
            new(".gif", "image/gif"),
            new(".svg", "image/svg+xml"),
            new(".webp", "image/webp"),
            new(".bmp", "image/bmp"),
            new(".ico", "image/x-icon"),
            new(".avif", "image/avif"),
        ];
        return types.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public static string MimeFor(string extension) =>
        MediaTypes.TryGetValue(extension, out var mediaType)
            ? mediaType
            : throw new ArgumentOutOfRangeException(nameof(extension), extension, "Not an embeddable image type.");

    /// <summary>
    /// Resolves <paramref name="decodedPath"/> (already percent-decoded) against
    /// <paramref name="baseDir"/>. Null when the path is not safe, or when the result is a device or
    /// UNC path after all — the Win32 layer maps names like <c>CON.png</c> to <c>\\.\CON</c>, which
    /// <see cref="Path.GetFullPath(string)"/> reveals.
    /// </summary>
    public static string? Resolve(string decodedPath, string baseDir)
    {
        if (!IsSafe(decodedPath)) return null;
        try
        {
            var full = Path.IsPathRooted(decodedPath)
                ? Path.GetFullPath(decodedPath)
                : Path.GetFullPath(Path.Combine(baseDir, decodedPath));
            if (IsUncOrDevice(full)) return null;
            // Win32 still treats CON, NUL, COM1 ... as devices wherever they appear in a path, with or
            // without an extension; opening one can hang the render waiting for input.
            if (OperatingSystem.IsWindows() && IsReservedDeviceName(Path.GetFileName(full))) return null;
            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            LogPathRejected(Logger, ex, decodedPath);
            return null;
        }
    }

    /// <summary>What happened when an image was asked for.</summary>
    internal enum ImageOutcome
    {
        /// <summary>Allowed, and the stream is open.</summary>
        Allowed = 0,

        /// <summary>No such file (or it could not be read). Not a policy decision.</summary>
        Missing = 1,

        /// <summary>Refused: unsafe path, or not an image type.</summary>
        Blocked = 2,

        /// <summary>Refused: over the size cap for one image or for the document.</summary>
        TooLarge = 3,
    }

    /// <summary>
    /// Opens a local image for reading, subject to every rule above. This is the one decision about
    /// which images a document may show: the renderer asks it, once per image, to decide between a
    /// picture and a placeholder, and the <c>mdasset://</c> handler (<see cref="AssetHandler"/>) asks it
    /// again for every request the page makes — so the two can never disagree about what is allowed.
    /// </summary>
    /// <param name="decodedPath">The path as written in the document, percent-decoded.</param>
    /// <param name="baseDir">
    /// The folder relative paths are resolved against; empty for text that has none (dropped onto the
    /// window), in which case only rooted paths count — a relative one must not be resolved against
    /// whatever the process's working directory happens to be.
    /// </param>
    /// <param name="maxImageBytes">Cap for this one image.</param>
    /// <param name="remainingDocumentBytes">What is left of the document's image budget.</param>
    /// <param name="stream">The open image, when the outcome is <see cref="ImageOutcome.Allowed"/>; the caller disposes it.</param>
    /// <param name="mediaType">The image's media type, when the outcome is <see cref="ImageOutcome.Allowed"/>.</param>
    public static ImageOutcome TryOpenImage(
        string decodedPath,
        string baseDir,
        long maxImageBytes,
        long remainingDocumentBytes,
        out FileStream? stream,
        out string? mediaType)
    {
        stream = null;
        mediaType = null;

        if (baseDir.Length == 0 && !Path.IsPathRooted(decodedPath)) return ImageOutcome.Missing;

        var full = Resolve(decodedPath, baseDir);
        // Decided: an image anywhere on disk is shown, as long as it is an image. Confining images to the
        // document's own folder tree would break wikis that share an image folder through "../". What
        // keeps this safe is the rest of the checks: the extension allow-list, the refusal of UNC and
        // device paths, the size caps, and that only the render on screen (its token) can ask at all.
        if (full is null || !IsImage(full)) return ImageOutcome.Blocked;

        FileStream? opened = null;
        try
        {
            // FileShare.ReadWrite|Delete: an editor that has the image open must not make it unreadable.
            opened = new FileStream(
                full,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                FileOptions.SequentialScan);
            if (opened.Length > maxImageBytes || opened.Length > remainingDocumentBytes)
            {
                opened.Dispose();
                return ImageOutcome.TooLarge;
            }

            stream = opened;
            mediaType = MimeFor(Path.GetExtension(full));
            return ImageOutcome.Allowed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            opened?.Dispose();
            LogImageUnreadable(Logger, ex, full);
            return ImageOutcome.Missing; // not there, locked, a directory, a device that is not a file
        }
    }

    /// <summary>
    /// What <see cref="TryOpenImage"/> says about an image, without keeping it open: the renderer only
    /// needs to know whether the picture will be shown, and how much of the document's budget it takes.
    /// </summary>
    /// <param name="decodedPath">The path as written in the document, percent-decoded.</param>
    /// <param name="baseDir">The folder relative paths are resolved against (see <see cref="TryOpenImage"/>).</param>
    /// <param name="maxImageBytes">Cap for this one image.</param>
    /// <param name="remainingDocumentBytes">What is left of the document's image budget.</param>
    /// <param name="length">The image's size in bytes, when the outcome is <see cref="ImageOutcome.Allowed"/>.</param>
    public static ImageOutcome Check(string decodedPath, string baseDir, long maxImageBytes, long remainingDocumentBytes, out long length)
    {
        FileStream? stream = null;
        try
        {
            var outcome = TryOpenImage(decodedPath, baseDir, maxImageBytes, remainingDocumentBytes, out stream, out _);
            length = stream?.Length ?? 0;
            return outcome;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private static readonly FrozenSet<string> ReservedDeviceNames =
        new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }
            .Concat(Enumerable.Range(1, 9)
                .Select(n => n.ToString(CultureInfo.InvariantCulture))
                .SelectMany(n => new[] { "COM" + n, "LPT" + n }))
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsReservedDeviceName(string fileName)
    {
        var dot = fileName.IndexOf('.', StringComparison.Ordinal);
        var stem = (dot < 0 ? fileName : fileName[..dot]).TrimEnd(' ');
        return ReservedDeviceNames.Contains(stem);
    }

    private static bool IsSeparator(char c) => c is '/' or '\\';

    [LoggerMessage(Level = LogLevel.Debug, Message = "The image path {Path} cannot be resolved; the image is not shown")]
    private static partial void LogPathRejected(ILogger log, Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The image {Path} could not be read; the image is not shown")]
    private static partial void LogImageUnreadable(ILogger log, Exception ex, string path);
}
