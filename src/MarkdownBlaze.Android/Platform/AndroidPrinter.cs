using Android.Content;
using Android.OS;
using Android.Print;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;
using AndroidWebView = Android.Webkit.WebView;

namespace MarkdownBlaze.Platform;

/// <summary>
/// <see cref="IPrinter"/> for Android. The WebView cannot print through <c>window.print()</c>; the page is handed to the system's
/// print framework instead, which shows its own dialog (with "Save as PDF" among the printers) and draws the page as the dialog
/// asks for it. The WebView renders its content lazily, while the framework lays out and writes the pages, which is after the
/// dialog is up. So the answer is held back until the framework has finished with the adapter: the page restores its own theme
/// and view only then (js/print.js), and the sheets are the print theme and not the screen.
/// </summary>
internal sealed partial class AndroidPrinter : IPrinter
{
    private static readonly ILogger Logger = Log.For(nameof(AndroidPrinter));

    /// <summary>The page margin in thousandths of an inch: 15 mm, the <c>@page</c> margin of the print rules. The WebView lays the page out with the margins the print service asks for, not with <c>@page</c>, and a PDF printer asks for none.</summary>
    private const int MarginMils = 591;

    private AndroidWebView? _webView;

    /// <summary>Tells the printer which WebView is the page; <c>BlazorWebView</c> makes it after the printer is created.</summary>
    /// <param name="webView">The page's WebView.</param>
    public void Attach(AndroidWebView webView) => _webView = webView;

    /// <inheritdoc />
    public async ValueTask<bool> PrintAsync()
    {
        var webView = _webView;
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (webView is null || activity is null) return false;

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            try
            {
                if (activity.GetSystemService(Context.PrintService) is not PrintManager manager)
                {
                    _ = finished.TrySetResult();
                    return;
                }

                // The title of the page names the job (and the PDF file); A4 is what the page view draws, and the dialog can change it.
                var jobName = string.IsNullOrWhiteSpace(webView.Title) ? "MarkdownBlaze" : webView.Title;
                // Not chained: the binding's nullability of these members differs between Android workload versions,
                // and a chain would need a '!' that one version calls necessary and the other unnecessary.
                var builder = new PrintAttributes.Builder();
                if (PrintAttributes.MediaSize.IsoA4 is { } a4) _ = builder.SetMediaSize(a4);
                _ = builder.SetMinMargins(new PrintAttributes.Margins(MarginMils, MarginMils, MarginMils, MarginMils));
                var attributes = builder.Build();

                // The sheets are white paper: the WebView paints its background behind them, and the dark one of a dark app would show in the margins.
                webView.SetBackgroundColor(Android.Graphics.Color.White);
                LogStarted(Logger, jobName);
                _ = manager.Print(jobName, new FinishedAdapter(webView.CreatePrintDocumentAdapter(jobName), () => Finish(webView, finished)), attributes);
            }
            catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
            {
                LogFailed(Logger, ex);
                _ = finished.TrySetResult();
            }
        });

        await finished.Task.ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Passes everything to the WebView's own adapter and says when the framework is done with it: after the pages are written,
    /// after the dialog is cancelled, and after a failure all end in <see cref="OnFinish"/>.
    /// </summary>
    private sealed class FinishedAdapter(PrintDocumentAdapter inner, Action finish) : PrintDocumentAdapter
    {
        public override void OnStart() => inner.OnStart();

        public override void OnLayout(
            PrintAttributes? oldAttributes,
            PrintAttributes? newAttributes,
            CancellationSignal? cancellationSignal,
            LayoutResultCallback? callback,
            Bundle? extras) =>
            inner.OnLayout(oldAttributes, WithMargins(newAttributes), cancellationSignal, callback, extras);

        public override void OnWrite(
            PageRange[]? pages,
            ParcelFileDescriptor? destination,
            CancellationSignal? cancellationSignal,
            WriteResultCallback? callback) =>
            inner.OnWrite(pages, destination, cancellationSignal, callback);

        /// <summary>The attributes the print service asked for, with at least the page margin on every side.</summary>
        private static PrintAttributes? WithMargins(PrintAttributes? asked)
        {
            if (asked?.GetMediaSize() is not { } size) return asked;

            var min = asked.MinMargins;
            using var margins = new PrintAttributes.Margins(
                Math.Max(MarginMils, min?.LeftMils ?? 0),
                Math.Max(MarginMils, min?.TopMils ?? 0),
                Math.Max(MarginMils, min?.RightMils ?? 0),
                Math.Max(MarginMils, min?.BottomMils ?? 0));
            using var builder = new PrintAttributes.Builder();
            _ = builder.SetMediaSize(size);
            _ = builder.SetMinMargins(margins);
            _ = builder.SetColorMode(asked.ColorMode);
            _ = builder.SetDuplexMode(asked.DuplexMode);
            if (asked.GetResolution() is { } resolution) _ = builder.SetResolution(resolution);
            return builder.Build();
        }

        public override void OnFinish()
        {
            try
            {
                inner.OnFinish();
            }
            finally
            {
                finish();
            }
        }
    }

    private static void Finish(AndroidWebView webView, TaskCompletionSource finished)
    {
        MainPage.UseSplashColour(webView);
        LogFinished(Logger);
        _ = finished.TrySetResult();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Print job '{Job}' handed to the print service")]
    private static partial void LogStarted(ILogger log, string job);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The print service is done with the page")]
    private static partial void LogFinished(ILogger log);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The page could not be handed to the print service")]
    private static partial void LogFailed(ILogger log, Exception ex);
}
