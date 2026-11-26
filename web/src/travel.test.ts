import { describe, expect, it } from 'vitest';
import { addMinutes, formatDuration, suggestedEnd, travelWarning, type TravelTime } from './travel';

const hist = (Minutes: number, Samples = 12): TravelTime => ({ Minutes, Source: 'Historical', Samples });

describe('travel', () => {
  it('addMinutes adds and does not cross midnight', () => {
    expect(addMinutes('08:00', 60)).toBe('09:00');
    expect(addMinutes('08:45', 30)).toBe('09:15');
    expect(addMinutes('23:30', 30)).toBeNull();
    expect(addMinutes('', 30)).toBeNull();
    expect(addMinutes('08:00', 0)).toBeNull();
  });

  it('formatDuration', () => {
    expect(formatDuration(60)).toBe('1 h 00 min');
    expect(formatDuration(95)).toBe('1 h 35 min');
    expect(formatDuration(30)).toBe('30 min');
  });

  it('suggestedEnd only if the arrival has not been touched and there is a figure', () => {
    expect(suggestedEnd('08:00', false, hist(60))).toBe('09:00');
    expect(suggestedEnd('08:00', true, hist(60))).toBeNull();
    expect(suggestedEnd('08:00', false, undefined)).toBeNull();
    expect(suggestedEnd('08:00', false, { Minutes: null, Source: 'None', Samples: 0 })).toBeNull();
  });

  it('travelWarning warns if the arrival is below the average (historical)', () => {
    expect(travelWarning(hist(60), 'A', 'B', '08:00', '08:30')).toBe(
      'El tiempo medio registrado A→B es 1 h 00 min (12 viajes); has puesto 30 min.',
    );
    expect(travelWarning(hist(60, 1), 'A', 'B', '08:00', '08:30')).toContain('(1 viaje)');
  });

  it('travelWarning does not warn at the limit, above it or with invalid times', () => {
    expect(travelWarning(hist(60), 'A', 'B', '08:00', '09:00')).toBeNull();
    expect(travelWarning(hist(60), 'A', 'B', '08:00', '10:00')).toBeNull();
    expect(travelWarning(hist(60), 'A', 'B', '09:00', '08:00')).toBeNull();
    expect(travelWarning(undefined, 'A', 'B', '08:00', '08:30')).toBeNull();
  });

  it('travelWarning with an estimate says so', () => {
    const warning = travelWarning({ Minutes: 60, Source: 'Estimated', Samples: 0 }, 'A', 'B', '08:00', '08:30');
    expect(warning).toContain('tiempo estimado');
    expect(warning).toContain('sin viajes registrados');
  });
});
