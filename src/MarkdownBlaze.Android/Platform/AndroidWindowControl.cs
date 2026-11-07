using System.Runtime.Versioning;
using Android.Views;

namespace MarkdownBlaze.Platform;

/// <summary>
/// <see cref="IWindowControl"/> for Android: "full screen" is the immersive mode, with the status and navigation
/// bars hidden (a swipe from the edge brings them back). The calls that hide them exist from Android 11 on; on
/// the releases before that the app never reports full screen and the toggle does nothing.
/// </summary>
internal sealed class AndroidWindowControl : IWindowControl
{
    /// <inheritdoc />
    public bool IsFullScreen { get; private set; }

    /// <inheritdoc />
    public void ToggleFullScreen()
    {
        if (IsFullScreen) _ = ExitFullScreen();
        else Set(fullScreen: true);
    }

    /// <inheritdoc />
    public bool ExitFullScreen()
    {
        if (!IsFullScreen) return false;

        Set(fullScreen: false);
        return true;
    }

    private void Set(bool fullScreen)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return;

        IsFullScreen = fullScreen;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(30)) ApplyFullScreen(fullScreen);
        });
    }

    [SupportedOSPlatform("android30.0")]
    private static void ApplyFullScreen(bool fullScreen)
    {
        var controller = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.InsetsController;
        if (controller is null) return;

        if (fullScreen)
        {
            controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            controller.Hide(WindowInsets.Type.SystemBars());
        }
        else
        {
            controller.Show(WindowInsets.Type.SystemBars());
        }
    }
}
