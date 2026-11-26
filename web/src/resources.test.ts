import { describe, expect, it } from 'vitest';
import {
  agendaConflicts,
  agendaConflictText,
  excludedByAgenda,
  isUnavailableAt,
  matchesSearch,
  overlappingUnavailabilities,
  sortUnavailabilities,
  unavailabilityText,
  upcomingTrips,
  vehiclePosition,
} from './resources';
import type { Driver, Trip, Unavailability, Vehicle } from './types';

const now = new Date('2026-11-18T12:00:00Z');
const hoursFromNow = (hours: number) => new Date(now.getTime() + hours * 3_600_000).toISOString();
const trip = (id: number, start: number, end: number, extra: Partial<Trip> = {}): Trip => ({
  Id: id,
  OriginLocationId: 1,
  DestinationLocationId: 100 + id,
  Start: hoursFromNow(start),
  End: hoursFromNow(end),
  RequiredSeats: 10,
  ServiceType: 'Regular',
  Status: 'Scheduled',
  Description: null,
  AssignedDriverId: 1,
  AssignedVehicleId: 1,
  IsLocked: false,
  IsActive: true,
  BookingId: 1,
  ...extra,
});
const unavailability = (start: number, end: number): Unavailability => ({
  Id: start,
  Start: hoursFromNow(start),
  End: hoursFromNow(end),
  Type: 'SickLeave',
  Notes: null,
});

describe('matchesSearch', () => {
  it('no query: everything matches', () => expect(matchesSearch('  ', 'x')).toBe(true));
  it('ignores case and accents', () => expect(matchesSearch('JOSE garcia', 'José García')).toBe(true));
  it('searches any field', () => expect(matchesSearch('1234', 'Ana', '1234-ABC')).toBe(true));
  it('no match', () => expect(matchesSearch('zz', 'Ana', null)).toBe(false));
});

describe('upcomingTrips', () => {
  it('includes the trip in progress, leaves out finished and cancelled ones, sorts by departure', () => {
    const upcoming = upcomingTrips([trip(1, 5, 6), trip(2, -1, 1), trip(3, -3, -1), trip(4, 2, 3, { Status: 'Cancelled' })], now);
    expect(upcoming.map(t => t.Id)).toEqual([2, 1]);
  });
  it('limit: leaving at exactly 14 days → out', () => {
    expect(upcomingTrips([trip(1, 14 * 24 - 1, 14 * 24 + 1), trip(2, 14 * 24, 14 * 24 + 1)], now).map(t => t.Id)).toEqual([1]);
  });
});

describe('vehiclePosition', () => {
  it('destination of the last finished trip', () => {
    expect(vehiclePosition([trip(1, -10, -8), trip(2, -5, -2), trip(3, 1, 2)], 9, now)).toEqual({
      locationId: 102,
      source: 'LastTrip',
      since: hoursFromNow(-2),
    });
  });
  it('ignores cancelled trips', () => {
    expect(vehiclePosition([trip(1, -10, -8), trip(2, -5, -2, { Status: 'Cancelled' })], 9, now).locationId).toBe(101);
  });
  it('7-day limit: finished exactly 7 days ago counts; 7 days and 1 h ago → base', () => {
    expect(vehiclePosition([trip(1, -170, -168)], 9, now).source).toBe('LastTrip');
    expect(vehiclePosition([trip(1, -170, -169)], 9, now)).toEqual({ locationId: 9, source: 'Home' });
  });
  it('no trips and no base → unknown', () => expect(vehiclePosition([], null, now).source).toBe('Unknown'));
});

describe('unavailabilities', () => {
  it('separates current and future ones from past ones', () => {
    const sorted = sortUnavailabilities(
      [unavailability(-50, -40), unavailability(10, 20), unavailability(-2, 2), unavailability(-90, -80)],
      now,
    );
    expect(sorted.current.map(u => u.Id)).toEqual([-2, 10]);
    expect(sorted.past.map(u => u.Id)).toEqual([-50, -90]);
  });
  it('isUnavailableAt: exclusive end', () => {
    expect(isUnavailableAt([unavailability(-2, 2)], now)).toBe(true);
    expect(isUnavailableAt([unavailability(-2, 0)], now)).toBe(false);
  });
});

