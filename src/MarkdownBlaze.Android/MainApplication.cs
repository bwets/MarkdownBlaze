using Android.App;
using Android.Runtime;

namespace MarkdownBlaze;

/// <summary>The Android application object: hands the start-up to <see cref="MauiProgram"/>.</summary>
[Application]
internal sealed class MainApplication : MauiApplication
{
    /// <summary>Called by the Android runtime.</summary>
    /// <param name="handle">The native object.</param>
    /// <param name="ownership">Who owns it.</param>
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    { }

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
