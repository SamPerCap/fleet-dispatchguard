import { t } from './i18n/t';
// Changes the bus of an already assigned service. `allocate` decides whether it is valid.
import { excludedByAgenda } from './resources';
import type { BookingTrip, Explanation, Vehicle, VehicleCandidate } from './types';

export interface BusProposal {
  vehicleId: number;
  plate: string;
  currentVehicleId: number;
  currentPlate: string;
}

export const proposedText = () => t.vehicleChange.proposedInReport;

type Assigned = Pick<BookingTrip, 'AssignedDriverId' | 'AssignedDriverName' | 'AssignedVehicleId' | 'AssignedVehiclePlate'>;

/** The choice is kept if still listed; otherwise the current driver when it must be kept, otherwise the first one. */
export function pickDriver(chosen: number | null, trip: Assigned, candidates: { DriverId: number }[], keepCurrent: boolean): number | null {
  const current = trip.AssignedDriverId;
  if (chosen != null && (chosen === current || candidates.some(c => c.DriverId === chosen))) return chosen;
  return (keepCurrent ? current : null) ?? candidates[0]?.DriverId ?? null;
}

export interface VehicleOption {
  id: number;
  label: string;
  tags: string[];
  seats?: number;
  detail: string;
}

export function vehicleOptions(
  trip: Assigned,
  candidates: VehicleCandidate[],
  recommendedId: number | null,
  proposal: BusProposal | null,
): VehicleOption[] {
  const proposed = (id: number) => (proposal?.vehicleId === id ? [proposedText()] : []);
  const current = trip.AssignedVehicleId;
  const listed = proposal == null || proposal.vehicleId === current || candidates.some(v => v.VehicleId === proposal.vehicleId);
  return [
    ...(current == null
      ? []
      : [{ id: current, label: trip.AssignedVehiclePlate ?? `#${current}`, tags: [t.common.current, ...proposed(current)], detail: '' }]),
    ...(listed ? [] : [{ id: proposal.vehicleId, label: proposal.plate, tags: [proposedText()], detail: t.vehicleChange.checkedWhenSaving }]),
    ...candidates
      .filter(v => v.VehicleId !== current)
      .map(candidate => ({
        id: candidate.VehicleId,
        label: candidate.LicensePlate,
        tags: [...(recommendedId === candidate.VehicleId ? [t.common.recommended] : []), ...proposed(candidate.VehicleId)],
        seats: candidate.SeatingCapacity,
        detail: candidate.Reason,
      })),
  ];
}

/** Only those that their agenda leaves out: not the ones that the engine would not offer anyway (`VehicleCandidateSearch`). */
export const busesOutByAgenda = (
  trip: Pick<BookingTrip, 'Start' | 'End' | 'RequiredSeats' | 'AssignedVehicleId'>,
  vehicles: Vehicle[],
  candidates: Pick<VehicleCandidate, 'VehicleId'>[],
) =>
  excludedByAgenda(
    trip,
    vehicles.filter(
      vehicle =>
        vehicle.AllowsAutoAssignment &&
        !vehicle.IsExternal &&
        vehicle.SeatingCapacity >= trip.RequiredSeats &&
        vehicle.Id !== trip.AssignedVehicleId &&
        !candidates.some(candidate => candidate.VehicleId === vehicle.Id),
    ),
  );

/** The proposed bus is never preselected (SPEC 0072). */
export function pickVehicle(
  chosen: number | null,
  trip: Assigned,
  candidates: VehicleCandidate[],
  proposal: BusProposal | null,
): number | null {
  if (chosen != null && vehicleOptions(trip, candidates, null, proposal).some(o => o.id === chosen)) return chosen;
  return trip.AssignedVehicleId ?? candidates[0]?.VehicleId ?? null;
}

export function changeOf(trip: Assigned, driverId: number, vehicleId: number): 'none' | 'vehicle' | 'other' {
  if (trip.AssignedDriverId !== driverId) return 'other';
  return trip.AssignedVehicleId === vehicleId ? 'none' : 'vehicle';
}

/** What confirming would store, next to the button: the list and the bus may be scrolled out of sight. */
export function choiceText(driver: string | null, plate: string | null, tripCount: number): string {
  if (driver == null || plate == null) {
    const missesBoth = driver == null && plate == null;
    const missing = missesBoth ? t.vehicleChange.driverAndBus : driver == null ? t.common.driverLower : t.common.vehicleLower;
    return t.vehicleChange.missing(missing);
  }
  // The other trips keep their own bus, so only the driver is common to all of them.
  return tripCount > 1 ? t.vehicleChange.driverInTrips(driver, tripCount, plate) : `${driver} · ${plate}`;
}

export function doneText(trip: Assigned, driverId: number, vehicleId: number, plate: string): string | undefined {
  if (changeOf(trip, driverId, vehicleId) !== 'vehicle') return undefined;
  return t.vehicleChange.changed(trip.AssignedVehicleId == null ? t.vehicleChange.assignedBus : t.vehicleChange.changedBus, plate, trip.AssignedDriverName ?? t.vehicleChange.theDriver(driverId));
}

export function proposalState(trip: Assigned, proposal: BusProposal): 'open' | 'done' | 'moved' {
  if (trip.AssignedVehicleId === proposal.vehicleId) return 'done';
  return trip.AssignedVehicleId === proposal.currentVehicleId ? 'open' : 'moved';
}

/** Keeping the driver of a forced service goes through the regulations again (ADR 0015). */
export function forcedNotice(trip: Pick<BookingTrip, 'AssignedDriverId' | 'ForcedDescription'>, driverId: number | null): string | null {
  return trip.ForcedDescription && driverId != null && driverId === trip.AssignedDriverId
    ? t.vehicleChange.forcedWarning
    : null;
}

/** Of the API (`Texts.NoVehicleOverlap`). */
const VEHICLE_OVERLAP = 'NoVehicleOverlap';

/** null if the rejection is not an overlap (the API reason wins). */
export function clashText(explanation: Explanation | null | undefined, plate: string, fromReport: boolean): string | null {
  const clash = explanation?.Business.find(item => item.Outcome === 'Failed' && item.Code === VEHICLE_OVERLAP);
  if (!clash) return null;
  return (
    t.vehicleChange.busTaken(plate, clash.Detail) +
    (fromReport
      ? t.vehicleChange.swapHint
      : '')
  );
}

export function returnNote(tripId: number, recalculated: boolean, listed: boolean): string | null {
  if (!recalculated) return null;
  return t.vehicleChange.recalculated(tripId, listed ? t.vehicleChange.still : t.vehicleChange.noLonger);
}
