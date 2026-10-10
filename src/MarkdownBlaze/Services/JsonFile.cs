using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Services;

/// <summary>
/// Reading and writing the small JSON files the app keeps (preferences, history) so that neither a
/// crash nor a damaged file costs the reader their data.
/// <para>
/// A file is never written in place: the new content goes to <c>&lt;file&gt;.tmp</c> first and replaces
/// the old file in one move, so that dying half-way leaves either the old file or the new one —
/// never an empty or half-written one, which <see cref="Load{T}"/> would have had to throw away.
/// </para>
/// </summary>
internal static partial class JsonFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Reads <paramref name="path"/>. Null when there is no file, when it cannot be read, or when what is
    /// in it is not a <typeparamref name="T"/>. A file that is there but damaged is not thrown away: it is
    /// renamed to <c>&lt;file&gt;.corrupt</c> so there is something to look at, and the reader starts over.
    /// </summary>
    /// <typeparam name="T">The type the file holds.</typeparam>
    public static T? Load<T>(string path, ILogger log)
        where T : class
    {
        string json;
        try
        {
            if (!File.Exists(path)) return null;
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogUnreadable(log, ex, path);
            return null;
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(json, Options);
            if (value is not null) return value;
        }
        catch (JsonException ex)
        {
            Quarantine(path, log, ex.Message);
            return null;
        }

        Quarantine(path, log, "the file holds null");
        return null;
    }

    /// <summary>
    /// Writes <paramref name="value"/> to <paramref name="path"/> atomically (see the class remarks).
    /// Returns whether it worked; a failure is logged and leaves the previous file as it was.
    /// </summary>
    /// <typeparam name="T">The type of value written.</typeparam>
    public static bool Save<T>(string path, T value, ILogger log)
    {
        var temp = path + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true); // on the disk before it replaces the file that is there
            }

            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(log, ex, path);
            return false;
        }
    }

    private static void Quarantine(string path, ILogger log, string reason)
    {
        var kept = path + ".corrupt";
        try
        {
            File.Move(path, kept, overwrite: true);
            LogCorrupt(log, path, reason, kept);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCorruptNotKept(log, ex, path, reason);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {Path}; starting from the defaults")]
    private static partial void LogUnreadable(ILogger log, Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path} is damaged ({Reason}); kept as {Kept} and starting from the defaults")]
    private static partial void LogCorrupt(ILogger log, string path, string reason, string kept);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path} is damaged ({Reason}) and could not be set aside; starting from the defaults")]
    private static partial void LogCorruptNotKept(ILogger log, Exception ex, string path, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save {Path}; the previous file is untouched")]
    private static partial void LogSaveFailed(ILogger log, Exception ex, string path);
}
