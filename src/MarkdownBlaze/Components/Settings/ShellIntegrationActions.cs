using MarkdownBlaze.Platform.Windows;

namespace MarkdownBlaze.Components.Settings;

/// <summary>
/// What Settings does about Windows integration, behind a type that exists on every platform:
/// <see cref="ShellIntegration"/> is Windows-only and is only in the container there, so the component
/// asks this instead, and it answers "not available" everywhere the row would mean nothing — on Linux
/// and macOS (their packages register the handler themselves) and in the Microsoft Store build, whose
/// manifest already does and whose registry writes would not reach Explorer anyway.
/// </summary>
internal sealed class ShellIntegrationActions
{
    private readonly ShellIntegration? _shell;

    public ShellIntegrationActions(IServiceProvider services)
        : this(OperatingSystem.IsWindows() ? services.GetService(typeof(ShellIntegration)) as ShellIntegration : null, PackageIdentity.IsPackaged)
    { }

    /// <summary>Creates the actions over a given integration, so that a test can supply one that writes somewhere harmless.</summary>
    /// <param name="shell">The integration; null where there is none (not Windows).</param>
    /// <param name="packaged">Whether the app runs from an MSIX package.</param>
    internal ShellIntegrationActions(ShellIntegration? shell, bool packaged)
    {
        _shell = packaged ? null : shell;
    }

    /// <summary>Whether the row is shown at all: Windows, and not packaged.</summary>
    public bool IsAvailable => _shell is not null;

    /// <summary>Who has registered the app; <see cref="ShellIntegrationState.NotRegistered"/> where it is not available.</summary>
    public ShellIntegrationState State =>
        OperatingSystem.IsWindows() && _shell is not null ? _shell.State : ShellIntegrationState.NotRegistered;

    /// <summary>Registers the app for this user. False when it is not available or the registry refused.</summary>
    public bool Add() => OperatingSystem.IsWindows() && _shell?.RegisterAll() == true;

    /// <summary>Takes the registration away. False when it is not available or the registry refused.</summary>
    public bool Remove() => OperatingSystem.IsWindows() && _shell?.UnregisterAll() == true;
}
