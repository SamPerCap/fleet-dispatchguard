import { describe, expect, it } from 'vitest';
import { isMadridMidnight, madridDayStart, madridInstant, madridMoment, shiftDate } from './madridTime';

describe('madridInstant', () => {
  it.each([
    ['2026-11-24', '08:00', '2026-11-24T07:00:00.000Z'], // winter
    ['2026-07-15', '08:00', '2026-07-15T06:00:00.000Z'], // summer
    ['2026-03-29', '01:59', '2026-03-29T00:59:00.000Z'], // last minute before the change
    ['2026-03-29', '03:00', '2026-03-29T01:00:00.000Z'], // first one after it
    ['2026-10-25', '01:59', '2026-10-24T23:59:00.000Z'],
    ['2026-10-25', '02:30', '2026-10-25T00:30:00.000Z'], // happens twice: the first one
    ['2026-10-25', '03:00', '2026-10-25T02:00:00.000Z'],
    ['2027-01-01', '00:00', '2026-12-31T23:00:00.000Z'],
  ])('%s %s in Madrid is %s', (date, time, utc) => {
    expect(madridInstant(date, time)?.toISOString()).toBe(utc);
  });
  it.each([
    ['2026-03-29', '02:00'],
    ['2026-03-29', '02:59'],
    ['2026-02-30', '10:00'],
    ['', '10:00'],
    ['2026-11-24', ''],
  ])('%s %s does not exist', (date, time) => expect(madridInstant(date, time)).toBeNull());
});

describe('whole days', () => {
  it.each([
    ['2026-11-24', 24],
    ['2026-10-25', 25],
    ['2026-03-29', 23],
  ])('%s lasts %s h', (date, hours) => {
    const start = madridDayStart(date)!;
    const end = madridDayStart(shiftDate(date, 1))!;
    expect((end.getTime() - start.getTime()) / 3_600_000).toBe(hours);
    expect(isMadridMidnight(start) && isMadridMidnight(end)).toBe(true);
  });
  it('shifts the date across months and years', () => {
    expect(shiftDate('2026-12-31', 1)).toBe('2027-01-01');
    expect(shiftDate('2028-03-01', -1)).toBe('2028-02-29');
  });
  it('midnight is the one of Madrid, to the second', () => {
    expect(isMadridMidnight(new Date('2026-11-24T00:00:00Z'))).toBe(false);
    expect(isMadridMidnight(new Date('2026-11-23T23:00:30Z'))).toBe(false);
  });
});

it('madridMoment shows both hours of the October change as the same time', () => {
  const twice = { date: '2026-10-25', time: '02:30' };
  expect(madridMoment(new Date('2026-10-25T00:30:00Z'))).toEqual(twice);
  expect(madridMoment(new Date('2026-10-25T01:30:00Z'))).toEqual(twice);
});
