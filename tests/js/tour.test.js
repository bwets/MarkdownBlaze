import { describe, expect, it } from 'vitest';
import { applies, textOf } from '../../src/MarkdownBlaze.Core/wwwroot/js/tour.js';

const step = { text: 'Ctrl + P opens this.', touchText: 'The button opens this.', narrowText: 'The menu opens this.' };

describe('what a tour step says', () => {
  it('names the key where there is a keyboard', () => expect(textOf(step, false, false)).toBe('Ctrl + P opens this.'));
  it('names the button where there is none', () => expect(textOf(step, false, true)).toBe('The button opens this.'));
  it('names the menu where the toolbar is cut down', () => expect(textOf(step, true, true)).toBe('The menu opens this.'));
  it('has one text when it has no other', () => expect(textOf({ text: 'Same.' }, true, true)).toBe('Same.'));
});

describe('which steps a window has', () => {
  it('leaves out what is about the sidebar beside the document in a narrow window', () => {
    expect(applies({ wide: true }, true)).toBe(false);
    expect(applies({ wide: true }, false)).toBe(true);
    expect(applies({}, true)).toBe(true);
  });
});
