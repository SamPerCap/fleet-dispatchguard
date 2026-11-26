import { t } from './i18n/t';
import { parseApiDate } from './gantt';
import { formatDecimal } from './history';
import { failureText } from './retry';
import type {
  AttemptOrigin,
  BookingTrip,
  Explanation,
  Recommendation,
  RecommendationLine,
  RecommendationLineInput,
  RecommendedResource,
} from './types';
import { listOf } from './language';

/** `busy` (503) and `limited` (429) are retried as they are; `unknown`: it is not known whether it was stored. */
export interface ApplyResult {
  state: 'applied' | 'rejected' | 'busy' | 'limited' | 'unknown';
  text: string;
  explanation?: Explanation | null;
}

/** The API decides; this only says whether to offer the button. */
export const isRecommendable = (
  trip: Pick<BookingTrip, 'AssignedDriverId' | 'AssignedVehicleId' | 'Status' | 'IsLocked' | 'Start'>,
  now: Date,
) =>
  trip.AssignedDriverId == null &&
  trip.AssignedVehicleId == null &&
  !trip.IsLocked &&
  trip.Status !== 'Cancelled' &&
  parseApiDate(trip.Start).getTime() > now.getTime();

export interface Pair {
  driverId: number | null;
  vehicleId: number | null;
}

/** `view` is the last thing the engine said about the line. */
export interface ProposalLine {
  view: RecommendationLine;
  proposed: Pair;
  chosen: Pair;
  reason: string;
  result?: ApplyResult;
}

const pairOf = (line: RecommendationLine): Pair => ({ driverId: line.Driver?.Id ?? null, vehicleId: line.Vehicle?.Id ?? null });

export const toLines = (recommendation: Recommendation): ProposalLine[] =>
  recommendation.Lines.map(view => ({ view, proposed: pairOf(view), chosen: pairOf(view), reason: view.Reason }));

export const samePair = (first: Pair, second: Pair) => first.driverId === second.driverId && first.vehicleId === second.vehicleId;

/** Putting the proposed pair back makes it recommended again. */
export const isManual = (line: Pick<ProposalLine, 'proposed' | 'chosen'>) => !samePair(line.proposed, line.chosen);

export const originOf = (line: Pick<ProposalLine, 'proposed' | 'chosen'>): AttemptOrigin => (isManual(line) ? 'Manual' : 'Recommended');

export const isApplied = (line: ProposalLine) => line.result?.state === 'applied';

/** Lines already applied are not sent: they are stored and count on their own. */
export const checkInput = (lines: ProposalLine[]): RecommendationLineInput[] =>
  lines
    .filter(l => !isApplied(l))
    .map(line => ({
      TripId: line.view.TripId,
      DriverId: line.chosen.driverId,
      VehicleId: line.chosen.vehicleId,
      Manual: isManual(line),
    }));

/** An untouched line without a pair keeps its reason (how many there are further away). */
export function merge(lines: ProposalLine[], checked: RecommendationLine[]): ProposalLine[] {
  const by = new Map(checked.map(c => [c.TripId, c]));
  return lines.map(line => {
    const checkedLine = by.get(line.view.TripId);
    if (!checkedLine) return line;
    const keepReason = checkedLine.Status === 'NoSolution' && !isManual(line);
    // A rejection on applying is no longer valid once the line is revalidated.
    return { ...line, view: keepReason ? { ...checkedLine, Reason: line.reason } : checkedLine, result: undefined };
  });
}

export const withPair = (lines: ProposalLine[], tripId: number, chosen: Pair) =>
  lines.map(l => (l.view.TripId === tripId ? { ...l, chosen } : l));

export const withResult = (lines: ProposalLine[], tripId: number, result: ApplyResult) =>
  lines.map(l => (l.view.TripId === tripId ? { ...l, result } : l));

/** Only what the engine accepts and is neither done nor rejected, in the order of the proposal. */
export const applyQueue = (lines: ProposalLine[]) =>
  lines.filter(line => line.view.Status === 'Proposed' && line.result?.state !== 'applied' && line.result?.state !== 'rejected');

export interface LineState {
  key: 'proposed' | 'manual' | 'noSolution' | 'invalid' | 'removed' | ApplyResult['state'];
  text: string;
}

