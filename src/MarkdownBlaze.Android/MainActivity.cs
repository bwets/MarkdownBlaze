using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;
using AndroidX.Core.View;
using MarkdownBlaze.Platform;
using MarkdownBlaze.Services;

namespace MarkdownBlaze;

/// <summary>
/// The app's one screen. It handles the configuration changes itself (a turn of the device, a change of
/// theme, a keyboard, a resized window) instead of being rebuilt by them: the document, its scroll position
/// and the open folder are state of the page, and a rebuilt activity would render it all again.
/// <para>
/// It is the one door for documents from outside, and <c>SingleTask</c> keeps it one: opening another file from the file manager
/// brings this activity to the front and calls <see cref="OnNewIntent"/>, instead of starting a second window with a second
/// history. It offers itself for <c>ACTION_VIEW</c> of Markdown (by media type, and by <c>.md</c> or <c>.markdown</c> in the
/// name for the many providers that call it <c>text/plain</c> or <c>application/octet-stream</c>; <c>content</c> and <c>file</c>
/// addresses) and for <c>ACTION_SEND</c> of text. It does not register the <c>markdown://</c> scheme, whose addresses carry
/// the desktop's paths.
/// </para>
/// </summary>
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density
        | ConfigChanges.Keyboard
        | ConfigChanges.KeyboardHidden)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["content", "file"],
    DataMimeTypes = ["text/markdown", "text/x-markdown"])]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["content", "file"],
    DataHost = "*",
    DataMimeTypes = ["text/plain", "application/octet-stream"],
    DataPathPatterns = [@".*\\.md", @".*\\.markdown"])]
[IntentFilter(
    [Intent.ActionSend],
    Categories = [Intent.CategoryDefault],
    DataMimeTypes = ["text/plain", "text/markdown"])]
internal sealed class MainActivity : MauiAppCompatActivity
{
    private InsetsListener? _insets;
    private BackCallback? _back;

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
#if DEBUG
        // Debug builds only: `am start -n ... --es mb_open <path>` opens a document inside the app's storage, which
        // is how a document was tried before the app could open files from the device.
        DebugStartup.Open = Intent?.GetStringExtra("mb_open");
#endif
        base.OnCreate(savedInstanceState);
        FitTheWindow();

        // A restored activity (the process was killed and the system brought the screen back) still has the intent it
        // was first started with; the document of that intent is already in the history, and is not opened a second time.
        if (savedInstanceState is null) Router()?.Handle(Intent);
    }

    /// <inheritdoc />
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        Router()?.Handle(intent);
    }

    /// <inheritdoc />
    protected override void OnResume()
    {
        base.OnResume();

        // Android has no way to watch a document the app was handed (D5), so the viewer looks again when the app
        // comes back to the front.
        Services()?.GetService<HostInbox>()?.NotifyResumed();
    }

    /// <summary>
    /// The page is drawn edge to edge, under the status bar and the gesture bar (Android 15 and later insist on it, and
    /// so does this for the earlier ones), and the app tells the page how far each bar reaches, so the toolbar and the
    /// bars along the bottom stay clear of them. The bars' icons are dark on a light app and light on a dark one. And the
    /// Back button is the viewer's to answer first.
    /// </summary>
    private void FitTheWindow()
    {
        var window = Services()?.GetService<HostWindow>();
        var decor = Window?.DecorView;
        if (window is null || decor is null) return;

        EdgeToEdge.Enable(this);
        _insets = new InsetsListener(window);
        ViewCompat.SetOnApplyWindowInsetsListener(decor, _insets);
        window.AppearanceChanged += (_, _) => RunOnUiThread(() => ColourSystemBars(window.Dark));
        ColourSystemBars(window.Dark);

        _back = new BackCallback(this, window);
        OnBackPressedDispatcher.AddCallback(this, _back);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _insets?.Dispose();
            _back?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ColourSystemBars(bool dark)
    {
        if (Window?.DecorView is not { } decor) return;
        var controller = WindowCompat.GetInsetsController(Window, decor);
        if (controller is null) return;

        controller.AppearanceLightStatusBars = !dark;
        controller.AppearanceLightNavigationBars = !dark;
    }

    /// <summary>Hands the viewer the size of the bars, in the page's own pixels, whenever the window is laid out again.</summary>
    private sealed class InsetsListener(HostWindow window) : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(Android.Views.View? v, WindowInsetsCompat? insets)
        {
            if (v is null || insets is null) return insets;

            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());
            var density = v.Resources?.DisplayMetrics?.Density ?? 1f;
            if (bars is not null) window.SetInsets(new WindowInsets(bars.Top / density, bars.Right / density, bars.Bottom / density, bars.Left / density));
            return ViewCompat.OnApplyWindowInsets(v, insets);
        }
    }

    /// <summary>
    /// Back: the viewer puts away what is open, or goes back in the history, and says so; with nothing to do the app is left, as
    /// the system leaves an app at its first screen. The viewer answers on its own thread, so the callback takes every press
    /// and does that itself.
    /// </summary>
    private sealed class BackCallback(MainActivity activity, HostWindow window) : OnBackPressedCallback(enabled: true)
    {
        public override void HandleOnBackPressed() => Background.Run(AnswerAsync);

        private async Task AnswerAsync()
        {
            var handled = await window.HandleBackAsync().ConfigureAwait(true);
            if (handled) return;

            // What Back does on the first screen of an app: since Android 12 it sends the app to the background (so a
            // second launch finds it as it was), before that it closes it.
            if (OperatingSystem.IsAndroidVersionAtLeast(31)) _ = activity.MoveTaskToBack(nonRoot: true);
            else activity.Finish();
        }
    }

    private static IServiceProvider? Services() => IPlatformApplication.Current?.Services;
    private static IntentRouter? Router() => Services()?.GetService<IntentRouter>();
}
