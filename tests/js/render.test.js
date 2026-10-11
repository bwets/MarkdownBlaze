import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  KATEX_OPTIONS,
  MAX_HIGHLIGHT_CHARS,
  MERMAID_LIMITS,
  highlightCode,
  renderMath,
  restoreMeasures,
  runMermaid,
  stripMathDelims,
  withSteadyMeasures,
} from '../../src/MarkdownBlaze/wwwroot/js/render.js';

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

// SEC-16: what the libraries are allowed to do with a document's diagrams, formulas and code.
describe('runMermaid', () => {
  afterEach(() => { delete window.mermaid; restoreMeasures(); document.body.innerHTML = ''; });

  it('initialises Mermaid in strict mode with its size limits stated, whatever the defaults are', async () => {
    const initialize = vi.fn();
    window.mermaid = { initialize, run: vi.fn(() => Promise.resolve()) };

    await runMermaid('dark');

    expect(initialize).toHaveBeenCalledTimes(1);
    expect(initialize.mock.calls[0][0]).toMatchObject({
      startOnLoad: false,
      theme: 'dark',
      securityLevel: 'strict',
      maxTextSize: 50000,
      maxEdges: 500,
    });
  });

  it('asks again with the same limits every time a diagram is drawn (a theme change draws it again)', async () => {
    const initialize = vi.fn();
    window.mermaid = { initialize, run: vi.fn(() => Promise.resolve()) };

    await runMermaid('dark');
    await runMermaid('default');

    expect(initialize.mock.calls.map(call => call[0].securityLevel)).toEqual(['strict', 'strict']);
  });

  it('keeps the limits where nothing can change them', () => {
    expect(MERMAID_LIMITS).toEqual({ securityLevel: 'strict', maxTextSize: 50000, maxEdges: 500 });
    expect(Object.isFrozen(MERMAID_LIMITS)).toBe(true);
  });
});

describe('renderMath', () => {
  afterEach(() => { delete window.katex; document.body.innerHTML = ''; });

  it('gives every formula the limits: no size over 50 em, 1000 expansions, nothing trusted', () => {
    const render = vi.fn();
    window.katex = { render };
    document.body.innerHTML = '<span class="math">$x^2$</span><div class="math">$$y$$</div><pre><code class="language-math">z</code></pre>';

    renderMath();

    expect(render).toHaveBeenCalledTimes(3);
    for (const [, , options] of render.mock.calls) {
      expect(options).toMatchObject({ maxSize: 50, maxExpand: 1000, trust: false, throwOnError: false });
    }
    const modes = render.mock.calls.map(([tex, , options]) => [tex, options.displayMode]);
    expect(modes).toEqual(expect.arrayContaining([['z', true], ['y', true], ['x^2', false]]));
  });

  it('keeps the options where nothing can change them', () => {
    expect(KATEX_OPTIONS).toEqual({ throwOnError: false, maxSize: 50, maxExpand: 1000, trust: false });
    expect(Object.isFrozen(KATEX_OPTIONS)).toBe(true);
  });
});

describe('highlightCode', () => {
  afterEach(() => { delete window.hljs; document.body.innerHTML = ''; });

  it('highlights a block of code', () => {
    const highlightElement = vi.fn();
    window.hljs = { highlightElement };
    document.body.innerHTML = '<pre><code class="language-js">let x = 1;</code></pre>';

    highlightCode();

    expect(highlightElement).toHaveBeenCalledTimes(1);
    expect(document.querySelector('code').hasAttribute('data-md-skipped')).toBe(false);
  });

  it('leaves a block over 100,000 characters as plain text and marks it', () => {
    const highlightElement = vi.fn();
    window.hljs = { highlightElement };
    document.body.innerHTML = '<pre><code id="small">x</code></pre><pre><code id="big"></code></pre><pre><code id="edge"></code></pre>';
    document.getElementById('big').textContent = 'x'.repeat(MAX_HIGHLIGHT_CHARS + 1);
    document.getElementById('edge').textContent = 'x'.repeat(MAX_HIGHLIGHT_CHARS);

    highlightCode();

    expect(MAX_HIGHLIGHT_CHARS).toBe(100_000);
    expect(document.getElementById('big').getAttribute('data-md-skipped')).toBe('too-large');
    expect(highlightElement).toHaveBeenCalledTimes(2); // the small one and the one at the limit
    expect(highlightElement.mock.calls.map(([code]) => code.id).sort()).toEqual(['edge', 'small']);
    expect(document.getElementById('big').textContent.length).toBe(MAX_HIGHLIGHT_CHARS + 1); // untouched
  });
});