const RESULT_TEXT: Record<ApplyResult['state'], string> = {
  get applied() { return t.recommend.applied; },
  get rejected() { return t.common.rejected; },
  get busy() { return t.common.busy; },
  get limited() { return t.common.waiting; },
  get unknown() { return t.common.unconfirmed; },
};

export function lineState(line: ProposalLine): LineState {
  if (line.result) return { key: line.result.state, text: RESULT_TEXT[line.result.state] };
  if (line.view.Status === 'Invalid') return { key: 'invalid', text: t.recommend.wouldNotApply };
  if (line.view.Status === 'NoSolution')
    return isManual(line) ? { key: 'removed', text: t.recommend.removed } : { key: 'noSolution', text: t.common.noSolution };
  return isManual(line) ? { key: 'manual', text: t.recommend.changedByHand } : { key: 'proposed', text: t.recommend.proposed };
}


export function summaryText(lines: ProposalLine[]): string {
  const states = lines.map(l => lineState(l).key);
  const count = (...keys: LineState['key'][]) => states.filter(s => keys.includes(s)).length;
  const groups: [number, (amount: number) => string][] = [
    [count('proposed'), t.recommend.countProposed],
    [count('manual'), t.recommend.countManual],
    [count('noSolution'), t.recommend.countNoSolution],
    [count('invalid'), t.recommend.countInvalid],
    [count('removed'), t.recommend.countRemoved],
    [count('applied'), t.recommend.countApplied],
    [count('rejected'), t.recommend.countRejected],
    [count('busy', 'limited'), t.recommend.countRetry],
    [count('unknown'), t.recommend.countUnconfirmed],
  ];
  const parts = groups.filter(([amount]) => amount > 0).map(([amount, text]) => text(amount));
  return t.recommend.tripsAndList(t.common.trips(lines.length), parts.length > 0 ? listOf(parts) : t.recommend.nothingLower);
}

export const leftOutText = (recommendation: Pick<Recommendation, 'LeftOut' | 'MaxTrips' | 'MaxDays'>): string | null =>
  recommendation.LeftOut === 0
    ? null
    : t.recommend.leftOut(recommendation.LeftOut, recommendation.MaxTrips, recommendation.MaxDays);

const beyond = (resource: RecommendedResource, steps: number[]) =>
  resource.DistanceKm != null && resource.StepKm == null && steps.length > 0 ? t.recommend.moreThan(steps[steps.length - 1]) : null;

export function reachText(resource: RecommendedResource, steps: number[]): string {
  const where =
    resource.DistanceKm == null ? t.common.unknownPosition : t.recommend.distanceFrom(formatDecimal(resource.DistanceKm), resource.Where);
  const why = resource.SameBooking
    ? t.recommend.keptFromBooking
    : resource.StepKm != null
      ? t.recommend.radius(resource.StepKm)
      : beyond(resource, steps);
  return [where, why].filter(Boolean).join(' · ');
}

export const optionText = (resource: RecommendedResource, steps: number[]): string =>
  [
    resource.Name,
    resource.DistanceKm == null ? t.common.unknownPosition : `${formatDecimal(resource.DistanceKm)} km`,
    resource.SameBooking ? t.recommend.sameBooking : beyond(resource, steps),
    resource.Late,
  ]
    .filter(Boolean)
    .join(' · ');

/**
 * A retryable 503 or a 429 are not legal rejections (ADR 0018); without a response, or with a 5xx, it is not known whether it was
 * stored.
 */
export function applyFailure(failure: {
  status?: number;
  title?: string;
  message: string;
  violations?: string[];
  retryable?: boolean;
  explanation?: Explanation;
}): ApplyResult {
  if (failure.retryable) return { state: 'busy', text: failure.message };
  if (failure.status === 429) return { state: 'limited', text: t.recommend.tooManyRequests };
  if (failure.status === undefined || failure.status >= 500) {
    return { state: 'unknown', text: t.recommend.notSureSaved(failure.message) };
  }

  return {
    state: 'rejected',
    text: failureText({ title: failure.title ?? '', message: failure.message, violations: failure.violations ?? [] }),
    explanation: failure.explanation,
  };
}

/** There is no point in going on with the others: the API does not answer or asked to wait. */
export const stopsTheRest = (result: ApplyResult) => result.state === 'unknown' || result.state === 'limited';
