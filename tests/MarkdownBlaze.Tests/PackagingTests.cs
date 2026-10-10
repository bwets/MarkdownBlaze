using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MarkdownBlaze.Platform.Windows;
using MarkdownBlaze.Services;
using Microsoft.Win32;

namespace MarkdownBlaze.Tests;

/// <summary>
/// PKG-1..4: the packaging files say what the code and the README say they do. These read the files in
/// <c>packaging/</c> and <c>src/MarkdownBlaze/app.manifest</c> as text or XML; they cannot run an
/// installer, but they catch the ways the pieces drift apart (a key the app registers that the
/// installer would leave behind, a file type missing from the manifest).
/// </summary>
public sealed partial class PackagingTests : IDisposable
{
    private const string TestRoot = @"Software\MarkdownBlazeTests";

    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
            Registry.CurrentUser.DeleteSubKeyTree(TestRoot, throwOnMissingSubKey: false);
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([TestFiles.RepoRoot, .. path]));

    // ---- the installer --------------------------------------------------------------------------

    /// <summary>One line of the installer's [Registry] section.</summary>
    private sealed record InstallerEntry(string Subkey, string? Name, string? Data, string Type, string Flags);

    /// <summary>One <c>Name: value</c> pair of an installer line; a value is a quoted string (with "" for a quote) or runs to the next semicolon.</summary>
    [GeneratedRegex("""(?<name>Subkey|ValueType|ValueName|ValueData|Flags): (?<value>"(?:[^"]|"")*"|[^;]*)""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InstallerField { get; }

    private static List<InstallerEntry> InstallerRegistry()
    {
        var entries = new List<InstallerEntry>();
        var inRegistry = false;
        foreach (var line in Read("packaging", "windows", "MarkdownBlaze.iss").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[')) inRegistry = string.Equals(trimmed, "[Registry]", StringComparison.Ordinal);
            if (!inRegistry || !trimmed.StartsWith("Root: HKCU;", StringComparison.Ordinal)) continue;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var match in InstallerField.Matches(trimmed).Cast<Match>())
            {
                var value = match.Groups["value"].Value;
                fields[match.Groups["name"].Value] = value.StartsWith('"')
                    ? value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
                    : value.Trim();
            }

            entries.Add(new InstallerEntry(
                fields["Subkey"],
                fields.GetValueOrDefault("ValueName"),
                fields.GetValueOrDefault("ValueData"),
                fields["ValueType"],
                fields.GetValueOrDefault("Flags", "")));
        }

        return entries;
    }

    [SkippableFact]
    [SupportedOSPlatform("windows")]
    public void The_installer_registers_every_key_the_app_can_register()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        // Let the app register itself over a scratch hive, then look for every value it wrote in the
        // installer's [Registry] section, under the real key and with the same data.
        const string exe = @"C:\Program Files\X\MarkdownBlaze.exe";
        var classes = $@"{TestRoot}\{Guid.NewGuid():N}\Classes";
        Assert.True(new ShellIntegration(classes, classes + "Settings", exe).RegisterAll());

        var installer = InstallerRegistry();
        var written = new List<(string Key, string Name, string Data)>();
        using (var root = Registry.CurrentUser.OpenSubKey(classes))
            Collect(root!, "", written);

        Assert.NotEmpty(written);
        foreach (var (key, name, data) in written)
        {
            var real = @"Software\Classes\" + key;
            var line = installer.SingleOrDefault(e =>
                string.Equals(e.Subkey, real, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Name ?? "", name, StringComparison.OrdinalIgnoreCase));
            Assert.True(line is not null, $"The installer does not write [{real}] {(string.IsNullOrEmpty(name) ? "(default)" : name)}");

            // The installer's {app}\MarkdownBlaze.exe is the executable the app would have registered.
            var installerData = line.Data!.Replace(@"{app}\{#MyAppExe}", exe, StringComparison.Ordinal);
            Assert.Equal(data, installerData);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void Collect(RegistryKey key, string path, List<(string, string, string)> into)
    {
        foreach (var name in key.GetValueNames())
            into.Add((path, name, Convert.ToString(key.GetValue(name), CultureInfo.InvariantCulture) ?? ""));

        foreach (var child in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(child)!;
            Collect(sub, path.Length == 0 ? child : path + @"\" + child, into);
        }
    }

    [Fact]
    public void Everything_the_installer_writes_is_removed_by_the_uninstaller()
    {
        var entries = InstallerRegistry().Where(e => !e.Subkey.Equals("Environment", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(entries);

        foreach (var entry in entries.Where(e => !string.Equals(e.Type, "none", StringComparison.Ordinal)))
        {
            var removedWithValue = entry.Flags.Contains("uninsdeletevalue", StringComparison.Ordinal);
            var removedWithAKey = entries.Exists(other =>
                other.Flags.Contains("uninsdeletekey", StringComparison.Ordinal)
                    && (entry.Subkey.Equals(other.Subkey, StringComparison.OrdinalIgnoreCase)
                        || entry.Subkey.StartsWith(other.Subkey + @"\", StringComparison.OrdinalIgnoreCase)));
            Assert.True(removedWithValue || removedWithAKey, $"Uninstall would leave [{entry.Subkey}] {entry.Name}");
        }
    }

    [Fact]
    public void The_installer_leaves_the_marker_the_app_looks_for_and_clears_the_old_verbs()
    {
        var entries = InstallerRegistry();

        var marker = Assert.Single(entries, e => string.Equals(e.Subkey, @"Software\MarkdownBlaze", StringComparison.Ordinal));
        Assert.Equal("InstallSource", marker.Name);
        Assert.Equal("installer", marker.Data);

        foreach (var extension in new[] { ".md", ".markdown", ".mdx" })
        {
            var legacy = $@"Software\Classes\SystemFileAssociations\{extension}\shell\MarkdownBlaze";
            var line = Assert.Single(entries, e => string.Equals(e.Subkey, legacy, StringComparison.Ordinal));
            Assert.Equal("deletekey", line.Flags);
        }
    }

    // ---- the Store package ------------------------------------------------------------------------

    /// <summary>
    /// The rating request opens the Store's review page by package family name: the manifest's Identity Name,
    /// an underscore, and the 13-character hash of its Publisher. The address is a constant in the code, so
    /// this is what keeps it from drifting away from the manifest.
    /// </summary>
    [Fact]
    public void The_store_review_address_names_the_package_family_of_the_manifest()
    {
        var manifest = XDocument.Parse(Read("packaging", "windows", "AppxManifest.xml"));
        var identity = manifest.Root!.Element(Foundation + "Identity")!;
        var name = (string)identity.Attribute("Name")!;
        var publisher = (string)identity.Attribute("Publisher")!;

        var query = RatingPrompt.StoreReviewUri.Query;
        Assert.StartsWith("?PFN=", query, StringComparison.Ordinal);
        var family = query["?PFN=".Length..];

        Assert.StartsWith(name + "_", family, StringComparison.Ordinal);
        Assert.Equal(name + "_" + PublisherId(publisher), family);
        Assert.Equal("ms-windows-store", RatingPrompt.StoreReviewUri.Scheme);
    }

    /// <summary>
    /// The Store's 13-character hash of a Publisher: the first 8 bytes of the SHA-256 of its UTF-16
    /// text, written 5 bits at a time in a base-32 alphabet of the digits and the letters without i, l, o
    /// and u, as the package tools do.
    /// </summary>
    private static string PublisherId(string publisher)
    {
        const string alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        var value = BinaryPrimitives.ReadUInt64BigEndian(hash);
        var id = new StringBuilder();
        for (var group = 0; group < 13; group++)
        {
            var shift = 64 - (5 * (group + 1)); // the 13th group runs one bit past the 64 there are: a zero
            var bits = shift >= 0 ? value >> shift : value << -shift;
            _ = id.Append(alphabet[(int)(bits & 31)]);
        }

        return id.ToString();
    }

    [Fact]
    public void The_msix_manifest_declares_the_file_types_the_protocol_and_the_alias()
    {
        var manifest = XDocument.Parse(Read("packaging", "windows", "AppxManifest.xml"));
        var application = manifest.Root!.Element(Foundation + "Applications")!.Element(Foundation + "Application")!;
        var extensions = application.Element(Foundation + "Extensions")!.Elements().ToList();

        var protocol = extensions.Single(e => string.Equals((string?)e.Attribute("Category"), "windows.protocol", StringComparison.Ordinal));
        Assert.Equal("markdown", (string?)protocol.Element(Uap + "Protocol")!.Attribute("Name"));

        var fileTypes = extensions.Single(e => string.Equals((string?)e.Attribute("Category"), "windows.fileTypeAssociation", StringComparison.Ordinal))
            .Descendants(Uap + "FileType")
            .Select(e => e.Value)
            .ToList();
        Assert.Equal([".md", ".markdown", ".mdx"], fileTypes);

        Assert.Contains(extensions, e => string.Equals((string?)e.Attribute("Category"), "windows.appExecutionAlias", StringComparison.Ordinal));
    }

    [Fact]
    public void The_application_manifest_names_windows_10_and_11_and_allows_long_paths()
    {
        var document = XDocument.Parse(Read("src", "MarkdownBlaze", "app.manifest"));
        XElement[] Named(string name)
        {
            return [.. document.Descendants().Where(e => string.Equals(e.Name.LocalName, name, StringComparison.Ordinal))];
        }

        // Windows 10 and 11 share this id.
        var supported = Assert.Single(Named("supportedOS"));
        Assert.Equal("{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}", (string?)supported.Attribute("Id"));
        Assert.Equal("true", Assert.Single(Named("longPathAware")).Value);
        Assert.Equal("PerMonitorV2", Assert.Single(Named("dpiAwareness")).Value);
    }

    // ---- Linux and macOS --------------------------------------------------------------------------

    [Fact]
    public void The_deb_is_named_in_lowercase_and_says_who_made_it_and_where_it_lives()
    {
        var script = Read("packaging", "linux", "build-deb.sh");

        Assert.Contains("NAME=\"markdownblaze\"", script, StringComparison.Ordinal);
        Assert.Contains("Package: ${NAME}", script, StringComparison.Ordinal);
        Assert.Matches(@"Maintainer: [^<\n]+ <[^@\n>]+@[^>\n]+>", script);
        Assert.Contains("Homepage: https://github.com/bwets/MarkdownBlaze", script, StringComparison.Ordinal);
        Assert.Contains("DEBIAN/postinst", script, StringComparison.Ordinal);
        Assert.Contains("DEBIAN/postrm", script, StringComparison.Ordinal);
        Assert.Contains("update-desktop-database", script, StringComparison.Ordinal);
        Assert.Contains("gtk-update-icon-cache", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_arch_package_is_lowercase_and_the_workflow_hands_it_the_tarball_it_expects()
    {
        var pkgbuild = Read("packaging", "arch", "PKGBUILD.template");
        var workflow = Read(".github", "workflows", "release.yml");

        Assert.Matches("(?m)^pkgname=markdownblaze$", pkgbuild);
        Assert.Contains("provides=('MarkdownBlaze')", pkgbuild, StringComparison.Ordinal);
        Assert.Contains("conflicts=('MarkdownBlaze')", pkgbuild, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^source=\(""\$pkgname-\$pkgver\.tar\.gz::", pkgbuild);

        // source=("$pkgname-$pkgver.tar.gz::...") is markdownblaze-<v>.tar.gz; makepkg finds it in the build folder.
        Assert.Contains("\"$build/markdownblaze-$ver.tar.gz\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("$build/MarkdownBlaze-$ver.tar.gz", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_desktop_entry_names_the_window_class_and_handles_the_scheme()
    {
        var desktop = Read("packaging", "linux", "MarkdownBlaze.desktop");

        Assert.Matches("(?m)^StartupWMClass=MarkdownBlaze$", desktop);
        Assert.Contains("x-scheme-handler/markdown", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void The_macos_bundle_claims_markdown_and_not_every_text_file()
    {
        var plist = Read("packaging", "macos", "Info.plist");

        Assert.Contains("<string>net.daringfireball.markdown</string>", plist, StringComparison.Ordinal);
        Assert.DoesNotContain("<string>public.plain-text</string>", plist, StringComparison.Ordinal);
    }

    // ---- the release ------------------------------------------------------------------------------

    [Fact]
    public void The_release_notes_list_which_files_are_in_the_release()
    {
        var workflow = Read(".github", "workflows", "release.yml");

        foreach (var expected in new[]
        {
            "*-win-x64-setup.exe",
            "*-win-x64.zip",
            "*-win-x64.msix",
            "*.cer",
            "*-linux-x64.tar.gz",
            "*-linux-arm64.tar.gz",
            "*.deb",
            "*.pkg.tar.zst",
            "*-osx-arm64.dmg",
            "*-osx-x64.dmg",
        })
        {
            Assert.Contains("present \"", workflow, StringComparison.Ordinal);
            Assert.Contains($"'{expected}'", workflow, StringComparison.Ordinal);
        }
    }
}
