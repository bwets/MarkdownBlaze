namespace MarkdownBlaze.Services;

/// <summary>
/// The one fact the <c>mdasset://</c> handler needs from the rest of the app: which render's images are
/// allowed to be fetched, and from where. <see cref="NavigationService"/> sets it when a render is put on
/// the page — and clears it when the page shows something with no images — and the handler reads it.
/// Nothing else touches it.
/// <para>
/// The handler is called by the WebView, on whatever thread the native side chooses, while the
/// navigation service works on the UI thread. The grant is an immutable object swapped in one
/// reference write, so a reader sees either the old grant or the new one, never a token from one and a
/// folder from the other.
/// </para>
/// </summary>
internal sealed class AssetScope
{
    private AssetGrant? _current;

    /// <summary>The grant in force, or null when the page shows nothing that may fetch an image.</summary>
    public AssetGrant? Current => Volatile.Read(ref _current);

    /// <summary>Makes <paramref name="grant"/> the only one in force (null for none): the previous document's images stop resolving at once.</summary>
    public void Set(AssetGrant? grant) => Volatile.Write(ref _current, grant);

    /// <summary>Withdraws the grant.</summary>
    public void Clear() => Set(grant: null);

    /// <summary>The folder for <paramref name="token"/>, or false when it is not the token of the render on the page.</summary>
    public bool TryGetBaseFolder(string token, out string baseFolder)
    {
        var grant = Current;
        if (grant is not null && string.Equals(grant.Token, token, StringComparison.Ordinal))
        {
            baseFolder = grant.BaseFolder;
            return true;
        }

        baseFolder = string.Empty;
        return false;
    }
}
