using Microsoft.JSInterop;

namespace MarkdownBlaze.Services;

/// <summary>Static entry points the WebView JavaScript calls back into (link clicks, shortcuts, resize).</summary>
public static class JsBridge
{
    public static event Action<string, string>? Link;   // (kind, value): kind = "nav" | "ext"
    public static event Action<string>? Key;            // shortcut combo, e.g. "alt+left"
    public static event Action<int>? SidebarResized;    // new width in px

    [JSInvokable] public static void OnLink(string kind, string value) => Link?.Invoke(kind, value);
    [JSInvokable] public static void OnKey(string combo) => Key?.Invoke(combo);
    [JSInvokable] public static void OnSidebarResized(int width) => SidebarResized?.Invoke(width);
}
