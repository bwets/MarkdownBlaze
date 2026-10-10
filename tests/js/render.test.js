import { afterEach, describe, expect, it, vi } from 'vitest';
import { restoreMeasures, stripMathDelims, withSteadyMeasures } from '../../src/MarkdownBlaze/wwwroot/js/render.js';

describe('stripMathDelims', () => {
  it.each([
    ['\\(x^2\\)', 'x^2'],
    ['\\[x^2\\]', 'x^2'],
    ['$$x^2$$', 'x^2'],
    ['$x^2$', 'x^2'],
    ['  $x$  ', 'x'],
    ['plain', 'plain'],
    ['$unclosed', '$unclosed'],
  ])('%s -> %s', (input, expected) => expect(stripMathDelims(input)).toBe(expected));
});

describe('withSteadyMeasures', () => {
  const original = Element.prototype.getBoundingClientRect;
  afterEach(() => { restoreMeasures(); vi.useRealTimers(); });

  it('replaces the prototype for the length of a drawing and puts it back', async () => {
    let during;
    await withSteadyMeasures(() => { during = Element.prototype.getBoundingClientRect; return Promise.resolve(); });
    expect(during).not.toBe(original);
    expect(Element.prototype.getBoundingClientRect).toBe(original);
  });

  it('puts it back when the drawing throws', () => {
    expect(() => withSteadyMeasures(() => { throw new Error('boom'); })).toThrow('boom');
    expect(Element.prototype.getBoundingClientRect).toBe(original);
  });

  it('keeps it until the last of overlapping drawings is done', async () => {
    let finishFirst, finishSecond;
    const first = withSteadyMeasures(() => new Promise(r => { finishFirst = r; }));
    const second = withSteadyMeasures(() => new Promise(r => { finishSecond = r; }));
    finishFirst(); await first;
    expect(Element.prototype.getBoundingClientRect).not.toBe(original);
    finishSecond(); await second;
    expect(Element.prototype.getBoundingClientRect).toBe(original);
  });

  it('puts it back by itself when a drawing never finishes', () => {
    vi.useFakeTimers();
    withSteadyMeasures(() => new Promise(() => {}), 1000); // never settles
    expect(Element.prototype.getBoundingClientRect).not.toBe(original);
    vi.advanceTimersByTime(1001);
    expect(Element.prototype.getBoundingClientRect).toBe(original);
  });

  it('rounds the size of a label to a thousandth and takes the zoom out of it', async () => {
    document.body.innerHTML = '<svg><foreignObject><div id="label"></div></foreignObject></svg>';
    const label = document.getElementById('label');
    Object.defineProperty(label, 'currentCSSZoom', { value: 2 });

    // jsdom does no layout, so the "browser's own" measurement is stood in for; the patch wraps it.
    const real = Element.prototype.getBoundingClientRect;
    Element.prototype.getBoundingClientRect = function () { return new DOMRect(0, 0, 120.0000076, 20); };
    try {
      let seen;
      await withSteadyMeasures(() => { seen = label.getBoundingClientRect(); return Promise.resolve(); });
      expect(seen.width).toBe(60);
      expect(seen.height).toBe(10);
    } finally {
      restoreMeasures();
      Element.prototype.getBoundingClientRect = real;
    }
  });
});
