import { beforeEach, describe, expect, it, vi } from 'vitest';

// print.js is driven through its collaborators: what it asks of the host, and what it does to the page.
const host = vi.hoisted(() => ({ answer: null }));
vi.mock('../../src/MarkdownBlaze.Core/wwwroot/js/bridge.js', () => ({ ask: () => host.answer.promise }));
vi.mock('../../src/MarkdownBlaze.Core/wwwroot/js/pagination.js', () => ({
  getView: () => ({ mode: 'continuous', fit: 'width' }),
  setView: () => {},
  paginate: () => {},
}));
vi.mock('../../src/MarkdownBlaze.Core/wwwroot/js/render.js', () => ({ redrawMermaid: () => Promise.resolve() }));
vi.mock('../../src/MarkdownBlaze.Core/wwwroot/js/theme.js', () => ({ applyContentTheme: () => {}, mermaidTheme: () => 'default' }));
vi.mock('../../src/MarkdownBlaze.Core/wwwroot/js/zoom.js', () => ({ getDocZoom: () => 1, restoreDocZoom: () => {} }));

const { installPrintHooks, print } = await import('../../src/MarkdownBlaze.Core/wwwroot/js/print.js');

const defer = () => {
  let resolve;
  const promise = new Promise(r => { resolve = r; });
  return { promise, resolve };
};
const flush = () => new Promise(r => setTimeout(r, 0));

describe('printing through a host that prints the page itself', () => {
  beforeEach(() => {
    document.documentElement.className = 'theme-dark ct-book';
    window.print = vi.fn();
    host.answer = defer();
    installPrintHooks(new AbortController().signal);
  });

  it('keeps the print state until the host is done, whatever afterprint says', async () => {
    const done = print('book', false);
    await flush();
    expect(document.documentElement.classList.contains('theme-light')).toBe(true);

    window.dispatchEvent(new Event('afterprint')); // the WebView says so after its first look at the page
    expect(document.documentElement.classList.contains('theme-light')).toBe(true);

    host.answer.resolve(true);
    await done;
    expect(document.documentElement.className).toBe('theme-dark ct-book');
    expect(window.print).not.toHaveBeenCalled();
  });

  it('prints the page itself when the host says no, and restores afterwards', async () => {
    const done = print('book', false);
    host.answer.resolve(false);
    await done;
    expect(window.print).toHaveBeenCalledOnce();
    expect(document.documentElement.className).toBe('theme-dark ct-book');
  });
});
