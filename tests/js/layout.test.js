import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { insetPx, isTouchHost, NARROW_QUERY, NO_KEYBOARD_QUERY, setInsets } from '../../src/MarkdownBlaze.Core/wwwroot/js/layout.js';

const css = readFileSync(resolve(process.cwd(), 'src/MarkdownBlaze.Core/wwwroot/css/app.css'), 'utf8');

describe('the breakpoint', () => {
  // The stylesheet cannot read the page's constant, so each query is written in both places.
  it('is the same in the stylesheet as in the page', () => expect(css).toContain(`@media ${NARROW_QUERY} {`));
  it('names a phone upright and on its side, and not a tablet', () => {
    expect(NARROW_QUERY).toContain('max-width: 720px');
    expect(NARROW_QUERY).toContain('max-height: 500px');
  });
  it('is for devices whose main input is a finger when there is no keyboard', () => {
    expect(NO_KEYBOARD_QUERY).toBe('(hover: none) and (pointer: coarse)');
    expect(css).toContain(`@media ${NO_KEYBOARD_QUERY} {`);
  });
});

describe('insets from the host', () => {
  it('are pixels', () => expect(insetPx(24)).toBe('24px'));
  it('keep a fraction of a pixel, rounded', () => expect(insetPx(24.456)).toBe('24.46px'));
  it('are never negative or not a number', () => {
    expect(insetPx(-4)).toBe('0px');
    expect(insetPx(NaN)).toBe('0px');
    expect(insetPx(undefined)).toBe('0px');
    expect(insetPx('x')).toBe('0px');
  });
  it('are written on the root element, where the stylesheet reads them', () => {
    setInsets(24, 0, 16, 8);
    const style = document.documentElement.style;
    expect(style.getPropertyValue('--safe-top')).toBe('24px');
    expect(style.getPropertyValue('--safe-right')).toBe('0px');
    expect(style.getPropertyValue('--safe-bottom')).toBe('16px');
    expect(style.getPropertyValue('--safe-left')).toBe('8px');
  });
});

describe('the touch host', () => {
  // Only the Android page says so; a desktop machine with a touch screen is still a desktop.
  it('is the page that marks its root element', () => {
    expect(isTouchHost()).toBe(false);
    document.documentElement.setAttribute('data-touch', '');
    expect(isTouchHost()).toBe(true);
    document.documentElement.removeAttribute('data-touch');
  });
});
