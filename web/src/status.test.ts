import { describe, expect, it } from 'vitest';
import { bookingStatus, isForced, STATUS_KEYS, STATUS_TEXT, tripStatus, type TripLike } from './status';

// API dates = UTC without Z: built from local times so that the test does not depend on the zone.
const toApiDate = (date: Date) => date.toISOString().replace('Z', '');
const now = new Date(2026, 10, 18, 11, 40);
const at = (hours: number, minutes = 0, day = 18) => new Date(2026, 10, day, hours, minutes);
const trip = (start: Date, end: Date, extra: Partial<TripLike> = {}): TripLike => ({
  Start: toApiDate(start),
  End: toApiDate(end),
  Status: 'Assigned',
  AssignedDriverId: 1,
  AssignedVehicleId: 2,
  ...extra,
});

describe('tripStatus', () => {
  it('covered and not yet departed → assigned', () => expect(tripStatus(trip(at(12), at(13)), now)).toBe('assigned'));
  it('covered and within its schedule → in progress by schedule (start included, end excluded)', () => {
    expect(tripStatus(trip(at(11, 40), at(13)), now)).toBe('running');
    expect(tripStatus(trip(at(10), at(11, 40)), now)).toBe('completed');
  });
  it('no telemetry: a trip with status Assigned whose end has passed is completed', () =>
    expect(tripStatus(trip(at(8), at(9)), now)).toBe('completed'));
  it('status Completed wins even if the schedule has not ended', () =>
    expect(tripStatus(trip(at(11), at(13), { Status: 'Completed' }), now)).toBe('completed'));
  it('without driver or vehicle and leaving today → uncovered (also if it should have left already)', () => {
    expect(tripStatus(trip(at(23, 30), at(23, 50), { AssignedDriverId: null }), now)).toBe('uncovered');
    expect(tripStatus(trip(at(9), at(10), { AssignedVehicleId: null }), now)).toBe('uncovered');
  });
  it('without resources and leaving tomorrow at 00:00 → planned (day limit)', () => {
    expect(tripStatus(trip(at(0, 0, 19), at(1, 0, 19), { AssignedDriverId: null }), now)).toBe('planned');
    expect(tripStatus(trip(at(23, 59), at(1, 0, 19), { AssignedDriverId: null }), now)).toBe('uncovered');
  });
  it('cancelled → out of the taxonomy', () => expect(tripStatus(trip(at(12), at(13), { Status: 'Cancelled' }), now)).toBeNull());
  it('forced is marked apart', () => {
    expect(isForced({ ForcedDescription: 'Sin alternativa' })).toBe(true);
    expect(isForced({ ForcedDescription: null })).toBe(false);
  });
});

describe('bookingStatus', () => {
  it('maps the derived status of the booking', () => {
    expect(bookingStatus('FullyAssigned', toApiDate(at(12)), now)).toBe('assigned');
    expect(bookingStatus('PartiallyAssigned', toApiDate(at(12)), now)).toBe('partial');
    expect(bookingStatus('Unassigned', toApiDate(at(12)), now)).toBe('uncovered');
    expect(bookingStatus('Unassigned', toApiDate(at(9, 0, 20)), now)).toBe('planned');
    expect(bookingStatus('Unassigned', null, now)).toBe('uncovered');
    expect(bookingStatus('Raro', null, now)).toBeNull();
  });
});

describe('closed taxonomy', () => {
  it('seven statuses, all with text and definition', () => {
    expect(STATUS_KEYS).toHaveLength(7);
    for (const key of STATUS_KEYS) expect(STATUS_TEXT[key].label.length).toBeGreaterThan(0);
    expect(STATUS_TEXT.running.label).toBe('En curso según horario');
  });
});
