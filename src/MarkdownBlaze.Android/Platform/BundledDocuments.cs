using Android.Content;

namespace MarkdownBlaze.Platform;

/// <summary>
/// Puts the documents that ship with the app (the manual, the change log, the notices) where a path can reach them.
/// The APK holds them as assets, which only a stream can read, and the app opens a document by its path, so
/// they are copied into a folder of the app's own, and <c>AppPaths.ConfigureBundledFolder</c> points the
/// renderer there. They are copied at every start: they are a few kilobytes, and a copy made by an older
/// version of the app can then never be what a newer one shows.
/// </summary>
internal static class BundledDocuments
{
    /// <summary>The folder inside the assets the documents are packed under (see the project file).</summary>
    private const string AssetFolder = "bundled";

    /// <summary>The asset names, also the file names (the manual links to the notices beside it).</summary>
    private static readonly string[] Names = ["UserManual.md", "WhatsNew.md", "THIRD-PARTY-NOTICES.md"];

    /// <summary>Copies the documents into <paramref name="folder"/>, which is created if it is not there.</summary>
    /// <param name="context">What the assets are read through.</param>
    /// <param name="folder">An absolute path inside the app's own storage.</param>
    /// <exception cref="InvalidOperationException">The app has no assets.</exception>
    public static void CopyTo(Context context, string folder)
    {
        ArgumentNullException.ThrowIfNull(context);

        _ = Directory.CreateDirectory(folder);
        var assets = context.Assets ?? throw new InvalidOperationException("The app has no assets.");
        foreach (var name in Names)
        {
            using var source = assets.Open(AssetFolder + "/" + name);
            using var target = File.Create(Path.Combine(folder, name));
            source.CopyTo(target);
        }
    }
}
