using Microsoft.JSInterop;

namespace MarkdownBlaze.Services;

/// <summary>Static entry points the WebView JavaScript calls back into (link clicks, shortcuts, resize).</summary>
public static class JsBridge
{
    public static event Action<string, string>? Link;         // (kind, value): kind = "nav" | "ext"
    public static event Action<string>? Key;                  // shortcut combo, e.g. "alt+left"
    public static event Action<int>? SidebarResized;          // new width in px
    public static event Action<double>? DocumentZoomed;       // new document zoom, 1 = natural size
    public static event Action<string, string, string>? FileDropped; // (kind, a, b): "path" → a=path; "text" → a=name, b=content
    public static event Action<bool>? TourEnded;              // the guided tour ended; true = put off until next launch
    public static event Action<bool>? TourPrintBar;           // the tour wants the print bar shown (true) or put away

    [JSInvokable] public static void OnLink(string kind, string value) => Link?.Invoke(kind, value);
    [JSInvokable] public static void OnKey(string combo) => Key?.Invoke(combo);
    [JSInvokable] public static void OnSidebarResized(int width) => SidebarResized?.Invoke(width);
    [JSInvokable] public static void OnDocumentZoomed(double zoom) => DocumentZoomed?.Invoke(zoom);
    [JSInvokable] public static void OnFileDrop(string kind, string a, string b) => FileDropped?.Invoke(kind, a, b);
    [JSInvokable] public static void OnTourEnded(bool deferred) => TourEnded?.Invoke(deferred);
    [JSInvokable] public static void OnTourPrintBar(bool open) => TourPrintBar?.Invoke(open);
}
