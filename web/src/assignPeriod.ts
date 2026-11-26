import { t } from './i18n/t';
import type { ApplyReport } from './applyRun';
import { isUnassigned, type Scale } from './calendar';
import { parseApiDate } from './gantt';
import { applyQueue, isApplied, isRecommendable, leftOutText, type ApplyResult, type ProposalLine } from './recommend';
import type { Explanation, Recommendation, RecommendationRequest, Trip } from './types';
import { listOf } from './language';

/** What Planning showed when the button was pressed. */
export interface AssignTarget {
  request: RecommendationRequest & { From: string; To: string };
  scale: Scale;
  period: string;
  base: string;
  /** Customer of each loaded booking, for the trips left out of the proposal. */
  customers: Map<number, string>;
}

const LABEL: Record<Scale, string> = { get day() { return t.period.assignDay; }, get week() { return t.period.assignWeek; }, get month() { return t.period.assignMonth; } };
export const assignLabel = (scale: Scale): string => LABEL[scale];


/** Without `button`, applying is not offered. */
export interface Confirmation {
  assign: number;
  headline: string;
  button: string | null;
  notes: string[];
}

export function confirmation(proposal: Recommendation, lines: ProposalLine[]): Confirmation {
  const assign = applyQueue(lines).length;
  const unsolved = lines.filter(l => l.view.Status === 'NoSolution').length;
  const leftOut = leftOutText(proposal);
  return {
    assign,
    headline: assign === 0 ? t.period.nothingToAssign : t.period.willAssign(assign),
    button: assign === 0 ? null : t.period.assignTrips(assign),
    notes: [
      ...(unsolved > 0
        ? [t.period.unsolvedLeft(unsolved)]
        : []),
      ...(leftOut ? [leftOut] : []),
    ],
  };
}

export type LeftKey =
  | 'noSolution'
  | 'untried'
  | 'leftOut'
  | 'new'
  | 'departed'
  | 'locked'
  | 'partial'
  | 'elsewhere'
  | 'cancelled'
  | Exclude<ApplyResult['state'], 'applied'>;

/** `pending`: it still needs to be assigned. */
export interface LeftTrip {
  tripId: number;
  bookingId: number;
  customer: string;
  start: string;
  end: string;
  origin: string;
  destination: string;
  key: LeftKey;
  reason: string;
  pending: boolean;
  explanation?: Explanation | null;
}

export interface Names {
  driver: (id: number) => string;
  vehicle: (id: number) => string;
  location: (id: number) => string;
  customer: (bookingId: number) => string;
}

/** `note`: what can only be counted, not listed (when the period cannot be re-read, the trips beyond the cap). */
export interface Outcome {
  assigned: number;
  left: LeftTrip[];
  untouched: LeftTrip[];
  headline: string;
  note: string | null;
}

const isGone = (trip: Trip) => !trip.IsActive || trip.Status === 'Cancelled';
const isTouched = (trip: Trip) => trip.AssignedDriverId != null || trip.AssignedVehicleId != null;
const isOurs = (line: ProposalLine, trip: Trip | undefined) =>
  trip != null && !isGone(trip) && trip.AssignedDriverId === line.chosen.driverId && trip.AssignedVehicleId === line.chosen.vehicleId;

/** Without a response, it counts as ours if it is stored with that pair. */
const isDone = (line: ProposalLine, trip: Trip | undefined) => isApplied(line) || (line.result?.state === 'unknown' && isOurs(line, trip));

const partialText = (trip: Trip, names: Names) =>
  trip.AssignedDriverId != null
    ? t.period.hasDriver(names.driver(trip.AssignedDriverId))
    : t.period.hasVehicle(names.vehicle(trip.AssignedVehicleId!));

const byId = (list: Trip[] | null) => new Map((list ?? []).map(t => [t.Id, t]));

/** Only what nobody else has touched meanwhile can be sent again as it is. */
export function retryQueue(lines: ProposalLine[], current: Trip[] | null): ProposalLine[] {
  const saved = byId(current);
  return applyQueue(lines).filter(line => {
    const trip = saved.get(line.view.TripId);
    return !trip || !(isGone(trip) || isTouched(trip) || trip.IsLocked);
  });
}

export const retryLabel = (queue: ProposalLine[]): string | null =>
  queue.length === 0 ? null : `${queue.some(l => !l.result) ? t.period.continueWith : t.common.retry} ${t.common.trips(queue.length)}`;

