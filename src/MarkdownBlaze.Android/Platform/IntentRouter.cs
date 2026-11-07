using Android.Content;
using AndroidX.Core.Content;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Platform;

/// <summary>
/// What the app does with the intents it is started or called with: <c>ACTION_VIEW</c> (a file manager, Drive or a mail
/// app opening a document with it) and <c>ACTION_SEND</c> (the share sheet). A document goes to <see cref="DocumentOpener"/>;
/// shared text has no file, and is rendered the way text dropped on the desktop window is. The intent is somebody else's
/// message: only the address and the text are read from it, and every other extra is ignored. The <c>markdown://</c> scheme is
/// not registered on Android, because its addresses carry the desktop's paths.
/// </summary>
internal sealed partial class IntentRouter
{
    private static readonly ILogger Logger = Log.For(nameof(IntentRouter));
    private readonly DocumentOpener _opener;
    private readonly HostInbox _inbox;

    /// <summary>Creates the router.</summary>
    /// <param name="opener">Opens documents.</param>
    /// <param name="inbox">Where shared text goes to reach the viewer.</param>
    public IntentRouter(DocumentOpener opener, HostInbox inbox)
    {
        _opener = opener;
        _inbox = inbox;
    }

    /// <summary>Handles an intent without waiting for it; whatever goes wrong is logged, and the reader is told.</summary>
    /// <param name="intent">The intent the activity was started with, or got in <c>OnNewIntent</c>.</param>
    public void Handle(Intent? intent)
    {
        if (intent is null) return;

        var action = intent.Action;
        LogHandling(Logger, action);
        if (string.Equals(action, Intent.ActionView, StringComparison.Ordinal))
        {
            if (intent.Data is { } uri) Background.Run(() => _opener.OpenAsync(uri), Logger);
        }
        else if (string.Equals(action, Intent.ActionSend, StringComparison.Ordinal))
        {
            HandleSend(intent);
        }
    }

    private void HandleSend(Intent intent)
    {
        // A shared file (EXTRA_STREAM) is a document like any other; a shared piece of text (EXTRA_TEXT) has no file.
        // Some apps send a file together with a line of text about it, so the file wins.
        if (IntentCompat.GetParcelableExtra(intent, Intent.ExtraStream, Java.Lang.Class.FromType(typeof(Android.Net.Uri))) is Android.Net.Uri stream)
        {
            Background.Run(() => _opener.OpenAsync(stream), Logger);
            return;
        }

        var text = intent.GetStringExtra(Intent.ExtraText);
        if (string.IsNullOrEmpty(text))
        {
            LogNothingShared(Logger);
            return;
        }

        _inbox.Post(new IncomingDocument { Text = text, Name = intent.GetStringExtra(Intent.ExtraSubject) });
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling an intent: {Action}")]
    private static partial void LogHandling(ILogger log, string? action);

    [LoggerMessage(Level = LogLevel.Information, Message = "Something was shared with the app that is neither a file nor text")]
    private static partial void LogNothingShared(ILogger log);
}
