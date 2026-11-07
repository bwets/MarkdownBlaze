namespace MarkdownBlaze.Platform;

/// <summary>What a host says about itself: the toolkit it runs the page in, as Settings names it in its about line.</summary>
/// <param name="Toolkit">The window toolkit, for example <c>Photino</c> or <c>.NET MAUI</c>.</param>
internal sealed record HostInfo(string Toolkit);
