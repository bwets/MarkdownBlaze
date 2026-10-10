import { describe, expect, it } from 'vitest';
import { fillLastSheet, fitScale, layOut, mm, sheetGeometry } from '../../src/MarkdownBlaze/wwwroot/js/pagination.js';

// A fake layout: blocks stacked one under the other, each with the height it would have. Setting a
// block's marginTop pushes it, and everything below it, down — which is all the browser's flow does
// for the pagination walk (margins between these blocks do not collapse here).
function fakeFlow(heights) {
  const state = heights.map(height => ({ height, margin: 0 }));
  return state.map((b, i) => ({
    style: {
      set marginTop(v) { b.margin = parseFloat(v); },
      get marginTop() { return b.margin + 'px'; },
    },
    get offsetHeight() { return b.height; },
    get offsetTop() {
      return state.slice(0, i + 1).reduce((y, x) => y + x.margin, 0) + state.slice(0, i).reduce((y, x) => y + x.height, 0);
    },
    margin: () => b.margin,
  }));
}

// 1000px sheets with a 100px margin and a 20px gap: content runs from 100 to 900 on the first sheet.
const geometry = { w: 800, h: 1000, m: 100, gap: 20 };

describe('sheetGeometry', () => {
  it('turns the page .NET describes into pixels', () => {
    const g = sheetGeometry({ widthMm: 210, heightMm: 297, marginMm: 15, gapPx: 16 });
    expect(g).toEqual({ w: mm(210), h: mm(297), m: mm(15), gap: 16 });
    expect(g.w).toBe(794);
    expect(g.h).toBe(1123);
  });
});

describe('layOut', () => {
  it('leaves a document that fits on one sheet alone', () => {
    const blocks = fakeFlow([100, 200, 300]);
    expect(layOut(blocks, geometry)).toBe(1);
    expect(blocks.map(b => b.margin())).toEqual([0, 0, 0]);
  });

  it('pushes a block that would cross the bottom of its sheet to the top of the next', () => {
    // The first block ends at 700; the second would end at 1100, past the 900 where the first sheet's
    // content stops.
    const blocks = fakeFlow([700, 400]);
    expect(layOut(blocks, geometry)).toBe(2);
    // The second sheet's content starts at 1 * (1000 + 20) + 100.
    expect(blocks[1].offsetTop).toBe(1120);
  });

  it('lets a block taller than a whole sheet run across sheets rather than leave a page blank', () => {
    const blocks = fakeFlow([3000, 100]);
    expect(layOut(blocks, geometry)).toBe(4);   // it ends on the third sheet, and the last block is on the fourth
    expect(blocks[0].margin()).toBe(0);         // it starts at the top of the first sheet: not pushed anywhere
    expect(blocks[1].offsetTop).toBe(3 * 1020 + 100);
  });

  it('keeps every block inside the content area of its sheet', () => {
    const blocks = fakeFlow(Array(10).fill(500));
    const pages = layOut(blocks, geometry);
    expect(pages).toBe(10); // one 500px block fits on a sheet, and two do not
    for (const b of blocks) {
      const sheet = Math.floor(b.offsetTop / 1020);
      expect(b.offsetTop + b.offsetHeight).toBeLessThanOrEqual(sheet * 1020 + 900);
    }
  });
});

describe('fillLastSheet', () => {
  it('pads a short last sheet out to the full paper', () => {
    // 2 sheets = 2*1020 - 20 = 2020 tall; the document used 1500 -> 520, plus the margin below the text.
    expect(fillLastSheet(2, geometry, 1500)).toBe(620);
  });
  it('adds nothing when the document already reaches that far', () => expect(fillLastSheet(2, geometry, 2020)).toBe(0));
});

describe('fitScale', () => {
  const pane = { clientWidth: 1024, clientHeight: 700 };
  it('fits the width of the sheet to the pane', () => expect(fitScale('width', pane, geometry)).toBeCloseTo((1024 - 24) / 800));
  it('fits the whole sheet when asked', () => expect(fitScale('page', pane, geometry)).toBeCloseTo((700 - 24) / 1000));
  it('never goes below 20% or above 300%', () => {
    expect(fitScale('width', { clientWidth: 10, clientHeight: 10 }, geometry)).toBe(0.2);
    expect(fitScale('width', { clientWidth: 9000, clientHeight: 10 }, geometry)).toBe(3);
  });
});
