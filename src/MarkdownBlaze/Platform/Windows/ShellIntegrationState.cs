namespace MarkdownBlaze.Platform.Windows;

/// <summary>Who, if anyone, has tied MarkdownBlaze into Windows for this user.</summary>
internal enum ShellIntegrationState
{
    /// <summary>Nothing is registered, or what is there points at some other copy of the app (or is only part of the set).</summary>
    NotRegistered = 0,

    /// <summary>The app registered itself, on request (Settings or <c>--register</c>). Only the app can take it away again.</summary>
    RegisteredByApp = 1,

    /// <summary>The installer did, and left its marker. The installer's uninstaller removes it; the app leaves it alone.</summary>
    InstalledByInstaller = 2,
}
