import { describe, expect, it } from 'vitest';
import { areaPath, gapSegments, linePath, peakIndex, ringDash, scalePoints, scaleSeries, seriesText } from './chartmath';

describe('scalePoints', () => {
  it('spreads values across the width and maps 0..max to bottom..top', () => {
    const points = scalePoints([0, 5, 10], 100, 20, 0);
    expect(points).toEqual([
      { x: 0, y: 20 },
      { x: 50, y: 10 },
      { x: 100, y: 0 },
    ]);
  });
  it('equal values: at the top from 0, at mid height with fromMin; all 0 on the baseline', () => {
    expect(scalePoints([3, 3, 3], 60, 20, 2).every(p => p.y === 2)).toBe(true);
    expect(scalePoints([3, 3, 3], 60, 20, 2, true).every(p => p.y === 10)).toBe(true);
    expect(scalePoints([0, 0, 0], 60, 20, 2).every(p => p.y === 18)).toBe(true);
  });
  it('a single value draws a flat line edge to edge', () => {
    expect(scalePoints([4], 80, 20, 0)).toEqual([
      { x: 0, y: 0 },
      { x: 80, y: 0 },
    ]);
  });
  it('no values → no points; non-finite values count as 0', () => {
    expect(scalePoints([], 10, 10)).toEqual([]);
    expect(scalePoints([NaN, 10], 10, 10, 0)[0].y).toBe(10);
  });
  it('fromMin uses the minimum as the bottom', () => {
    expect(scalePoints([50, 100], 10, 10, 0, true).map(p => p.y)).toEqual([10, 0]);
  });
});

describe('paths', () => {
  it('line and closed area', () => {
    const pts = [
      { x: 0, y: 5 },
      { x: 10, y: 0 },
    ];
    expect(linePath(pts)).toBe('M0 5 L10 0');
    expect(areaPath(pts, 20)).toBe('M0 5 L10 0 L10 20 L0 20 Z');
    expect(areaPath([], 20)).toBe('');
  });
});

describe('ringDash', () => {
  it('dash is the share of the circumference, clipped to 0–100', () => {
    const { circumference, dash } = ringDash(25, 10);
    expect(circumference).toBeCloseTo(62.83, 2);
    expect(dash).toBeCloseTo(15.71, 2);
    expect(ringDash(140, 10).dash).toBe(ringDash(100, 10).circumference);
    expect(ringDash(-3, 10).dash).toBe(0);
    expect(ringDash(NaN, 10).dash).toBe(0);
  });
});

describe('seriesText and peakIndex', () => {
  it('describes the series for screen readers', () => {
    expect(seriesText('Últimos 3 días', [1, 2, 3])).toBe('Últimos 3 días: 1, 2 y 3');
    expect(seriesText('Hoy', [7])).toBe('Hoy: 7');
    expect(seriesText('Hoy', [])).toBe('Hoy: sin datos');
    expect(seriesText('Cobertura', [50], v => `${v} %`)).toBe('Cobertura: 50 %');
  });
  it('first maximum', () => {
    expect(peakIndex([1, 4, 4, 2])).toBe(1);
    expect(peakIndex([])).toBe(-1);
  });
});

describe('scaleSeries and gapSegments', () => {
  it('shares one scale across series and keeps gaps as null', () => {
    const [first, second] = scaleSeries(
      [
        [0, 10, null],
        [5, null, 0],
      ],
      100,
      20,
      0,
    );
    expect(first).toEqual([{ x: 0, y: 20 }, { x: 50, y: 0 }, null]);
    expect(second).toEqual([{ x: 0, y: 10 }, null, { x: 100, y: 20 }]);
  });
  it('all gaps or all zero stay on the baseline (no fake activity)', () => {
    expect(scaleSeries([[null, null]], 10, 10, 0)[0]).toEqual([null, null]);
    expect(scaleSeries([[0, 0]], 10, 10, 1)[0].map(p => p?.y)).toEqual([9, 9]);
  });
  it('a single slot sits in the middle', () => {
    expect(scaleSeries([[3]], 40, 10, 0)[0]).toEqual([{ x: 20, y: 0 }]);
  });
  it('splits a series at every gap', () => {
    expect(gapSegments([1, 2, null, null, 3, null, 4, 5])).toEqual([[1, 2], [3], [4, 5]]);
    expect(gapSegments([null, null])).toEqual([]);
    expect(gapSegments([])).toEqual([]);
  });
});
