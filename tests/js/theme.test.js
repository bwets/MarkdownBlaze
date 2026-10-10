import { afterEach, describe, expect, it } from 'vitest';
import { applyContentTheme, contentIsDark, isDark, mermaidTheme, setTheme } from '../../src/MarkdownBlaze/wwwroot/js/theme.js';

const html = document.documentElement;
afterEach(() => { html.className = ''; });

describe('isDark', () => {
  it('follows an explicit app theme', () => {
    html.className = 'theme-dark';
    expect(isDark()).toBe(true);
    html.className = 'theme-light';
    expect(isDark()).toBe(false);
  });

  it('follows the system when the theme is "system"', () => {
    const real = window.matchMedia;
    window.matchMedia = q => ({ matches: q.includes('dark') });
    html.className = 'theme-system';
    expect(isDark()).toBe(true);
    window.matchMedia = () => ({ matches: false });
    expect(isDark()).toBe(false);
    window.matchMedia = real;
  });
});

describe('content themes', () => {
  it('a content theme decides whether the document is dark, whatever the app theme', () => {
    html.className = 'theme-light';
    applyContentTheme('night', true);
    expect(html.classList.contains('ct-night')).toBe(true);
    expect(contentIsDark()).toBe(true);
    expect(mermaidTheme()).toBe('dark');
  });

  it('"auto" removes the content theme and the document follows the app again', () => {
    applyContentTheme('book', false);
    applyContentTheme('auto', false);
    expect([...html.classList].some(c => c.startsWith('ct-') || c === 'ctl' || c === 'ctd')).toBe(false);
    html.className = 'theme-dark';
    expect(contentIsDark()).toBe(true);
  });

  it('changing the theme replaces the old one', () => {
    applyContentTheme('book', false);
    applyContentTheme('night', true);
    expect(html.classList.contains('ct-book')).toBe(false);
    expect(html.classList.contains('ctl')).toBe(false);
  });

  it('setTheme swaps the app theme class and defaults to system', () => {
    setTheme('Dark');
    expect(html.className).toBe('theme-dark');
    setTheme(undefined);
    expect(html.className).toBe('theme-system');
  });
});
