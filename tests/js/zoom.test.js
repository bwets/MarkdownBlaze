import { describe, expect, it } from 'vitest';
import { clampZoom } from '../../src/MarkdownBlaze/wwwroot/js/zoom.js';

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
