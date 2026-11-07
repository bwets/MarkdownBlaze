namespace MarkdownBlaze.Services.Storage;

/// <summary>Why a walk stopped before it had seen everything.</summary>
internal enum MirrorCap
{
    /// <summary>It did not.</summary>
    None = 0,

    /// <summary>The folder holds more entries than <see cref="MirrorWalker.MaxEntries"/>.</summary>
    Entries = 1,

    /// <summary>The files to mirror add up to more than <see cref="MirrorWalker.MaxBytes"/>.</summary>
    Size = 2,
}
