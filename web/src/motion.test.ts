import { describe, expect, it } from 'vitest';
import { decimalsOf, easeOutCubic, tween } from './motion';

describe('tween', () => {
  it('tween_Ends_ReturnsExactOriginAndTarget', () => {
    expect(tween(12, 13, 0)).toBe(12);
    expect(tween(12, 13, 1)).toBe(13);
  });

  it('tween_OutOfRange_IsClamped', () => {
    expect(tween(10, 20, -0.5)).toBe(10);
    expect(tween(10, 20, 3)).toBe(20);
  });

  it('tween_Halfway_DeceleratesAndRoundsToIntegers', () => {
    // easeOutCubic(0.5) = 0.875 → 0 + 100 · 0.875 = 87.5 → 88
    expect(tween(0, 100, 0.5)).toBe(88);
    expect(Number.isInteger(tween(3, 7, 0.37))).toBe(true);
  });

  it('tween_Decrease_InterpolatesDownwards', () => {
    const halfway = tween(50, 40, 0.5);
    expect(halfway).toBeLessThan(50);
    expect(halfway).toBeGreaterThan(40);
  });

  it('tween_WithDecimals_KeepsTheRequestedDecimals', () => {
    expect(tween(12.5, 13.5, 0.5, 1)).toBe(13.4); // 12.5 + 0.875
  });

  it('easeOutCubic_IsMonotonicAndEndsAtOne', () => {
    expect(easeOutCubic(0)).toBe(0);
    expect(easeOutCubic(1)).toBe(1);
    expect(easeOutCubic(0.25)).toBeLessThan(easeOutCubic(0.75));
  });
});

describe('decimalsOf', () => {
  it('decimalsOf_IntegersAndDecimals', () => {
    expect(decimalsOf(42)).toBe(0);
    expect(decimalsOf(12.5)).toBe(1);
    expect(decimalsOf(0.125)).toBe(3);
    expect(decimalsOf(1.23456)).toBe(3);
    expect(decimalsOf(Number.NaN)).toBe(0);
  });
});
