using Android.App;
using Android.Content;
using AndroidX.Activity;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;

namespace MarkdownBlaze.Platform;

/// <summary>
/// Starts one of the system's choosers (a document, a folder) and waits for the reader's answer. The activity-result
/// registry of AndroidX does the work the deprecated <c>StartActivityForResult</c> and <c>OnActivityResult</c> pair did,
/// without the activity having to know which request an answer belongs to: each call registers its own one-shot callback.
/// </summary>
internal static class ActivityResults
{
    /// <summary>Shows the chooser <paramref name="intent"/> describes and returns what the reader chose, or null when they backed out.</summary>
    /// <param name="intent">An <c>ACTION_OPEN_DOCUMENT</c> or <c>ACTION_OPEN_DOCUMENT_TREE</c> intent.</param>
    /// <returns>The result intent, whose <see cref="Intent.Data"/> is the chosen address; null for cancel.</returns>
    /// <exception cref="InvalidOperationException">There is no activity in front to show it from.</exception>
    public static async Task<Intent?> LaunchAsync(Intent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity as ComponentActivity
            ?? throw new InvalidOperationException("There is no activity to show the chooser from.");

        var answer = new TaskCompletionSource<Intent?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var callback = new Callback(answer);
            callback.Launcher = activity.ActivityResultRegistry.Register(
                "markdownblaze-" + Guid.NewGuid().ToString("N"),
                new ActivityResultContracts.StartActivityForResult(),
                callback);
            callback.Launcher.Launch(intent);
        })
            .ConfigureAwait(false);

        return await answer.Task.ConfigureAwait(false);
    }

    /// <summary>The one-shot callback of a request: completes the task, then gives the registration back.</summary>
    private sealed class Callback(TaskCompletionSource<Intent?> answer) : Java.Lang.Object, IActivityResultCallback
    {
        public ActivityResultLauncher? Launcher { get; set; }

        public void OnActivityResult(Java.Lang.Object? result)
        {
            Launcher?.Unregister();
            var outcome = result as ActivityResult;
            _ = answer.TrySetResult(outcome?.ResultCode == (int)Result.Ok ? outcome.Data : null);
        }
    }
}
