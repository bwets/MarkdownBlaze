using System.Runtime.InteropServices;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Platform.Windows;

/// <summary>
/// Sets a window's icon directly via WM_SETICON / the window class. Photino's SetIconFile only
/// updates the Windows title bar, so we push the .ico onto the HWND once the native window exists.
/// Windows-only: guarded so it is a no-op (and never touches user32) on other platforms.
/// </summary>
internal static partial class WindowIcon
{
    private static readonly ILogger Logger = Log.For(nameof(WindowIcon));

    private const int WM_SETICON = 0x0080;
    private const nint ICON_SMALL = 0;
    private const nint ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;

    // user32 is looked for in System32 only, never next to the executable or in the current directory.
    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LoadImage(nint hinst, string name, uint type, int cx, int cy, uint load);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetClassLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    public static void Apply(nint hwnd, string icoPath)
    {
        if (!OperatingSystem.IsWindows() || hwnd == nint.Zero) return;
        try
        {
            // Windows 11's taskbar only adopts ICON_BIG when it's a *large* image — a 16/32px icon
            // updates just the title bar and leaves the taskbar generic. So load the big icon at 256px.
            var big = LoadImage(nint.Zero, icoPath, IMAGE_ICON, 256, 256, LR_LOADFROMFILE);
            var small = LoadImage(nint.Zero, icoPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            if (big != nint.Zero)
            {
                _ = SendMessage(hwnd, WM_SETICON, ICON_BIG, big);
                _ = SetClassLongPtr(hwnd, GCLP_HICON, big);   // the shell/taskbar also reads the class icon
            }

            if (small != nint.Zero)
            {
                _ = SendMessage(hwnd, WM_SETICON, ICON_SMALL, small);
                _ = SetClassLongPtr(hwnd, GCLP_HICONSM, small);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Best-effort: the window keeps its default icon.
            LogIconNotApplied(Logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not set the window icon")]
    private static partial void LogIconNotApplied(ILogger log, Exception ex);
}
