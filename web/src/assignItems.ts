import { t } from './i18n/t';
// What the driver and bus fields offer when assigning a trip (SPEC 0082). The engine decides who can be chosen; here it is only shown.
import { formatKm } from './history';
import type { PickerItem } from './pickerSearch';
import { overlappingUnavailabilities, unavailabilityText } from './resources';
import type { BookingTrip, Driver, DriverCandidate, DriverOptions, Vehicle, VehicleCandidate } from './types';
import { vehicleOptions, type BusProposal } from './vehicleChange';

/** Cards shown with their badges, warnings and explanation; the other drivers are reached through the field. */
export const DRIVER_CARDS = 5;

type DriverTrip = Pick<BookingTrip, 'AssignedDriverId' | 'AssignedDriverName'>;

/** A list from before being assigned from another trip may still include the current driver, who has its own option. */
export const otherCandidates = (trip: DriverTrip, options: DriverOptions) =>
  options.Candidates.filter(candidate => candidate.DriverId !== trip.AssignedDriverId);

export const distanceText = (candidate: Pick<DriverCandidate, 'DistanceKm'>) =>
  candidate.DistanceKm != null ? formatKm(candidate.DistanceKm) : t.items.noBase;

export function candidateTags(candidate: DriverCandidate, recommendedId?: number | null): string[] {
  return [
    ...(recommendedId === candidate.DriverId ? [t.common.recommended] : []),
    ...(candidate.CoversSameBooking ? [t.common.sameBooking] : []),
    ...(candidate.WonTieBreak ? [t.items.tieBreak] : []),
  ];
}

export function driverItems(trip: DriverTrip, options: DriverOptions, recommendedId?: number | null): PickerItem[] {
  const current: PickerItem[] =
    trip.AssignedDriverId == null
      ? []
      : [
          {
            id: trip.AssignedDriverId,
            label: trip.AssignedDriverName ?? `#${trip.AssignedDriverId}`,
            detail: t.assign.keepsDriver,
            tags: [t.common.current],
          },
        ];
  const candidates = otherCandidates(trip, options).map(candidate => ({
    id: candidate.DriverId,
    label: candidate.FullName,
    detail: [distanceText(candidate), candidate.Reason, ...candidate.Warnings.map(warning => `⚠ ${warning}`)].join(' · '),
    tags: candidateTags(candidate, recommendedId),
  }));
  const offered = new Set([...current, ...candidates].map(item => item.id));
  const discarded = options.Discarded.filter(driver => !offered.has(driver.DriverId)).map(driver => ({
    id: driver.DriverId,
    label: driver.FullName,
    detail: driver.Reason,
    blocked: true,
  }));
  return [...current, ...candidates, ...discarded];
}

const byName = new Intl.Collator('es', { numeric: true });

/** Who can be put on leave. */
export const leaveItems = (drivers: Driver[]): PickerItem[] =>
  drivers
    .map(driver => ({
      id: driver.Id,
      label: `${driver.FirstName} ${driver.LastName}`,
      detail: [driver.IdentificationNumber, driver.IsExternal && 'externo', !driver.IsActive && 'inactivo'].filter(Boolean).join(' · '),
      keywords: driver.IdentificationNumber,
      ...(driver.IsActive ? {} : { blocked: true }),
    }))
    .sort((a, b) => byName.compare(a.label, b.label) || byName.compare(a.detail, b.detail) || a.id - b.id);

/** The engine leaves out of its search whoever has the service, so the driver on leave is added to say why they are not offered. */
export function replacementItems(options: DriverOptions, onLeave: { id: number; name: string }): PickerItem[] {
  const isOnLeave = (driver: { DriverId: number }) => driver.DriverId === onLeave.id;
  const items = driverItems(
    { AssignedDriverId: null, AssignedDriverName: null },
    {
      Candidates: options.Candidates.filter(candidate => !isOnLeave(candidate)),
      Discarded: options.Discarded.filter(driver => !isOnLeave(driver)),
    },
  );
  return [...items, { id: onLeave.id, label: onLeave.name, detail: t.items.onLeave, blocked: true }];
}

/** The first ones and, if it is another, the chosen one. */
export function driverCards<T extends { DriverId: number }>(candidates: T[], chosenId: number | null, max = DRIVER_CARDS): T[] {
  return candidates.filter((candidate, index) => index < max || candidate.DriverId === chosenId);
}

type BusTrip = Parameters<typeof vehicleOptions>[0] & Pick<BookingTrip, 'Start' | 'End' | 'RequiredSeats'>;

/**
 * The same filters as `VehicleCandidateSearch`, in order, to say why a bus of the fleet is missing from its list. The fleet may be
 * newer than the list, so what is left is not claimed to be an overlap.
 */
export function whyBusNotOffered(trip: BusTrip, vehicle: Vehicle): string {
  if (!vehicle.IsActive) return t.items.vehicleInactive;
  if (vehicle.IsExternal) return t.items.vehicleExternal;
  if (!vehicle.AllowsAutoAssignment) return t.items.notAuto;
  if (vehicle.SeatingCapacity < trip.RequiredSeats)
    return t.items.fewSeats(vehicle.SeatingCapacity, trip.RequiredSeats);
  const entries = overlappingUnavailabilities(trip, vehicle.Unavailabilities);
  if (entries.length > 0) return entries.map(unavailabilityText).join(' · ');
  return t.items.busyOrUnfit;
}

const modelOf = (vehicle: Vehicle) => `${vehicle.Brand} ${vehicle.Model}`.trim();

/** A plate is typed with or without separators. */
const busKeywords = (plate: string, vehicle?: Vehicle) =>
  [plate.replace(/[^\p{L}\p{N}]/gu, ''), vehicle && `${modelOf(vehicle)} ${vehicle.SeatingCapacity} plazas`].filter(Boolean).join(' ');

export function busItems(
  trip: BusTrip,
  candidates: VehicleCandidate[],
  recommendedId: number | null,
  proposal: BusProposal | null,
  fleet: Vehicle[],
): PickerItem[] {
  const byId = new Map(fleet.map(vehicle => [vehicle.Id, vehicle]));
  const offered = vehicleOptions(trip, candidates, recommendedId, proposal).map(option => {
    const vehicle = byId.get(option.id);
    const seats = option.seats ?? vehicle?.SeatingCapacity;
    return {
      id: option.id,
      label: option.label,
      tags: option.tags,
      detail: [vehicle && modelOf(vehicle), seats != null && `${seats} plazas`, option.detail].filter(Boolean).join(' · '),
      keywords: busKeywords(option.label, vehicle),
    };
  });
  const offeredIds = new Set(offered.map(item => item.id));
  const missing = fleet
    .filter(vehicle => !offeredIds.has(vehicle.Id))
    .map(vehicle => ({
      id: vehicle.Id,
      label: vehicle.LicensePlate,
      detail: whyBusNotOffered(trip, vehicle),
      keywords: busKeywords(vehicle.LicensePlate, vehicle),
      blocked: true,
    }));
  return [...offered, ...missing];
}
