import { describe, expect, it } from 'vitest';
import { clampZoom, pinchZoom, scrollAfterZoom } from '../../src/MarkdownBlaze.Core/wwwroot/js/zoom.js';

describe('clampZoom', () => {
  it('holds the zoom between the limits .NET gave', () => {
    expect(clampZoom(0.1, 0.4, 4)).toBe(0.4);
    expect(clampZoom(9, 0.4, 4)).toBe(4);
    expect(clampZoom(1.5, 0.4, 4)).toBe(1.5);
  });
  it('takes what is not a number for 100%', () => {
    expect(clampZoom('abc', 0.4, 4)).toBe(1);
    expect(clampZoom(undefined, 0.4, 4)).toBe(1);
  });
});

describe('pinchZoom', () => {
  it('multiplies the zoom by how much further apart the fingers are', () => {
    expect(pinchZoom(1, 100, 200, 0.4, 4)).toBe(2);
    expect(pinchZoom(1.5, 200, 100, 0.4, 4)).toBe(0.75);
  });
  it('stays between the limits', () => {
    expect(pinchZoom(1, 100, 1000, 0.4, 4)).toBe(4);
    expect(pinchZoom(1, 100, 1, 0.4, 4)).toBe(0.4);
  });
  it('changes nothing when the fingers began on one spot', () => {
    expect(pinchZoom(1.2, 0, 150, 0.4, 4)).toBe(1.2);
    expect(pinchZoom(1.2, NaN, 150, 0.4, 4)).toBe(1.2);
  });
});

describe('scrollAfterZoom', () => {
  it('keeps the point under the fingers where it was', () => {
    // 300px down the pane and the fingers 200px from its top: that point is 500px into the document.
    // Doubled, it is 1000px in, and to stay 200px from the top the pane has to be scrolled 800.
    expect(scrollAfterZoom(300, 200, 200, 2)).toBe(800);
  });
  it('follows the fingers as they move', () => {
    expect(scrollAfterZoom(300, 200, 150, 2)).toBe(850);
  });
  it('does nothing at the same zoom with the fingers still', () => expect(scrollAfterZoom(300, 200, 200, 1)).toBe(300));
});
