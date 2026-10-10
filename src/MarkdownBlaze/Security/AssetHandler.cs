using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Security;

/// <summary>
/// Serves the pictures of the open document to the page: <c>mdasset://doc/&lt;token&gt;/&lt;path&gt;</c>,
/// where the token is the one the renderer put in the address of this render's images (see
/// <see cref="AssetScope"/>) and the path is the one the document wrote, percent-escaped as a single
/// segment.
/// <para>
/// This replaces putting every image into the page as base64, which decoded, re-encoded and parsed
/// every byte of every picture for each render. Now the HTML carries a short address per image, and the
/// WebView asks for the bytes itself.
/// </para>
/// <para>
/// The page is the caller and a document steers it, so this is a trust boundary of its own, and the
/// rules are not written down a second time: whether an image may be shown is
/// <see cref="LocalPath.TryOpenImage"/>, the same call the renderer makes when it decides between a
/// picture and a placeholder. What is added here is the token: a request for any render but the one on
/// the page — a document that kept an address from before it was replaced, or guessed one — is refused.
/// Everything refused is answered with nothing at all, which the WebView turns into a 404.
/// </para>
/// </summary>
internal sealed partial class AssetHandler(AssetScope scope)
{
    private static readonly ILogger Logger = Log.For(nameof(AssetHandler));

    /// <summary>The URL scheme; also what the page's Content-Security-Policy names in <c>img-src</c>.</summary>
    public const string Scheme = "mdasset";
    private const string Prefix = Scheme + "://doc/";

    /// <summary>The address of an image of the render with <paramref name="token"/>, for the path the document wrote.</summary>
    /// <param name="token">The token of the render (see <see cref="AssetGrant"/>).</param>
    /// <param name="decodedPath">The image path as the document wrote it, percent-decoded.</param>
    public static string UrlFor(string token, string decodedPath) =>
        // One escaped segment, "/" included. A browser resolves "." and ".." segments in a URL before it
        // makes the request, so a path left as segments (../images/a.png) would arrive with the token
        // chopped off; written as one segment it arrives as written.
        Prefix + token + "/" + Uri.EscapeDataString(decodedPath);

    /// <summary>
    /// Answers one <c>mdasset://</c> request. PhotinoX calls this (through the lambda in <c>Program</c>) on
    /// the WebView's thread for every request the page makes, and reads the stream to its end. A null
    /// stream is how PhotinoX is told "no such resource": the page sees a 404 and the <c>&lt;img&gt;</c>
    /// its alt text.
    /// </summary>
    /// <param name="url">The full request URL.</param>
    /// <param name="contentType">The image's media type; <c>text/plain</c> for a refusal, which has no body.</param>
    public Stream? Serve(string url, out string? contentType)
    {
        contentType = "text/plain";
        if (!TryParse(url, out var token, out var decodedPath))
        {
            LogRefused(Logger, url, "not an address this app made");
            return null;
        }

        if (!scope.TryGetBaseFolder(token, out var baseFolder))
        {
            // Both a document that was replaced and a token nobody issued land here, deliberately
            // indistinguishable: a refusal says nothing about which tokens have existed.
            LogRefused(Logger, url, "not the render on the page");
            return null;
        }

        // The document's total budget is a matter for the render, which knows all its images; a request
        // is for one, and the cap for one image applies.
        FileStream? stream = null;
        try
        {
            var outcome = LocalPath.TryOpenImage(decodedPath, baseFolder, LocalPath.MaxImageBytes, long.MaxValue, out stream, out var mediaType);
            if (outcome != LocalPath.ImageOutcome.Allowed)
            {
                LogRefusedByPolicy(Logger, url, outcome);
                return null;
            }

            contentType = mediaType;
            var allowed = stream;
            stream = null; // the caller owns it now, and the finally below must not close it
            return allowed;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    /// <summary>
    /// Splits <c>mdasset://doc/&lt;token&gt;/&lt;escaped path&gt;</c>. The path may be one escaped segment
    /// (what <see cref="UrlFor"/> writes) or several; it is unescaped once and handed on as it would
    /// be from a document, so every rule applies to it whichever way it was written.
    /// </summary>
    private static bool TryParse(string url, out string token, out string decodedPath)
    {
        token = string.Empty;
        decodedPath = string.Empty;
        if (!url.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var rest = url.AsSpan(Prefix.Length);
        var slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1) return false;

        token = rest[..slash].ToString();
        decodedPath = Uri.UnescapeDataString(rest[(slash + 1)..].ToString());
        return decodedPath.Length > 0;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Refused the image request {Url}: {Reason}")]
    private static partial void LogRefused(ILogger log, string url, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Refused the image request {Url}: {Outcome}")]
    private static partial void LogRefusedByPolicy(ILogger log, string url, LocalPath.ImageOutcome outcome);
}
