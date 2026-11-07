using Android.Content;
using Android.Database;

namespace MarkdownBlaze.Platform;

/// <summary>
/// The two things every call to a documents provider has in common: asking it for rows, and what it can throw back. A provider
/// is another app, so a failure is part of the answer and not a bug here.
/// </summary>
internal static class Providers
{
    /// <summary>Asks a provider for the columns of the rows behind an address.</summary>
    /// <param name="resolver">What queries providers.</param>
    /// <param name="uri">The address of a document or of a folder's children.</param>
    /// <param name="columns">The columns to read, in the order the caller reads them.</param>
    /// <returns>The cursor, which the caller disposes; null when the provider has no answer.</returns>
    public static ICursor? Query(ContentResolver resolver, Android.Net.Uri? uri, string[] columns) =>
        uri is null ? null : resolver.Query(uri, columns, selection: null, selectionArgs: null, sortOrder: null);

    /// <summary>
    /// What a provider can throw at a caller: a Java exception of any kind (a revoked grant is a
    /// <c>SecurityException</c>, a dead provider a <c>RemoteException</c>, an id it dislikes an
    /// <c>IllegalArgumentException</c>), or the .NET ones the binding raises for I/O.
    /// </summary>
    /// <param name="ex">What was thrown.</param>
    public static bool IsFailure(Exception ex) => ex is Java.Lang.Throwable or IOException or UnauthorizedAccessException;
}
