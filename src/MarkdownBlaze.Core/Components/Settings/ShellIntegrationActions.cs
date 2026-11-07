using MarkdownBlaze.Platform;

namespace MarkdownBlaze.Components.Settings;

/// <summary>
/// What Settings does about shell integration, behind a type that exists on every host: the integration
/// itself (<see cref="IShellIntegration"/>) is only in the container where there is one — Windows, outside
/// the Microsoft Store build, whose manifest already registers the app and whose registry writes would
/// not reach Explorer anyway — and this answers "not available" everywhere else, where the row would mean
/// nothing.
/// </summary>
internal sealed class ShellIntegrationActions
{
    private readonly IShellIntegration? _shell;

    public ShellIntegrationActions(IServiceProvider services)
        : this(services.GetService(typeof(IShellIntegration)) as IShellIntegration, packaged: false)
    { }

    /// <summary>Creates the actions over a given integration, so that a test can supply one that writes somewhere harmless.</summary>
    /// <param name="shell">The integration; null where there is none.</param>
    /// <param name="packaged">Whether the app runs from an MSIX package.</param>
    internal ShellIntegrationActions(IShellIntegration? shell, bool packaged)
    {
        _shell = packaged ? null : shell;
    }

    /// <summary>Whether the row is shown at all: there is an integration, and the app is not packaged.</summary>
    public bool IsAvailable => _shell is not null;

    /// <summary>Who has registered the app; <see cref="ShellIntegrationState.NotRegistered"/> where it is not available.</summary>
    public ShellIntegrationState State => _shell?.State ?? ShellIntegrationState.NotRegistered;

    /// <summary>Registers the app for this user. False when it is not available or the system refused.</summary>
    public bool Add() => _shell?.RegisterAll() == true;

    /// <summary>Takes the registration away. False when it is not available or the system refused.</summary>
    public bool Remove() => _shell?.UnregisterAll() == true;
}
