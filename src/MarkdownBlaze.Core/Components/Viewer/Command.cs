namespace MarkdownBlaze.Components.Viewer;

/// <summary>
/// One thing the viewer can be told to do, by the keyboard, the toolbar or both.
/// </summary>
/// <param name="Id">What the toolbar's buttons name it by (see <see cref="Commands"/>).</param>
/// <param name="Label">What the shortcuts overlay calls it.</param>
/// <param name="Keys">
/// The keys as the overlay shows them: tokens separated by spaces, "+" joining a chord ("Ctrl+Shift+O").
/// Empty for a command with no shortcut, which the overlay leaves out.
/// </param>
/// <param name="Combos">
/// The key combinations the page reports for it, spelled as the page spells them ("ctrl+shift+o", "alt+left",
/// "?"). Empty for one whose keys the page handles itself — the zoom keys only move the document, so they
/// are not worth a round trip — which is still listed so the overlay shows them.
/// </param>
/// <param name="Run">What it does.</param>
internal sealed record Command(string Id, string Label, string Keys, IReadOnlyList<string> Combos, Func<Task> Run);
