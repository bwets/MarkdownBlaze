using System.Runtime.InteropServices;

namespace MarkdownBlaze.Platform.Windows;

/// <summary>
/// Whether the process runs from an MSIX package (the Microsoft Store build). A packaged app has its own
/// registration — the manifest declares the file types and the <c>markdown</c> protocol — and the registry
/// writes it makes under HKEY_CURRENT_USER\Software\Classes go to the package's private hive, where
/// Explorer never sees them. So the app does not try to register itself when packaged.
/// </summary>
internal static partial class PackageIdentity
{
    /// <summary>What <c>GetCurrentPackageFullName</c> answers when the process has no package identity.</summary>
    internal const int AppModelErrorNoPackage = 15700;

    /// <summary>What it answers for a packaged process when the buffer it was given is too small — which a null buffer always is.</summary>
    internal const int ErrorInsufficientBuffer = 122;

    /// <summary>What it answers for a packaged process when the buffer was big enough (never, here).</summary>
    private const int ErrorSuccess = 0;

    private static readonly Lazy<bool> s_isPackaged = new(Query);

    /// <summary>True when running from an MSIX package; false when unpackaged, and on any other platform.</summary>
    public static bool IsPackaged => s_isPackaged.Value;

    /// <summary>Whether the answer <c>GetCurrentPackageFullName</c> gave is that of a packaged process.</summary>
    internal static bool IsPackagedResult(int result) =>
        result is ErrorSuccess or ErrorInsufficientBuffer;

    // kernel32 is looked for in System32 only, never next to the executable or in the current directory.
    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentPackageFullName")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, nint packageFullName);

    private static bool Query()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            // The name itself is not wanted, only whether there is one: a length of zero and no buffer
            // makes a packaged process answer "buffer too small" and an unpackaged one "no package".
            uint length = 0;
            return IsPackagedResult(GetCurrentPackageFullName(ref length, nint.Zero));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false; // Windows before 8 has no packages
        }
    }
}
