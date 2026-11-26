import { describe, expect, it } from 'vitest';
import { isUnassigned, planningStats, rangeOf, serviceTypeLabel, SERVICE_TYPES, shift, spanIn } from './calendar';

const day = (isoDate: string) => new Date(`${isoDate}T00:00`);

describe('rangeOf', () => {
  it('the week goes from Monday to Monday', () => {
    const range = rangeOf(new Date('2026-11-18T15:00'), 'week'); // Wednesday
    expect(range.from).toEqual(day('2026-11-16'));
    expect(range.to).toEqual(day('2026-11-23'));
    expect(range.days).toHaveLength(7);
  });
  it('a Sunday belongs to the week that started on the previous Monday', () => {
    expect(rangeOf(day('2026-11-22'), 'week').from).toEqual(day('2026-11-16'));
  });
  it('day and month', () => {
    expect(rangeOf(new Date('2026-11-18T15:00'), 'day').days).toHaveLength(1);
    const month = rangeOf(day('2026-02-10'), 'month');
    expect(month.from).toEqual(day('2026-02-01'));
    expect(month.days).toHaveLength(28);
  });
});

describe('shift', () => {
  it('moves one unit of the view', () => {
    expect(shift(day('2026-11-18'), 'day', 1)).toEqual(day('2026-11-19'));
    expect(shift(day('2026-11-18'), 'week', -1)).toEqual(day('2026-11-11'));
    expect(shift(day('2026-01-31'), 'month', 1)).toEqual(day('2026-02-01'));
  });
});

describe('spanIn', () => {
  const week = rangeOf(day('2026-11-16'), 'week');
  it('places in % of the week', () => {
    const span = spanIn(day('2026-11-17'), day('2026-11-18'), week)!;
    expect(span.left).toBeCloseTo(100 / 7);
    expect(span.width).toBeCloseTo(100 / 7);
  });
  it('clips, and discards what falls outside', () => {
    expect(spanIn(day('2026-11-15'), day('2026-11-16'), week)).toBeNull();
    expect(spanIn(day('2026-11-22'), day('2026-11-24'), week)!.width).toBeCloseTo(100 / 7);
  });
});

describe('isUnassigned', () => {
  const trip = { AssignedDriverId: 1, AssignedVehicleId: 2, Status: 'Assigned' };
  it('driver or vehicle missing', () => {
    expect(isUnassigned(trip)).toBe(false);
    expect(isUnassigned({ ...trip, AssignedDriverId: null })).toBe(true);
    expect(isUnassigned({ ...trip, AssignedVehicleId: null })).toBe(true);
  });
  it('cancelled and completed are never pending', () => {
    expect(isUnassigned({ ...trip, AssignedDriverId: null, Status: 'Cancelled' })).toBe(false);
    expect(isUnassigned({ ...trip, AssignedDriverId: null, Status: 'Completed' })).toBe(false);
  });
});

describe('serviceTypeLabel', () => {
  it('translates every ServiceType value to Spanish', () => {
    expect(SERVICE_TYPES.map(serviceTypeLabel)).toEqual(['Regular', 'Discrecional', 'Circuito', 'Traslado aeropuerto']);
  });
  it('falls back to the raw value for an unknown type', () => {
    expect(serviceTypeLabel('Other')).toBe('Other');
  });
});

describe('planningStats', () => {
  const trip = (driverId: number | null, vehicleId: number | null, Status = 'Pending', ForcedDescription: string | null = null) => ({
    AssignedDriverId: driverId,
    AssignedVehicleId: vehicleId,
    Status,
    ForcedDescription,
  });
  it('counts live trips only, coverage and pending add up', () => {
    const stats = planningStats([trip(1, 1), trip(1, null), trip(null, null), trip(1, 1, 'Cancelled'), trip(2, 2, 'Assigned', 'motivo')]);
    expect(stats).toEqual({ total: 4, covered: 2, coveredPct: 50, pending: 2, forced: 1 });
  });
  it('no trips → 0 %', () => expect(planningStats([]).coveredPct).toBe(0));
});
