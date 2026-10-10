using System.ComponentModel;
using System.Diagnostics;
using MarkdownBlaze.Security;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Platform;

/// <summary>
/// <see cref="IShellLauncher"/> for the real shell: one class with the platform switches, so there is a
/// single place that starts processes on the app's behalf and a single seam to replace in a test.
/// <para>
/// Nothing here goes through a command line that is built from text: arguments are added one by one to
/// <see cref="ProcessStartInfo.ArgumentList"/>, so a path cannot break out of its argument whatever it
/// holds. Programs that are looked for through the search path (<c>open</c>, <c>xdg-open</c>) are started
/// from the user's profile folder rather than from the document's, which is the one directory that could
/// otherwise be searched first.
/// </para>
/// </summary>
internal sealed partial class ShellLauncher : IShellLauncher
{
    private static readonly ILogger Logger = Log.For(nameof(ShellLauncher));

    /// <summary>The only scheme <see cref="OpenStoreReview"/> opens.</summary>
    private const string StoreScheme = "ms-windows-store";

    public bool OpenExternal(Uri uri)
    {
        // The callers have checked already; this is the last step before the shell, so it is checked again
        // from the address as the shell would see it.
        var address = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString;
        if (!ExternalLinks.TryGetOpenable(address, out var openable))
        {
            LogRefused(Logger, uri.IsAbsoluteUri ? uri.Scheme : "(relative)");
            return false;
        }

        return Start(new ProcessStartInfo(openable.AbsoluteUri) { UseShellExecute = true }, "a link", openable.Scheme);
    }

    public void OpenInNewWindow(string path)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;

        // ArgumentList, not a quoted string: the runtime does the quoting.
        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        info.ArgumentList.Add(path);
        _ = Start(info, "a new window", path);
    }

    public void RevealInFileManager(string path)
    {
        if (!File.Exists(path)) return;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            LogLaunchFailed(Logger, ex, "the containing folder", path);
            return;
        }

        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            // The full path of the system's own Explorer. A bare "explorer.exe" is looked for in the
            // current directory first, and when Explorer launches the app for a document that is the
            // document's folder.
            info = new ProcessStartInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            info.ArgumentList.Add("/select,");
            info.ArgumentList.Add(full);
        }
        else if (OperatingSystem.IsMacOS())
        {
            info = new ProcessStartInfo("open");
            info.ArgumentList.Add("-R");
            info.ArgumentList.Add(full);
        }
        else
        {
            info = new ProcessStartInfo("xdg-open");
            info.ArgumentList.Add(Path.GetDirectoryName(full) ?? full);
        }

        info.UseShellExecute = false;
        // open and xdg-open are found through PATH, which is how they are meant to be run; the working
        // directory is the one place left that could be searched, so it is not the document's folder.
        info.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _ = Start(info, "the containing folder", full);
    }

    public bool OpenStoreReview(Uri review)
    {
        if (!review.IsAbsoluteUri || !string.Equals(review.Scheme, StoreScheme, StringComparison.OrdinalIgnoreCase))
        {
            LogRefused(Logger, review.IsAbsoluteUri ? review.Scheme : "(relative)");
            return false;
        }

        return Start(new ProcessStartInfo(review.AbsoluteUri) { UseShellExecute = true }, "the Store", review.Scheme);
    }

    private static bool Start(ProcessStartInfo info, string what, string target)
    {
        try
        {
            Process.Start(info)?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // Best-effort: no handler for the scheme, no Store on this machine, a shell that would not
            // start it. The reader asked and nothing happened, which is exactly what the log is for.
            LogLaunchFailed(Logger, ex, what, target);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Refused to open an address with the {Scheme} scheme")]
    private static partial void LogRefused(ILogger log, string scheme);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open {What} ({Target})")]
    private static partial void LogLaunchFailed(ILogger log, Exception ex, string what, string target);
}
