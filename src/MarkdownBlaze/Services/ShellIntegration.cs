using System.Diagnostics;

namespace MarkdownBlaze.Services;

/// <summary>
/// Ties MarkdownBlaze into Windows: the <c>markdown://</c> protocol, and "Open with MarkdownBlaze"
/// on documents and folders in Explorer.
/// <para>
/// Applied at startup, every launch: being able to open a document from Explorer is part of having
/// the app installed, not a preference to hunt for. Everything lives under HKEY_CURRENT_USER, so
/// nothing needs elevation and nothing changes for other users.
/// </para>
/// <para>
/// On Windows 11 these entries sit in the classic menu, which is one level down under
/// <em>Show more options</em> — pinning to the short menu requires a packaged extension rather than
/// a registry key.
/// </para>
/// </summary>
public sealed class ShellIntegration
{
    private const string Verb = "MarkdownBlaze";
    private const string Label = "Open with MarkdownBlaze";
    private const string Classes = @"HKCU\Software\Classes";
    private const string ProtocolKey = Classes + @"\" + UriScheme.Name;

    /// <summary>Document types offered the Explorer verb. Deliberately not .txt — too broad a claim.</summary>
    private static readonly string[] FileExtensions = [".md", ".markdown", ".mdx"];

    private static string FileVerbKey(string extension) =>
        $@"{Classes}\SystemFileAssociations\{extension}\shell\{Verb}";

    private const string FolderVerbKey = Classes + @"\Directory\shell\" + Verb;
    private const string FolderBackgroundVerbKey = Classes + @"\Directory\Background\shell\" + Verb;

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Registers whatever is missing. Called on every launch, so it checks first: the keys are
    /// already right the vast majority of the time, and this also repairs them after the app is
    /// moved or reinstalled somewhere else, when they would point at an executable that has gone.
    /// </summary>
    public void EnsureRegistered()
    {
        if (!IsSupported) return;
        if (!IsContextMenuRegistered()) RegisterContextMenu();
        if (!IsProtocolRegistered()) RegisterProtocol();
    }

    // ---- markdown:// protocol ---------------------------------------------------------------------

    public bool IsProtocolRegistered() => PointsAtThisApp($@"{ProtocolKey}\shell\open\command");

    public bool RegisterProtocol()
    {
        if (Exe is not { } exe) return false;

        return Reg("add", ProtocolKey, "/ve", "/d", $"URL:{UriScheme.Name} document", "/f").ok
            && Reg("add", ProtocolKey, "/v", "URL Protocol", "/d", "", "/f").ok
            && Reg("add", $@"{ProtocolKey}\DefaultIcon", "/ve", "/d", $"\"{exe}\",0", "/f").ok
            && Reg("add", $@"{ProtocolKey}\shell\open\command", "/ve", "/d", $"\"{exe}\" \"%1\"", "/f").ok;
    }

    public bool UnregisterProtocol() => IsSupported && Reg("delete", ProtocolKey, "/f").ok;

    // ---- Explorer context menu --------------------------------------------------------------------

    public bool IsContextMenuRegistered() => PointsAtThisApp($@"{FileVerbKey(FileExtensions[0])}\command");

    public bool RegisterContextMenu()
    {
        if (Exe is not { } exe) return false;

        var ok = true;
        foreach (var extension in FileExtensions)
            ok &= AddVerb(FileVerbKey(extension), exe, "%1");

        // %V is the folder that was clicked, for both a folder itself and the empty space inside one.
        ok &= AddVerb(FolderVerbKey, exe, "%V");
        ok &= AddVerb(FolderBackgroundVerbKey, exe, "%V");
        return ok;
    }

    public bool UnregisterContextMenu()
    {
        if (!IsSupported) return false;

        var ok = true;
        foreach (var extension in FileExtensions)
            ok &= Reg("delete", FileVerbKey(extension), "/f").ok;

        ok &= Reg("delete", FolderVerbKey, "/f").ok;
        ok &= Reg("delete", FolderBackgroundVerbKey, "/f").ok;
        return ok;
    }

    private static bool AddVerb(string key, string exe, string argument) =>
        Reg("add", key, "/ve", "/d", Label, "/f").ok
        && Reg("add", key, "/v", "Icon", "/d", $"\"{exe}\",0", "/f").ok
        && Reg("add", $@"{key}\command", "/ve", "/d", $"\"{exe}\" \"{argument}\"", "/f").ok;

    // ---- registry plumbing ------------------------------------------------------------------------

    private static string? Exe => IsSupported && !string.IsNullOrEmpty(Environment.ProcessPath)
        ? Environment.ProcessPath
        : null;

    /// <summary>True when the command stored at <paramref name="key"/> launches this executable.</summary>
    private static bool PointsAtThisApp(string key)
    {
        if (Exe is not { } exe) return false;
        var (ok, output) = Reg("query", key, "/ve");
        return ok && output.Contains(exe, StringComparison.OrdinalIgnoreCase);
    }

    private static (bool ok, string output) Reg(params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo("reg")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);

            using var process = Process.Start(info);
            if (process is null) return (false, string.Empty);

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return (process.HasExited && process.ExitCode == 0, output);
        }
        catch
        {
            return (false, string.Empty); // a locked-down machine is a "no", not a crash
        }
    }
}
