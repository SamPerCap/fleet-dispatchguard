/// <reference types="vite/client" />
import { describe, expect, it, vi } from 'vitest';
import tourSource from './tourCase.ts?raw';
import tourView from './Tour.tsx?raw';
import { findCase, MOST_TRIPS_ASKED, tripsToTry, type TourSource } from './tourCase';
import type { DriverOptions, EngineAttempt, Trip } from './types';

const NOW = new Date('2026-11-25T10:00:00Z');

const trip = (id: number, hoursAhead: number, changes: Partial<Trip> = {}): Trip => ({
  Id: id,
  OriginLocationId: 1,
  DestinationLocationId: 2,
  Start: new Date(NOW.getTime() + hoursAhead * 3_600_000).toISOString(),
  End: new Date(NOW.getTime() + (hoursAhead + 1) * 3_600_000).toISOString(),
  RequiredSeats: 30,
  ServiceType: 'Regular',
  Status: 'Assigned',
  Description: null,
  AssignedDriverId: 9,
  AssignedVehicleId: 9,
  IsLocked: false,
  IsActive: true,
  BookingId: id,
  ...changes,
});
const unassigned = { AssignedDriverId: null, AssignedVehicleId: null, Status: 'Pending' };

const attempt = (tripId: number, driverId: number, outcome = 'RejectedCompliance') =>
  ({ TripId: tripId, DriverId: driverId, Outcome: outcome }) as EngineAttempt;

const discarded = (...drivers: [id: number, reason: string][]): DriverOptions => ({
  Candidates: [],
  Discarded: drivers.map(([DriverId, Reason]) => ({
    DriverId,
    FullName: `Conductor ${DriverId}`,
    Reason,
    ...(Reason.startsWith(ILLEGAL_PREFIX) ? { Cause: 'NotCompliant', Broken: Reason.slice(ILLEGAL_PREFIX.length) } : { Cause: 'Overlap' }),
  })),
});
/** As the API words it; the web reads `Cause` and `Broken`, never this text (SPEC 0095). */
const ILLEGAL_PREFIX = 'No cumple CE 561/2006: ';
const ILLEGAL = 'No cumple CE 561/2006: Conducción continua: 5 h (máximo 4,5 h)';
const OVERLAP = 'Tiene un solape con otro servicio no cancelado';

function source(trips: Trip[], rejected: EngineAttempt[], options: Record<number, DriverOptions>) {
  const driverOptions = vi.fn(async (tripId: number) => options[tripId] ?? discarded());
  const all: TourSource = { trips: async () => trips, rejectedAttempts: async () => rejected, driverOptions };
  return { all, driverOptions };
}

describe('the case of the tour (SPEC 0093 CA2)', () => {
  it('is an attempt that was rejected, if the engine still rejects it', async () => {
    const { all, driverOptions } = source(
      [trip(1, 2), trip(2, 30)],
      [attempt(2, 7)],
      { 1: discarded([5, ILLEGAL]), 2: discarded([5, ILLEGAL], [7, ILLEGAL]) },
    );

    expect(await findCase(all, NOW)).toEqual({
      trip: expect.objectContaining({ Id: 2 }),
      driverId: 7,
      driverName: 'Conductor 7',
      reason: 'Conducción continua: 5 h (máximo 4,5 h)',
    });
    expect(driverOptions).toHaveBeenCalledTimes(1);
  });

  it('goes on when what was rejected would be legal now', async () => {
    const { all } = source([trip(1, 2, unassigned), trip(2, 30)], [attempt(2, 7)], { 1: discarded([5, ILLEGAL]) });

    expect(await findCase(all, NOW)).toMatchObject({ trip: { Id: 1 }, driverId: 5 });
  });

  it('only counts a rejection of the regulation', async () => {
    const { all } = source([trip(1, 2)], [attempt(1, 5)], { 1: discarded([5, OVERLAP], [6, ILLEGAL]) });

    expect(await findCase(all, NOW)).toMatchObject({ driverId: 6 });
  });

  it('is null when there is none', async () => {
    const { all } = source([trip(1, 2)], [attempt(1, 5)], { 1: discarded([5, OVERLAP]) });

    expect(await findCase(all, NOW)).toBeNull();
  });

  it('is null without services', async () => {
    const { all, driverOptions } = source([], [], {});

    expect(await findCase(all, NOW)).toBeNull();
    expect(driverOptions).not.toHaveBeenCalled();
  });

  it('asks about a few services at most', async () => {
    const { all, driverOptions } = source(
      Array.from({ length: 40 }, (_, index) => trip(index + 1, index + 1)),
      [],
      {},
    );

    expect(await findCase(all, NOW)).toBeNull();
    expect(driverOptions).toHaveBeenCalledTimes(MOST_TRIPS_ASKED);
  });

  it('a failure of the API is not «no case»', async () => {
    const { all, driverOptions } = source([trip(1, 2)], [], {});
    driverOptions.mockRejectedValue(new Error('HTTP 429'));

    await expect(findCase(all, NOW)).rejects.toThrow('HTTP 429');
  });
});

describe('services worth trying', () => {
  it('leaves out what has departed, is cancelled, locked or inactive', () => {
    const trips = [
      trip(1, -1),
      trip(2, 1, { Status: 'Cancelled' }),
      trip(3, 1, { IsLocked: true }),
      trip(4, 1, { IsActive: false }),
      trip(5, 1),
    ];
    expect(tripsToTry(trips, [], NOW).map(one => one.Id)).toEqual([5]);
  });

  it('rejected for the regulation, then without a driver, then the rest, by departure', () => {
    const trips = [trip(1, 1), trip(2, 2, unassigned), trip(3, 3), trip(4, 4, unassigned), trip(5, 5), trip(6, 6)];
    const rejected = [attempt(5, 1), attempt(6, 1, 'RejectedVehicle'), attempt(3, 1)];
    expect(tripsToTry(trips, rejected, NOW).map(one => one.Id)).toEqual([3, 5, 2, 4, 1, 6]);
  });
});

describe('the tour changes no data (SPEC 0093 R2)', () => {
  it('its code has no way to write', () => {
    expect(`${tourSource}${tourView}`).not.toMatch(/allocate|recommendations|saving|\bpost\b|createBooking|\bmasterData\b/i);
  });
});