describe('overlap notice with an unavailability', () => {
  it('returns the overlapping ones, by start; adjacent ones do not count', () => {
    const overlapping = overlappingUnavailabilities(trip(1, 4, 6), [
      unavailability(5, 9),
      unavailability(0, 4),
      unavailability(6, 8),
      unavailability(3, 5),
    ]);
    expect(overlapping.map(u => u.Id)).toEqual([3, 5]);
  });
  it.each([
    ['Repair', 'Reparación en taller'],
    ['TechnicalInspection', 'ITV'],
    ['Cleaning', 'Limpieza'],
    ['Administrative', 'Gestión administrativa'],
    ['SickLeave', 'Baja médica'],
    ['WeeklyRest', 'Descanso semanal'],
  ])('the text comes from the recorded type: %s', (type, label) => {
    // 12:00Z and 14:00Z are 13:00 and 15:00 in Madrid.
    expect(unavailabilityText({ ...unavailability(0, 2), Type: type })).toBe(`Indisponible: ${label} (18/11, 13:00 – 18/11, 15:00)`);
  });
});

describe('agenda against services', () => {
  const driver = (id: number, entries: Unavailability[], IsActive = true): Driver => ({
    Id: id,
    IdentificationNumber: `D${id}`,
    FirstName: 'Ana',
    LastName: `López ${id}`,
    IsExternal: false,
    AllowsAutoAssignment: true,
    IsActive,
    HomeLocationId: null,
    Unavailabilities: entries,
    DetailsVersion: 'v1',
  });
  const vehicle = (id: number, entries: Unavailability[], IsActive = true): Vehicle => ({
    Id: id,
    LicensePlate: `000${id}ABC`,
    Brand: 'Irizar',
    Model: 'i6',
    IsExternal: false,
    AllowsAutoAssignment: true,
    SeatingCapacity: 55,
    IsActive,
    HomeLocationId: null,
    Unavailabilities: entries,
    DetailsVersion: 'v1',
  });
  const itv = { ...unavailability(5, 9), Type: 'TechnicalInspection' };

  it('names the driver and the vehicle that catch it, and only those assigned to it', () => {
    const conflicts = agendaConflicts(
      trip(1, 4, 6),
      [driver(1, [unavailability(3, 5), unavailability(6, 8)]), driver(2, [unavailability(0, 9)])],
      [vehicle(1, [itv]), vehicle(2, [itv])],
      now,
    );
    expect(conflicts.map(c => [c.kind, c.resourceId, c.resource, c.entry.Id])).toEqual([
      ['driver', 1, 'Ana López 1', 3],
      ['vehicle', 1, '0001ABC', 5],
    ]);
    expect(agendaConflictText(conflicts[1])).toBe('0001ABC. Indisponible: ITV (18/11, 18:00 – 18/11, 22:00)');
  });
  it.each([
    ['leaves when the absence ends', 9, 10, 0],
    ['arrives when the absence starts', 3, 5, 0],
    ['one minute inside', 3, 5 + 1 / 60, 1],
  ])('%s', (_, start, end, count) => {
    expect(agendaConflicts(trip(1, start, end), [], [vehicle(1, [itv])], now)).toHaveLength(count);
  });
  it('nothing for unassigned, cancelled, completed or finished services', () => {
    const always = [vehicle(1, [unavailability(-90, 90)])];
    expect(agendaConflicts(trip(1, 4, 6, { AssignedVehicleId: null }), [], always, now)).toEqual([]);
    expect(agendaConflicts(trip(1, 4, 6, { Status: 'Cancelled' }), [], always, now)).toEqual([]);
    expect(agendaConflicts(trip(1, -2, 2, { Status: 'Completed' }), [], always, now)).toEqual([]);
    expect(agendaConflicts(trip(1, -2, 0), [], always, now)).toEqual([]);
    expect(agendaConflicts(trip(1, -2, 0.01), [], always, now)).toHaveLength(1);
  });
  it('excludedByAgenda: the active ones whose agenda overlaps, whoever is assigned', () => {
    const excluded = excludedByAgenda(trip(1, 4, 6), [
      vehicle(1, [itv]),
      vehicle(2, [unavailability(6, 8)]),
      vehicle(3, []),
      vehicle(4, [itv], false),
    ]);
    expect(excluded.map(e => [e.resource.Id, e.entries.map(x => x.Id)])).toEqual([[1, [5]]]);
  });
});
