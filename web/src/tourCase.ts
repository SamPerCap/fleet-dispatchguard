import { addDays, parseApiDate } from './gantt';
import type { DriverOptions, EngineAttempt, Trip } from './types';

/** Only reads: the tour never assigns, so it leaves not even an attempt (SPEC 0093 R2). */
export interface TourSource {
  trips: (from: Date, to: Date) => Promise<Trip[]>;
  rejectedAttempts: () => Promise<EngineAttempt[]>;
  driverOptions: (tripId: number) => Promise<DriverOptions>;
}

export interface TourCase {
  trip: Trip;
  driverId: number;
  driverName: string;
  /** Rule and figures, as the API says them. */
  reason: string;
}


const DAYS_AHEAD = 7;
/** Each one evaluates the whole staff. */
export const MOST_TRIPS_ASKED = 6;

const byDeparture = (first: Trip, second: Trip) => parseApiDate(first.Start).getTime() - parseApiDate(second.Start).getTime();

const canBeTried = (trip: Trip, now: Date) =>
  trip.IsActive && !trip.IsLocked && trip.Status !== 'Cancelled' && parseApiDate(trip.Start).getTime() > now.getTime();

/** First the ones where somebody was already rejected for this reason, then the ones that nobody has. */
export function tripsToTry(trips: Trip[], rejected: EngineAttempt[], now: Date): Trip[] {
  const possible = trips.filter(trip => canBeTried(trip, now)).sort(byDeparture);
  const illegal = new Set(rejected.filter(attempt => attempt.Outcome === 'RejectedCompliance').map(attempt => attempt.TripId));
  const rank = (trip: Trip) => (illegal.has(trip.Id) ? 0 : trip.AssignedDriverId === null ? 1 : 2);
  return possible.sort((first, second) => rank(first) - rank(second)).slice(0, MOST_TRIPS_ASKED);
}

export function illegalDriver(options: DriverOptions, rejectedDriverIds: ReadonlySet<number>) {
  const illegal = options.Discarded.filter(driver => driver.Cause === 'NotCompliant');
  const found = illegal.find(driver => rejectedDriverIds.has(driver.DriverId)) ?? illegal[0];
  return found && { driverId: found.DriverId, driverName: found.FullName, reason: found.Broken ?? found.Reason };
}

/** What the engine says now, not what it said when the attempt was made. Null when no case is left. */
export async function findCase(source: TourSource, now: Date): Promise<TourCase | null> {
  const [trips, rejected] = await Promise.all([source.trips(now, addDays(now, DAYS_AHEAD)), source.rejectedAttempts()]);
  for (const trip of tripsToTry(trips, rejected, now)) {
    const rejectedHere = new Set(rejected.filter(attempt => attempt.TripId === trip.Id).map(attempt => attempt.DriverId));
    const driver = illegalDriver(await source.driverOptions(trip.Id), rejectedHere);
    if (driver) return { trip, ...driver };
  }
  return null;
}
