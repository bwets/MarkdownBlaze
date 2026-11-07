using MarkdownBlaze.Services.Storage;

namespace MarkdownBlaze.Platform;

/// <summary>A folder the reader has granted, with where its mirror is.</summary>
/// <param name="Uri">The tree address Android persisted the grant under.</param>
/// <param name="Id">The mirror's folder name, made from the address (<see cref="MirrorNames.MirrorId"/>).</param>
/// <param name="RootDocumentId">The provider's id for the granted folder itself, where a listing starts.</param>
/// <param name="Location">Where the folder is on the shared storage, when its provider says (the external-storage provider does); null otherwise.</param>
/// <param name="Folder">The mirror of this folder: <c>mirror/&lt;Id&gt;</c>, which holds the copy and the scratch folder.</param>
internal sealed record TreeGrant(Android.Net.Uri Uri, string Id, string RootDocumentId, StorageLocation? Location, string Folder)
{
    /// <summary>The folder the granted folder's files are copied into, and the tree's root in the viewer: the one folder in <see cref="Folder"/> that is not the scratch folder.</summary>
    public string Root
    {
        get
        {
            if (Directory.Exists(Folder))
            {
                foreach (var directory in Directory.EnumerateDirectories(Folder))
                {
                    if (!Path.GetFileName(directory).StartsWith('.')) return directory;
                }
            }

            return Path.Combine(Folder, MirrorNames.RootFolderName(Location?.Name));
        }
    }

    /// <summary>Where files being copied wait until they are complete (hidden, so it is never the tree's root).</summary>
    public string Scratch => Path.Combine(Folder, ".tmp");

    /// <summary>How the reader would know the folder: its path on the storage, or its name in the mirror.</summary>
    public string Label => Location?.Label ?? Path.GetFileName(Root);
}
