import { describe, expect, it } from 'vitest';
import { formatTime, barPosition, parseApiDate } from './gantt';

const day = new Date('2036-06-03T00:00:00');

describe('barPosition', () => {
  it('MidDay_ProportionalPercent', () => {
    const position = barPosition(new Date('2036-06-03T06:00:00'), new Date('2036-06-03T12:00:00'), day);
    expect(position).toEqual({ left: 25, width: 25, clippedStart: false, clippedEnd: false });
  });
  it('CrossesMidnight_ClippedToDay', () => {
    const position = barPosition(new Date('2036-06-03T22:00:00'), new Date('2036-06-04T02:00:00'), day);
    expect(position?.left).toBeCloseTo(91.667, 2);
    expect(position?.width).toBeCloseTo(8.333, 2);
    expect(position?.clippedEnd).toBe(true);
  });
  it('StartedPreviousDay_ClippedStart', () => {
    const position = barPosition(new Date('2036-06-02T23:00:00'), new Date('2036-06-03T01:00:00'), day);
    expect(position).toMatchObject({ left: 0, clippedStart: true });
  });
  it('OutsideDay_Null', () => {
    expect(barPosition(new Date('2036-06-04T00:00:00'), new Date('2036-06-04T01:00:00'), day)).toBeNull();
  });
});

describe('parseApiDate', () => {
  it('NoOffset_TreatedAsUtc', () => {
    expect(parseApiDate('2036-06-03T08:00:00').toISOString()).toBe('2036-06-03T08:00:00.000Z');
  });
  it('WithZ_Kept', () => {
    expect(parseApiDate('2036-06-03T08:00:00Z').toISOString()).toBe('2036-06-03T08:00:00.000Z');
  });
});

describe('formatTime in Europe/Madrid', () => {
  it('shows Madrid time in summer (UTC+2) and winter (UTC+1)', () => {
    expect(formatTime(new Date('2026-07-01T06:00:00Z'))).toBe('08:00');
    expect(formatTime(new Date('2026-01-15T06:00:00Z'))).toBe('07:00');
  });
});
