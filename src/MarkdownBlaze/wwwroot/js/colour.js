// Colour arithmetic for the page view: which colour the paper is, whether it is dark, and a desk to
// put it on. All of it works on the `rgb()` / `rgba()` strings getComputedStyle returns.

/// The nearest background colour that actually paints something, starting at the element itself.
export function opaqueBackground(el) {
    for (let node = el; node; node = node.parentElement) {
        const colour = getComputedStyle(node).backgroundColor;
        const parts = colour.match(/[\d.]+/g);
        if (parts && (parts.length < 4 || parseFloat(parts[3]) > 0)) return colour;
    }
    return '#ffffff';
}

export function isDarkColour(colour) {
    const p = (colour.match(/[\d.]+/g) || []).map(Number);
    if (p.length < 3) return false;
    return 0.2126 * p[0] + 0.7152 * p[1] + 0.0722 * p[2] < 128;
}

/// Moves a colour towards black or white by a fixed step — used to set the desk apart from the paper.
export function shift(colour, amount) {
    const p = (colour.match(/[\d.]+/g) || []).map(Number);
    if (p.length < 3) return colour;
    const to = amount > 0 ? 255 : 0;
    const t = Math.abs(amount);
    const mix = c => Math.round(c + (to - c) * t);
    return `rgb(${mix(p[0])}, ${mix(p[1])}, ${mix(p[2])})`;
}