/** What is stored now wins; if it adds nothing, what happened when applying. */
function whyNot(
  line: ProposalLine,
  trip: Trip | undefined,
  end: ApplyReport['end'] | null,
  names: Names,
): Pick<LeftTrip, 'key' | 'reason' | 'pending' | 'explanation'> {
  if (trip && isGone(trip)) return { key: 'cancelled', reason: t.period.wasCancelled, pending: false };
  if (trip && trip.AssignedDriverId != null && trip.AssignedVehicleId != null) {
    return {
      key: 'elsewhere',
      pending: false,
      reason: t.period.assignedElsewhere(names.driver(trip.AssignedDriverId), names.vehicle(trip.AssignedVehicleId)),
    };
  }
  if (trip && isTouched(trip)) return { key: 'partial', reason: partialText(trip, names), pending: true };
  if (trip?.IsLocked) return { key: 'locked', reason: t.period.isLocked, pending: true };
  if (line.result && line.result.state !== 'applied')
    return { key: line.result.state, reason: line.result.text, pending: true, explanation: line.result.explanation };
  if (line.view.Status !== 'Proposed')
    return { key: 'noSolution', reason: line.view.Reason, pending: true, explanation: line.view.Explanation };
  return {
    key: 'untried',
    pending: true,
    explanation: line.view.Explanation,
    reason: t.period.notTried(end === 'api' ? t.period.apiStopped : end === 'stopped' ? t.period.youStopped : ''),
  };
}

const GROUPS: [LeftKey, (amount: number) => string][] = [
  ['noSolution', amount => t.period.leftNoSolution(amount)],
  ['rejected', amount => t.period.leftRejected(amount)],
  ['busy', amount => t.period.leftBusy(amount)],
  ['limited', amount => t.period.leftWaiting(amount)],
  ['unknown', amount => t.period.leftUnconfirmed(amount)],
  ['untried', amount => t.period.leftUntried(amount)],
  ['locked', amount => t.period.leftLocked(amount)],
  ['partial', amount => t.period.leftPartial(amount)],
  ['leftOut', amount => t.period.leftOverCap(amount)],
  ['new', amount => t.period.leftNew(amount)],
];

const count = (left: LeftTrip[], key: LeftKey) => left.filter(r => r.key === key).length;

/**
 * `trips` = the period as stored now (same criterion as the proposal); null if it could not be read, and then only what the
 * operation itself knows is said.
 */
export function outcome({
  proposal,
  lines,
  trips: current,
  baseId,
  end,
  now,
  names,
}: {
  proposal: Recommendation;
  lines: ProposalLine[];
  trips: Trip[] | null;
  baseId: number | null;
  end: ApplyReport['end'] | null;
  now: Date;
  names: Names;
}): Outcome {
  const saved = byId(current);
  const ids = new Set(lines.map(l => l.view.TripId));
  const left: LeftTrip[] = [];
  const untouched: LeftTrip[] = [];
  for (const line of lines) {
    const view = line.view;
    const trip = saved.get(view.TripId);
    if (isDone(line, trip)) continue;
    left.push({
      tripId: view.TripId,
      bookingId: view.BookingId,
      customer: view.CustomerName,
      start: view.Start,
      end: view.End,
      origin: view.Origin,
      destination: view.Destination,
      ...whyNot(line, trip, end, names),
    });
  }
  for (const trip of current ?? []) {
    if (ids.has(trip.Id) || !trip.IsActive || !isUnassigned(trip) || (baseId != null && trip.BaseId !== baseId)) continue;
    const row = (key: LeftKey, reason: string): LeftTrip => ({
      tripId: trip.Id,
      bookingId: trip.BookingId,
      customer: names.customer(trip.BookingId),
      start: trip.Start,
      end: trip.End,
      origin: names.location(trip.OriginLocationId),
      destination: names.location(trip.DestinationLocationId),
      key,
      reason,
      pending: true,
    });
    if (isTouched(trip)) untouched.push(row('partial', partialText(trip, names)));
    else if (trip.IsLocked) untouched.push(row('locked', t.period.isLocked));
    else if (!isRecommendable(trip, now)) untouched.push(row('departed', t.period.hasDeparted));
    else if (proposal.LeftOut > 0)
      left.push(
        row('leftOut', t.period.overCapWhy(proposal.MaxTrips, proposal.MaxDays)),
      );
    else left.push(row('new', t.period.notPending));
  }
  const byDeparture = (a: LeftTrip, b: LeftTrip) =>
    parseApiDate(a.start).getTime() - parseApiDate(b.start).getTime() || a.tripId - b.tripId;
  left.sort(byDeparture);
  untouched.sort(byDeparture);

  const assigned = lines.filter(l => isDone(l, saved.get(l.view.TripId))).length;
  const parts = GROUPS.map(([key, text]) => [count(left, key), text] as const)
    .filter(([amount]) => amount > 0)
    .map(([amount, text]) => text(amount));
  const elsewhere = count(left, 'elsewhere');
  const cancelled = count(left, 'cancelled');
  const headline = [
    t.period.assignedCount(assigned),
    parts.length > 0 ? t.period.leftList(listOf(parts)) : null,
    elsewhere > 0 ? t.period.elsewhereCount(elsewhere) : null,
    cancelled > 0 ? t.period.cancelledCount(cancelled) : null,
    current && parts.length === 0 && untouched.length === 0 ? t.period.nonePending : null,
  ]
    .filter(Boolean)
    .join(' ');
  return { assigned, left, untouched, headline, note: current ? null : leftOutText(proposal) };
}
