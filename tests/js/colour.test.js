import { describe, expect, it } from 'vitest';
import { isDarkColour, shift } from '../../src/MarkdownBlaze/wwwroot/js/colour.js';

describe('shift', () => {
  it('moves towards white for a positive amount', () => expect(shift('rgb(100, 100, 100)', 0.5)).toBe('rgb(178, 178, 178)'));
  it('moves towards black for a negative amount', () => expect(shift('rgb(100, 100, 100)', -0.25)).toBe('rgb(75, 75, 75)'));
  it('reads rgba too', () => expect(shift('rgba(0, 0, 0, 1)', 1)).toBe('rgb(255, 255, 255)'));
  it('leaves what it cannot read alone', () => expect(shift('transparent', 0.5)).toBe('transparent'));
});

describe('isDarkColour', () => {
  it.each([
    ['rgb(0, 0, 0)', true],
    ['rgb(30, 30, 30)', true],
    ['rgb(255, 255, 255)', false],
    ['rgb(250, 245, 230)', false],
    ['not a colour', false],
  ])('%s', (colour, expected) => expect(isDarkColour(colour)).toBe(expected));
});
