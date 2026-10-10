namespace MarkdownBlaze.Interop;

/// <summary>
/// What happens in the page that the app has to answer to. <see cref="ViewerInterop"/> receives each of
/// these as a call from the page's JavaScript and hands it on; everything the page reports is a
/// <i>request</i> — the page shows an untrusted document — and the implementation checks it.
/// </summary>
internal interface IPageEvents
{
    /// <summary>A link was clicked: <paramref name="kind"/> is "nav" or "ext", <paramref name="value"/> the target.</summary>
    void OnLink(string kind, string value);

    /// <summary>A file was dropped onto the window: "path" → a=path; "text" → a=name, b=content.</summary>
    void OnFileDrop(string kind, string a, string b);

    /// <summary>A shortcut was pressed, spelled as the command registry spells it ("alt+left").</summary>
    Task OnKeyAsync(string combo);

    /// <summary>The sidebar was dragged to a new width in px.</summary>
    void OnSidebarResized(int width);

    /// <summary>The document was zoomed; 1 = natural size.</summary>
    void OnDocumentZoomed(double zoom);

    /// <summary>The guided tour ended; <paramref name="deferred"/> = put off until next launch.</summary>
    void OnTourEnded(bool deferred);

    /// <summary>The tour wants the print bar shown (true) or put away.</summary>
    Task OnTourPrintBarAsync(bool open);
}
