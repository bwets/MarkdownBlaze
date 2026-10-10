import { describe, expect, it } from 'vitest';
import { neighbour } from '../../src/MarkdownBlaze/wwwroot/js/focus.js';
import { sectionAt } from '../../src/MarkdownBlaze/wwwroot/js/position.js';

describe('sectionAt', () => {
  it('is the last heading to have passed the line', () => expect(sectionAt([-300, -20, 150, 600], 100)).toBe(1));
  it('is none before the first heading', () => expect(sectionAt([200, 400], 100)).toBe(-1));
  it('skips a heading that is not on the page', () => expect(sectionAt([-50, null, 40, 300], 100)).toBe(2));
});

describe('neighbour', () => {
  const items = ['a', 'b', 'c'];
  it('steps and wraps round', () => {
    expect(neighbour(items, 'a', 1)).toBe('b');
    expect(neighbour(items, 'c', 1)).toBe('a');
    expect(neighbour(items, 'a', -1)).toBe('c');
  });
  it('starts at an end when nothing has the focus', () => {
    expect(neighbour(items, undefined, 1)).toBe('a');
    expect(neighbour(items, undefined, -1)).toBe('c');
  });
  it('is null for nothing', () => expect(neighbour([], undefined, 1)).toBeNull());
});
