using System.Runtime.Versioning;
using System.Security;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace MarkdownBlaze.Platform.Windows;

/// <summary>
/// Ties MarkdownBlaze into Windows: the <c>markdown://</c> protocol, "Open with" for Markdown documents,
/// and "Open with MarkdownBlaze" on folders in Explorer.
/// <para>
/// There is one way of registering, and two parties that can do it. The installer
/// (<c>packaging/windows/MarkdownBlaze.iss</c>) writes exactly the keys listed here, and leaves a marker
/// (<c>HKCU\Software\MarkdownBlaze\InstallSource = installer</c>) so the app can tell. A portable copy
/// writes them only when asked — Settings, or <c>--register</c> — and takes them away the same way
/// (<see cref="UnregisterAll"/>, <c>--unregister</c>). Nothing is written at startup: a viewer that
/// edits the registry every time it opens is not what someone who unzipped it expects, and the entries
/// it left behind after being deleted would point at nothing.
/// </para>
/// <para>
/// Documents are registered the way Windows wants a handler registered: a ProgId
/// (<c>MarkdownBlaze.Document</c>) listed under each extension's <c>OpenWithProgids</c>. That makes the app
/// a candidate for "Open with" and for Settings → Default apps, which a verb under
/// <c>SystemFileAssociations</c> never was (and which is what the app wrote before; those keys are
/// removed when it registers or unregisters). Folders have no default app, so they get a verb.
/// </para>
/// <para>
/// Everything lives under HKEY_CURRENT_USER, so nothing needs elevation and nothing changes for other
/// users. On Windows 11 the folder verb sits in the classic menu, one level down under <em>Show more
/// options</em> — pinning it to the short menu requires a packaged extension rather than a registry key.
/// </para>
/// <para>
/// The registry is written in-process through <see cref="Registry"/>. It used to be written by
/// starting <c>reg.exe</c> by its bare name, and a bare name is looked for in the current directory
/// before System32 — which, when Explorer launches the app for a document, is the document's folder.
/// A <c>reg.exe</c> placed beside a <c>.md</c> file would have run, with the user's rights, on every
/// launch. No process is started here at all.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class ShellIntegration
{
    /// <summary>The ProgId the installer registers too.</summary>
    public const string ProgId = "MarkdownBlaze.Document";

    /// <summary>The value, under <see cref="DefaultSettings"/>, in which the installer says it did the registering.</summary>
    public const string InstallSourceName = "InstallSource";

    /// <summary>What the installer writes to <see cref="InstallSourceName"/>.</summary>
    public const string InstallSourceInstaller = "installer";

    /// <summary>Where, under HKEY_CURRENT_USER, the per-user class registrations live.</summary>
    internal const string DefaultClasses = @"Software\Classes";

    /// <summary>Where, under HKEY_CURRENT_USER, the app keeps its marker — the one place it is not under Classes.</summary>
    internal const string DefaultSettings = @"Software\MarkdownBlaze";

    private static readonly ILogger Logger = Log.For(nameof(ShellIntegration));

    private const string Verb = "MarkdownBlaze";
    private const string Label = "Open with MarkdownBlaze";

    /// <summary>Document types offered "Open with". Deliberately not .txt — too broad a claim.</summary>
    private static readonly string[] FileExtensions = [".md", ".markdown", ".mdx"];
    private readonly string _classes;
    private readonly string _settings;
    private readonly string? _exe;

    public ShellIntegration()
        : this(DefaultClasses, DefaultSettings, Environment.ProcessPath)
    { }

    /// <summary>Creates the integration over other registry keys, so that a test can write somewhere harmless.</summary>
    /// <param name="classesKey">
    /// The key, under HKEY_CURRENT_USER, that plays the part of <c>Software\Classes</c> — so a test can
    /// write somewhere that is not the real file associations.
    /// </param>
    /// <param name="settingsKey">The key that plays the part of <c>Software\MarkdownBlaze</c>, where the installer's marker is.</param>
    /// <param name="exePath">The executable the registered commands launch.</param>
    internal ShellIntegration(string classesKey, string settingsKey, string? exePath)
    {
        _classes = classesKey.Trim('\\');
        _settings = settingsKey.Trim('\\');
        _exe = string.IsNullOrEmpty(exePath) ? null : exePath;
    }

    private string ProgIdKey => $@"{_classes}\{ProgId}";
    private string ProtocolKey => $@"{_classes}\{UriScheme.Name}";
    private string FolderVerbKey => $@"{_classes}\Directory\shell\{Verb}";
    private string FolderBackgroundVerbKey => $@"{_classes}\Directory\Background\shell\{Verb}";

    private string OpenWithProgIdsKey(string extension) => $@"{_classes}\{extension}\OpenWithProgids";

    /// <summary>What the app used to write for documents: a verb on the extension, which is not a candidate for default app.</summary>
    private string LegacyFileVerbKey(string extension) => $@"{_classes}\SystemFileAssociations\{extension}\shell\{Verb}";

    // ---- state ------------------------------------------------------------------------------------

    /// <summary>Who registered the app, if anybody; see <see cref="ShellIntegrationState"/>.</summary>
    public ShellIntegrationState State
    {
        get
        {
            if (IsInstalledByInstaller()) return ShellIntegrationState.InstalledByInstaller;
            return IsRegistered() ? ShellIntegrationState.RegisteredByApp : ShellIntegrationState.NotRegistered;
        }
    }

    /// <summary>
    /// True when every part of the registration is there and launches this executable — so a registration
    /// that points at another copy of the app (or at one that has since been moved) is not "registered":
    /// <see cref="RegisterAll"/> would put it right.
    /// </summary>
    public bool IsRegistered() =>
        PointsAtThisApp($@"{ProgIdKey}\shell\open\command")
            && FileExtensions.All(extension => HasValue(OpenWithProgIdsKey(extension), ProgId))
            && PointsAtThisApp($@"{ProtocolKey}\shell\open\command")
            && PointsAtThisApp($@"{FolderVerbKey}\command")
            && PointsAtThisApp($@"{FolderBackgroundVerbKey}\command");

    /// <summary>Whether the installer's marker is there. The marker says who did it, not whether the keys are intact.</summary>
    public bool IsInstalledByInstaller()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_settings);
            return key?.GetValue(InstallSourceName) is string value
                && string.Equals(value, InstallSourceInstaller, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            LogRegistryRefused(Logger, ex, "read", _settings);
            return false;
        }
    }

    // ---- registering ------------------------------------------------------------------------------

    /// <summary>
    /// Registers the app for this user: the ProgId and its place in each document type's "Open with" list,
    /// the folder verbs, and the <c>markdown://</c> protocol. Safe to repeat; it also repairs a
    /// registration that points at an executable that has since moved. Returns false when the registry
    /// refused something, or when the executable is not known.
    /// </summary>
    public bool RegisterAll()
    {
        if (_exe is not { } exe) return false;

        var ok = RegisterProgId(exe);
        foreach (var extension in FileExtensions)
            ok &= Write(OpenWithProgIdsKey(extension), (ProgId, ""));

        // %V is the folder that was clicked, for both a folder itself and the empty space inside one.
        ok &= AddVerb(FolderVerbKey, exe, "%V");
        ok &= AddVerb(FolderBackgroundVerbKey, exe, "%V");
        ok &= RegisterProtocol(exe);
        ok &= RemoveLegacyFileVerbs();
        return ok;
    }

    /// <summary>
    /// Takes the registration away, whoever made it — the app's own and the installer's are the same keys.
    /// That includes the installer's marker, so that the app does not go on saying the installer manages
    /// something that is no longer there. Returns false when the registry refused something.
    /// </summary>
    public bool UnregisterAll()
    {
        var ok = true;
        foreach (var extension in FileExtensions)
        {
            ok &= DeleteValue(OpenWithProgIdsKey(extension), ProgId);

            // An extension the app added to its own (.mdx has no other owner) is not left behind as an empty key.
            ok &= DeleteIfEmpty(OpenWithProgIdsKey(extension));
            ok &= DeleteIfEmpty($@"{_classes}\{extension}");
        }

        ok &= DeleteTree(ProgIdKey);
        ok &= DeleteTree(FolderVerbKey);
        ok &= DeleteTree(FolderBackgroundVerbKey);
        ok &= DeleteTree(ProtocolKey);
        ok &= RemoveLegacyFileVerbs();
        ok &= DeleteValue(_settings, InstallSourceName);
        ok &= DeleteIfEmpty(_settings);
        return ok;
    }

    private bool RegisterProgId(string exe) =>
        Write(ProgIdKey, (null, "Markdown Document"))
            && Write($@"{ProgIdKey}\DefaultIcon", (null, $"\"{exe}\",0"))
            && Write($@"{ProgIdKey}\shell\open\command", (null, $"\"{exe}\" \"%1\""));

    private bool RegisterProtocol(string exe) =>
        Write(ProtocolKey, (null, $"URL:{UriScheme.Name} document"), ("URL Protocol", ""))
            && Write($@"{ProtocolKey}\DefaultIcon", (null, $"\"{exe}\",0"))
            && Write($@"{ProtocolKey}\shell\open\command", (null, $"\"{exe}\" \"%1\""));

    private static bool AddVerb(string key, string exe, string argument) =>
        Write(key, (null, Label), ("Icon", $"\"{exe}\",0"))
            && Write($@"{key}\command", (null, $"\"{exe}\" \"{argument}\""));

    private bool RemoveLegacyFileVerbs()
    {
        var ok = true;
        foreach (var extension in FileExtensions)
        {
            ok &= DeleteTree(LegacyFileVerbKey(extension));
            ok &= DeleteIfEmpty($@"{_classes}\SystemFileAssociations\{extension}\shell");
            ok &= DeleteIfEmpty($@"{_classes}\SystemFileAssociations\{extension}");
        }

        return ok;
    }

    // ---- registry plumbing ------------------------------------------------------------------------

    /// <summary>True when the command stored at <paramref name="key"/> launches this executable.</summary>
    private bool PointsAtThisApp(string key)
    {
        if (_exe is not { } exe) return false;
        try
        {
            using var command = Registry.CurrentUser.OpenSubKey(key);
            return command?.GetValue(name: null) is string value
                && value.Contains(exe, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            LogRegistryRefused(Logger, ex, "read", key);
            return false; // a locked-down machine is a "no", not a crash
        }
    }

    private static bool HasValue(string key, string name)
    {
        try
        {
            using var subKey = Registry.CurrentUser.OpenSubKey(key);
            return subKey?.GetValue(name) is not null;
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            LogRegistryRefused(Logger, ex, "read", key);
            return false;
        }
    }

    /// <summary>Creates <paramref name="key"/> if need be and sets the given values on it (a null name is the default value).</summary>
    private static bool Write(string key, params (string? name, string value)[] values)
    {
        try
        {
            using var subKey = Registry.CurrentUser.CreateSubKey(key);
            foreach (var (name, value) in values)
                subKey.SetValue(name, value, RegistryValueKind.String);

            return true;
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            LogRegistryRefused(Logger, ex, "write", key);
            return false;
        }
    }

    private static bool DeleteTree(string key)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            LogRegistryRefused(Logger, ex, "delete", key);
            return false;
        }
    }

    /// <summary>Removes one value from <paramref name="key"/>, leaving the key and whatever else is in it; a missing key or value is fine.</summary>
    private static bool DeleteValue(string key, string name)
    {
        try
        {
            using var subKey = Registry.CurrentUser.OpenSubKey(key, writable: true);
            subKey?.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            LogRegistryRefused(Logger, ex, "delete", key);
            return false;
        }
    }

    /// <summary>Removes <paramref name="key"/> if there is nothing in it — no values, no subkeys — and leaves it alone otherwise.</summary>
    private static bool DeleteIfEmpty(string key)
    {
        try
        {
            using (var subKey = Registry.CurrentUser.OpenSubKey(key))
            {
                if (subKey is null || subKey.ValueCount > 0 || subKey.SubKeyCount > 0) return true;
            }

            Registry.CurrentUser.DeleteSubKey(key, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            LogRegistryRefused(Logger, ex, "delete", key);
            return false;
        }
    }

    /// <summary>
    /// What the registry does when it will not let us in: not allowed, policy, a key that is being
    /// deleted, a name it does not like. Anything else is a bug and is left to surface.
    /// </summary>
    private static bool IsRegistryRefusal(Exception ex) =>
        ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The registry refused to {Operation} {Key}")]
    private static partial void LogRegistryRefused(ILogger log, Exception ex, string operation, string key);
}
