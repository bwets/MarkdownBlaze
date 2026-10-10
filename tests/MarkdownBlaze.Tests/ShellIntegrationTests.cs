using System.Runtime.Versioning;
using MarkdownBlaze.Components.Settings;
using MarkdownBlaze.Platform.Windows;
using Microsoft.Win32;

namespace MarkdownBlaze.Tests;

/// <summary>
/// PKG-1/PKG-3/SEC-6: the shell registration is one model (a ProgId in each document type's
/// OpenWithProgids, verbs on folders, the markdown protocol), written to the registry directly — never by
/// starting reg.exe — and only on request. The keys go under HKCU\Software\MarkdownBlazeTests\&lt;guid&gt;,
/// not the real file associations.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShellIntegrationTests : IDisposable
{
    private const string TestRoot = @"Software\MarkdownBlazeTests";

    private static readonly string[] Extensions = [".md", ".markdown", ".mdx"];
    private readonly string _classes = $@"{TestRoot}\{Guid.NewGuid():N}\Classes";
    private readonly string _settings;
    private readonly string _exe = Path.Combine(Path.GetTempPath(), "Some Folder", "MarkdownBlaze.exe");

    public ShellIntegrationTests()
    {
        _settings = _classes.Replace(@"\Classes", @"\Settings", StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
            Registry.CurrentUser.DeleteSubKeyTree(TestRoot, throwOnMissingSubKey: false);
    }

    private ShellIntegration Shell(string? exe = null) => new(_classes, _settings, exe ?? _exe);

    private static string? Default(string key)
    {
        using var subKey = Registry.CurrentUser.OpenSubKey(key);
        return subKey?.GetValue(name: null) as string;
    }

    private static bool Exists(string key)
    {
        using var subKey = Registry.CurrentUser.OpenSubKey(key);
        return subKey is not null;
    }

    private static bool Lists(string key, string name)
    {
        using var subKey = Registry.CurrentUser.OpenSubKey(key);
        return subKey?.GetValue(name) is not null;
    }

    private void InstallerMarker()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_settings);
        key.SetValue("InstallSource", "installer", RegistryValueKind.String);
    }

    [SkippableFact]
    public void Registering_writes_the_progid_the_folder_verbs_and_the_protocol()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var shell = Shell();

        Assert.False(shell.IsRegistered());

        Assert.True(shell.RegisterAll());

        Assert.True(shell.IsRegistered());

        Assert.Equal("URL:markdown document", Default($@"{_classes}\markdown"));
        using (var protocol = Registry.CurrentUser.OpenSubKey($@"{_classes}\markdown"))
            Assert.Equal("", protocol!.GetValue("URL Protocol"));

        Assert.Equal($"\"{_exe}\" \"%1\"", Default($@"{_classes}\markdown\shell\open\command"));
        Assert.Equal($"\"{_exe}\",0", Default($@"{_classes}\markdown\DefaultIcon"));

        // Documents: the same ProgId the installer writes, listed for each extension — not a verb.
        Assert.Equal("Markdown Document", Default($@"{_classes}\MarkdownBlaze.Document"));
        Assert.Equal($"\"{_exe}\" \"%1\"", Default($@"{_classes}\MarkdownBlaze.Document\shell\open\command"));
        Assert.Equal($"\"{_exe}\",0", Default($@"{_classes}\MarkdownBlaze.Document\DefaultIcon"));
        foreach (var extension in Extensions)
        {
            Assert.True(Lists($@"{_classes}\{extension}\OpenWithProgids", "MarkdownBlaze.Document"), extension);
            Assert.False(Exists($@"{_classes}\SystemFileAssociations\{extension}\shell\MarkdownBlaze"), extension);
        }

        // Folders: a verb, with %V — the folder clicked, or the one whose empty space was.
        foreach (var verb in new[] { @"Directory\shell\MarkdownBlaze", @"Directory\Background\shell\MarkdownBlaze" })
        {
            Assert.Equal("Open with MarkdownBlaze", Default($@"{_classes}\{verb}"));
            Assert.Equal($"\"{_exe}\" \"%V\"", Default($@"{_classes}\{verb}\command"));
            using var key = Registry.CurrentUser.OpenSubKey($@"{_classes}\{verb}");
            Assert.Equal($"\"{_exe}\",0", key!.GetValue("Icon"));
        }
    }

    [SkippableFact]
    public void Registering_is_repeatable_and_leaves_other_handlers_in_the_list_alone()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        using (var other = Registry.CurrentUser.CreateSubKey($@"{_classes}\.md\OpenWithProgids"))
            other.SetValue("SomeOther.md", "", RegistryValueKind.String);

        var shell = Shell();
        Assert.True(shell.RegisterAll());
        Assert.True(shell.RegisterAll());
        Assert.True(shell.IsRegistered());
        Assert.True(Lists($@"{_classes}\.md\OpenWithProgids", "SomeOther.md"));

        Assert.True(shell.UnregisterAll());

        Assert.False(Lists($@"{_classes}\.md\OpenWithProgids", "MarkdownBlaze.Document"));
        Assert.True(Lists($@"{_classes}\.md\OpenWithProgids", "SomeOther.md"));
    }

    [SkippableFact]
    public void Registration_is_repaired_when_the_app_has_moved()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        Assert.True(Shell(@"C:\old place\MarkdownBlaze.exe").RegisterAll());

        var moved = Shell();
        Assert.False(moved.IsRegistered());
        Assert.Equal(ShellIntegrationState.NotRegistered, moved.State);

        Assert.True(moved.RegisterAll());

        Assert.True(moved.IsRegistered());
        Assert.Equal($"\"{_exe}\" \"%1\"", Default($@"{_classes}\markdown\shell\open\command"));
        Assert.Equal($"\"{_exe}\" \"%1\"", Default($@"{_classes}\MarkdownBlaze.Document\shell\open\command"));
        Assert.Equal($"\"{_exe}\" \"%V\"", Default($@"{_classes}\Directory\shell\MarkdownBlaze\command"));
    }

    [SkippableFact]
    public void Registration_with_a_part_missing_is_not_registered_until_repaired()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var shell = Shell();
        Assert.True(shell.RegisterAll());
        Registry.CurrentUser.DeleteSubKeyTree($@"{_classes}\Directory\Background\shell\MarkdownBlaze");

        Assert.False(shell.IsRegistered());
        Assert.Equal(ShellIntegrationState.NotRegistered, shell.State);

        Assert.True(shell.RegisterAll());
        Assert.Equal(ShellIntegrationState.RegisteredByApp, shell.State);
    }

    [SkippableFact]
    public void The_old_per_extension_verbs_are_removed_by_registering_and_by_unregistering()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        void WriteLegacy()
        {
            foreach (var extension in Extensions)
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"{_classes}\SystemFileAssociations\{extension}\shell\MarkdownBlaze\command");
                key.SetValue(name: null, "\"old.exe\" \"%1\"");
            }
        }

        bool AnyLegacy()
        {
            return Extensions.Any(extension => Exists($@"{_classes}\SystemFileAssociations\{extension}\shell\MarkdownBlaze"));
        }

        var shell = Shell();
        WriteLegacy();
        Assert.True(shell.RegisterAll());
        Assert.False(AnyLegacy());

        WriteLegacy();
        Assert.True(shell.UnregisterAll());
        Assert.False(AnyLegacy());
    }

    [SkippableFact]
    public void Unregistering_removes_what_registering_added()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var shell = Shell();
        Assert.True(shell.RegisterAll());

        Assert.True(shell.UnregisterAll());

        Assert.False(shell.IsRegistered());
        Assert.Equal(ShellIntegrationState.NotRegistered, shell.State);
        Assert.False(Exists($@"{_classes}\markdown"));
        Assert.False(Exists($@"{_classes}\MarkdownBlaze.Document"));
        Assert.False(Exists($@"{_classes}\Directory\shell\MarkdownBlaze"));
        Assert.False(Exists($@"{_classes}\Directory\Background\shell\MarkdownBlaze"));
        foreach (var extension in Extensions)
            Assert.False(Lists($@"{_classes}\{extension}\OpenWithProgids", "MarkdownBlaze.Document"), extension);

        // Nothing is left behind where the app was the only owner: .mdx had no other handler, and the
        // marker's key held nothing else.
        Assert.False(Exists($@"{_classes}\.mdx"));
        Assert.False(Exists(_settings));

        // and doing it again is not an error
        Assert.True(shell.UnregisterAll());
    }

    [SkippableFact]
    public void The_state_tells_who_registered()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var shell = Shell();
        Assert.Equal(ShellIntegrationState.NotRegistered, shell.State);

        Assert.True(shell.RegisterAll());
        Assert.Equal(ShellIntegrationState.RegisteredByApp, shell.State);

        // The installer writes the same keys and a marker; the marker is what says it was the installer.
        InstallerMarker();
        Assert.Equal(ShellIntegrationState.InstalledByInstaller, shell.State);
        Assert.True(shell.IsInstalledByInstaller());
    }

    [SkippableFact]
    public void The_installer_marker_counts_even_when_the_keys_point_at_another_copy()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        Assert.True(Shell(@"C:\Program Files\MarkdownBlaze\MarkdownBlaze.exe").RegisterAll());
        InstallerMarker();

        Assert.Equal(ShellIntegrationState.InstalledByInstaller, Shell().State);
    }

    [SkippableFact]
    public void A_marker_that_is_not_the_installers_is_ignored()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        using (var key = Registry.CurrentUser.CreateSubKey(_settings))
            key.SetValue("InstallSource", "somebody else", RegistryValueKind.String);

        Assert.Equal(ShellIntegrationState.NotRegistered, Shell().State);
    }

    [SkippableFact]
    public void Unregistering_also_removes_the_installers_marker()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var shell = Shell();
        Assert.True(shell.RegisterAll());
        InstallerMarker();

        Assert.True(shell.UnregisterAll());

        // The registration the marker vouched for is gone, so the app no longer says it is managed.
        Assert.Equal(ShellIntegrationState.NotRegistered, shell.State);
    }

    [SkippableFact]
    public void Without_an_executable_nothing_is_registered()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var shell = new ShellIntegration(_classes, _settings, exePath: null);

        Assert.False(shell.RegisterAll());
        Assert.False(shell.IsRegistered());
        Assert.False(Exists(_classes));
    }

    [SkippableFact]
    public void A_reg_exe_in_the_current_directory_is_never_run()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        // A decoy "reg.exe" (a copy of cmd.exe, which would fail every reg command and write nothing)
        // in the working directory, as when Explorer launches the app for a document next to it. The
        // registration must still succeed — it cannot have gone through that program — and the
        // decoy's presence must not matter.
        using var folder = new TempFolder();
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), Path.Combine(folder.Path, "reg.exe"));
        var before = Environment.CurrentDirectory;
        Environment.CurrentDirectory = folder.Path;
        try
        {
            var shell = Shell();

            Assert.True(shell.RegisterAll());
            Assert.True(shell.IsRegistered());
            Assert.True(shell.UnregisterAll());
            Assert.False(shell.IsRegistered());
        }
        finally
        {
            Environment.CurrentDirectory = before;
        }
    }

    [SkippableFact]
    public void Settings_only_offers_the_row_where_it_means_something()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        var windows = new ShellIntegrationActions(Shell(), packaged: false);
        Assert.True(windows.IsAvailable);
        Assert.Equal(ShellIntegrationState.NotRegistered, windows.State);

        Assert.True(windows.Add());
        Assert.Equal(ShellIntegrationState.RegisteredByApp, windows.State);

        Assert.True(windows.Remove());
        Assert.Equal(ShellIntegrationState.NotRegistered, windows.State);

        // The Store build: the manifest registers the app, and the registry would be the package's own.
        var store = new ShellIntegrationActions(Shell(), packaged: true);
        Assert.False(store.IsAvailable);
        Assert.False(store.Add());
        Assert.False(Exists($@"{_classes}\markdown"));
    }

    [Fact]
    public void Where_there_is_no_integration_the_row_is_not_available_and_does_nothing()
    {
        var elsewhere = new ShellIntegrationActions(shell: null, packaged: false);

        Assert.False(elsewhere.IsAvailable);
        Assert.Equal(ShellIntegrationState.NotRegistered, elsewhere.State);
        Assert.False(elsewhere.Add());
        Assert.False(elsewhere.Remove());
    }

    [Fact]
    public void No_source_file_starts_reg_exe_or_a_bare_executable_name()
    {
        // The review's acceptance test, as a regression guard: nothing in the app starts "reg", and no
        // ProcessStartInfo is built from a bare executable name that a current directory could shadow.
        var sources = Directory.EnumerateFiles(
            Path.Combine(TestFiles.RepoRoot, "src", "MarkdownBlaze"),
            "*.cs",
            SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            Assert.DoesNotContain("\"reg\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("reg.exe\")", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ProcessStartInfo(\"explorer", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_app_never_registers_itself_at_startup()
    {
        // PKG-1: registration is the installer's, or the reader's by request. Program.cs must not call
        // RegisterAll/UnregisterAll except for --register/--unregister.
        var program = File.ReadAllText(Path.Combine(TestFiles.RepoRoot, "src", "MarkdownBlaze", "Program.cs"));

        Assert.DoesNotContain("EnsureRegistered", program, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(program, ".RegisterAll()"));
        Assert.Equal(1, CountOf(program, ".UnregisterAll()"));
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
