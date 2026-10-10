import { describe, expect, it } from 'vitest';
import { clampWidth, defaultWidth, minWidth } from '../../src/MarkdownBlaze/wwwroot/js/sidebar.js';

describe('sidebar width', () => {
  it('the minimum is the left toolbar plus the header padding', () => expect(minWidth(211.2)).toBe(220));
  it('falls back when the toolbar cannot be measured', () => {
    expect(minWidth(0)).toBe(150);
    expect(minWidth(-3)).toBe(150);
  });

  it('opens at the usual width when the toolbar is narrow', () => expect(defaultWidth(150)).toBe(280));
  it('opens wider than the toolbar when the toolbar is wide', () => expect(defaultWidth(300)).toBe(364));

  it('is held between the minimum and the maximum while dragged', () => {
    expect(clampWidth(100, 220)).toBe(220);
    expect(clampWidth(400, 220)).toBe(400);
    expect(clampWidth(5000, 220)).toBe(700);
  });
});
