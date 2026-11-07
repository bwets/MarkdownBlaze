namespace MarkdownBlaze.Platform;

/// <summary>
/// Tying the app into the operating system's shell for the current user — today the Windows registry's
/// "Open with" entries and <c>markdown://</c> handler. A host registers one only where there is something
/// to register and the reader may do it from Settings; without one the Settings row is not shown.
/// </summary>
internal interface IShellIntegration
{
    /// <summary>Who, if anyone, has registered the app.</summary>
    ShellIntegrationState State { get; }

    /// <summary>Registers the app for this user. False when the system refused.</summary>
    bool RegisterAll();

    /// <summary>Takes the registration away. False when the system refused.</summary>
    bool UnregisterAll();
}
