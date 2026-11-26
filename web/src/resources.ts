import { t } from './i18n/t';
import type { ResourceKind } from './resourceUrl';
import type { TripLike } from './status';
import type { Driver, Trip, Unavailability, Vehicle } from './types';
import { parseApiDate, TIME_ZONE } from './gantt';
import { locale } from './language';

const DAY = 86_400_000;
const norm = (text: string) => text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

export const matchesSearch = (query: string, ...fields: (string | null | undefined)[]) =>
  !query.trim() || fields.some(f => f && norm(f).includes(norm(query.trim())));

export function upcomingTrips(trips: Trip[], now: Date, days = 14): Trip[] {
  const limit = now.getTime() + days * DAY;
  return trips
    .filter(
      trip => trip.Status !== 'Cancelled' && parseApiDate(trip.End).getTime() > now.getTime() && parseApiDate(trip.Start).getTime() < limit,
    )
    .sort((a, b) => parseApiDate(a.Start).getTime() - parseApiDate(b.Start).getTime());
}

export type PositionSource = 'LastTrip' | 'Home' | 'Unknown';
export interface Position {
  locationId: number | null;
  source: PositionSource;
  since?: string;
}

/** Mirrors `VehicleCandidateSearch` (SPEC 0032), only to show it: last finished trip in the previous 7 days → home base → unknown. */
export function vehiclePosition(trips: Trip[], homeLocationId: number | null, now: Date): Position {
  const from = now.getTime() - 7 * DAY;
  const last = trips
    .filter(
      trip => trip.Status !== 'Cancelled' && parseApiDate(trip.End).getTime() <= now.getTime() && parseApiDate(trip.End).getTime() >= from,
    )
    .sort((a, b) => parseApiDate(b.End).getTime() - parseApiDate(a.End).getTime())[0];
  if (last) return { locationId: last.DestinationLocationId, source: 'LastTrip', since: last.End };
  return homeLocationId ? { locationId: homeLocationId, source: 'Home' } : { locationId: null, source: 'Unknown' };
}

export const UNAVAILABILITY_LABEL: Record<string, string> = {
  get Vacation() { return t.absence.vacation; },
  get SickLeave() { return t.absence.sickLeave; },
  get WeeklyRest() { return t.absence.weeklyRest; },
  get Training() { return t.absence.training; },
  get PersonalLeave() { return t.absence.personalLeave; },
  get Other() { return t.absence.other; },
  // Vehicles
  get Maintenance() { return t.absence.maintenance; },
  get TechnicalInspection() { return t.absence.technicalInspection; },
  get Repair() { return t.absence.repair; },
  get Cleaning() { return t.absence.cleaning; },
  get Administrative() { return t.absence.paperwork; },
};

export function sortUnavailabilities(items: Unavailability[], now: Date): { current: Unavailability[]; past: Unavailability[] } {
  const current = items
    .filter(u => parseApiDate(u.End).getTime() > now.getTime())
    .sort((a, b) => parseApiDate(a.Start).getTime() - parseApiDate(b.Start).getTime());
  const past = items
    .filter(u => parseApiDate(u.End).getTime() <= now.getTime())
    .sort((a, b) => parseApiDate(b.Start).getTime() - parseApiDate(a.Start).getTime());
  return { current, past };
}

export const isUnavailableAt = (items: Unavailability[], now: Date) =>
  items.some(
    unavailability =>
      parseApiDate(unavailability.Start).getTime() <= now.getTime() && parseApiDate(unavailability.End).getTime() > now.getTime(),
  );

/** Same criterion as the engine: [Start, End). */
export const overlappingUnavailabilities = (trip: Pick<Trip, 'Start' | 'End'>, items: Unavailability[]): Unavailability[] =>
  items
    .filter(
      unavailability =>
        parseApiDate(unavailability.Start) < parseApiDate(trip.End) && parseApiDate(trip.Start) < parseApiDate(unavailability.End),
    )
    .sort((a, b) => parseApiDate(a.Start).getTime() - parseApiDate(b.Start).getTime());

const when = (instant: string) =>
  parseApiDate(instant).toLocaleString(locale(), {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: TIME_ZONE,
  });

/** A fact, not a story: it does not assume the reason (an unavailable vehicle may not be in the workshop). */
export const unavailabilityText = (unavailability: Unavailability) =>
  t.absence.unavailable(UNAVAILABILITY_LABEL[unavailability.Type] ?? unavailability.Type, when(unavailability.Start), when(unavailability.End));

/** An agenda entry of the driver or the vehicle assigned to a service that catches it. */
export interface AgendaConflict {
  kind: ResourceKind;
  resourceId: number;
  resource: string;
  entry: Unavailability;
}

/** Nobody can do anything about it any more. */
export const isOver = (trip: Pick<TripLike, 'End' | 'Status'>, now: Date) =>
  trip.Status === 'Cancelled' || trip.Status === 'Completed' || parseApiDate(trip.End) <= now;

/** Driver first. */
export function agendaConflicts(
  trip: Pick<TripLike, 'Start' | 'End' | 'Status' | 'AssignedDriverId' | 'AssignedVehicleId'>,
  drivers: Driver[],
  vehicles: Vehicle[],
  now: Date,
): AgendaConflict[] {
  if (isOver(trip, now)) return [];
  const driver = drivers.find(candidate => candidate.Id === trip.AssignedDriverId);
  const vehicle = vehicles.find(candidate => candidate.Id === trip.AssignedVehicleId);
  const conflicts = (kind: ResourceKind, resourceId: number, resource: string, entries: Unavailability[]) =>
    overlappingUnavailabilities(trip, entries).map(entry => ({ kind, resourceId, resource, entry }));
  return [
    ...(driver ? conflicts('driver', driver.Id, `${driver.FirstName} ${driver.LastName}`, driver.Unavailabilities) : []),
    ...(vehicle ? conflicts('vehicle', vehicle.Id, vehicle.LicensePlate, vehicle.Unavailabilities) : []),
  ];
}

export const agendaConflictText = ({ resource, entry }: AgendaConflict) => `${resource}. ${unavailabilityText(entry)}`;

/** Active drivers or vehicles that the engine would reject for this service because of their agenda, with what catches it. */
export const excludedByAgenda = <T extends Pick<Driver, 'IsActive' | 'Unavailabilities'>>(
  trip: Pick<TripLike, 'Start' | 'End'>,
  resources: T[],
): { resource: T; entries: Unavailability[] }[] =>
  resources
    .filter(resource => resource.IsActive)
    .map(resource => ({ resource, entries: overlappingUnavailabilities(trip, resource.Unavailabilities) }))
    .filter(({ entries }) => entries.length > 0);
