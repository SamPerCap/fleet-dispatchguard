import { t } from './i18n/t';
import { ChevronDown, RefreshCw } from 'lucide-react';
import { Fragment, useEffect, useMemo, useState } from 'react';
import { api, recommendations } from './api';
import { busItems, candidateTags, distanceText, DRIVER_CARDS, driverCards, driverItems, otherCandidates } from './assignItems';
import { AssignResults } from './AssignResults';
import { attempt, forced, repeat, type AttemptTarget, type Outcome } from './attempt';
import { InfoButton } from './ExplanationDialog';
import type { ForceRequest } from './force';
import { formatTime, parseApiDate } from './gantt';
import { formatDecimal } from './history';
import { hoursNote, hourLimits } from './hours';
import { unavailabilityText } from './resources';
import { SearchPicker, type PickerTexts } from './SearchPicker';
import type { BarExplanation } from './Timeline';
import type { Booking, BookingTrip, DriverHours, DriverOptions, TripRecommendation, Vehicle, VehicleCandidate } from './types';
import { Avatar, Badge, ErrorBox, SkeletonLines } from './ui';
import {
  busesOutByAgenda,
  changeOf,
  choiceText,
  doneText,
  forcedNotice,
  pickDriver,
  pickVehicle,
  proposedText,
  proposalState,
  type BusProposal,
} from './vehicleChange';
import { useFormVisit } from './visit';

const DRIVER_TEXTS: PickerTexts = { get placeholder() { return t.assign.searchDriver; }, get singular() { return t.picker.driver; }, get plural() { return t.picker.drivers; } };
const BUS_TEXTS: PickerTexts = { get placeholder() { return t.assign.searchBus; }, get singular() { return t.assign.bus; }, get plural() { return t.picker.buses; } };
/** AA contrast of the field border on the tinted block (SPEC 0080). */
const FIELD_BORDER = 'border-ink-3/75 hover:border-ink-3';

/** Same driver for every trip; each of the others keeps its own vehicle if it had one. The open trip is not sent again if nothing changes. */
function assignmentQueue(trip: BookingTrip, others: BookingTrip[], driverId: number, vehicleId: number) {
  const own = changeOf(trip, driverId, vehicleId) === 'none' ? [] : [{ trip, vehicleId }];
  return [...own, ...others.map(other => ({ trip: other, vehicleId: other.AssignedVehicleId ?? vehicleId }))];
}

/**
 * The current driver can be chosen too, to change only the bus: `allocate` checks it on saving. `keepDriver` preselects it (SPEC
 * 0072).
 */
export function AssignForm({
  trip,
  booking,
  keepDriver,
  proposal,
  fleet,
  onAllocated,
  onRefresh,
  onClose,
}: {
  trip: BookingTrip;
  booking: Booking;
  fleet: Vehicle[];
  keepDriver: boolean;
  proposal: BusProposal | null;
  onAllocated: (tripId: number, info: BarExplanation) => void;
  onRefresh: () => void;
  onClose: () => void;
}) {
  useFormVisit(t.assign.title);
  const [options, setOptions] = useState<DriverOptions | null>(null);
  const [vehicles, setVehicles] = useState<VehicleCandidate[]>([]);
  const [driverId, setDriverId] = useState<number | null>(null);
  const [vehicleId, setVehicleId] = useState<number | null>(trip.AssignedVehicleId);
  const [hours, setHours] = useState<DriverHours | null>(null);
  const [wholeBooking, setWholeBooking] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const [busy, setBusy] = useState(false);
  const [results, setResults] = useState<Outcome[] | null>(null);
  const [listedAt, setListedAt] = useState<Date | null>(null);
  // `pick` only drives the "Recomendado" badge: if it fails to load there is no badge and nothing else changes.
  const [pick, setPick] = useState<TripRecommendation | null>(null);
  const [round, setRound] = useState(0);
  const [listing, setListing] = useState(true);
  const others = booking.Trips.filter(t => t.Id !== trip.Id && t.Status !== 'Cancelled' && !t.IsLocked);

  useEffect(() => {
    let live = true;
    setListing(true);
    setPick(null);
    recommendations
      .forTrip(trip.Id)
      .then(p => live && setPick(p))
      .catch(() => undefined);
    Promise.all([api.driverOptions(trip.Id), api.everyVehicleCandidate(trip.Id)])
      .then(([loadedOptions, loadedVehicles]) => {
        if (!live) return;
        setOptions(loadedOptions);
        setVehicles(loadedVehicles);
        setListedAt(new Date());
        setError(null);
        // A recalculated list keeps the choice if it is still in it.
        setDriverId(id => pickDriver(id, trip, loadedOptions.Candidates, keepDriver));
        setVehicleId(id => pickVehicle(id, trip, loadedVehicles, proposal));
      })
      .catch(e => live && setError(e as Error))
      .finally(() => live && setListing(false));
    return () => {
      live = false;
    };
  }, [trip.Id, trip.AssignedDriverId, trip.AssignedVehicleId, round]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    setHours(null);
    if (driverId == null) return;
    let live = true;
    api
      .driverHours(driverId, parseApiDate(trip.Start))
      .then(h => live && setHours(h))
      .catch(() => undefined);
    return () => {
      live = false;
    };
  }, [driverId, trip.Start]);

  const report = (outcome: Outcome, what = t.assign.explainedFor(outcome.target.driverName)) => {
    if (outcome.ok && outcome.explanation)
      onAllocated(outcome.target.tripId, { label: `${what} #${outcome.target.tripId}`, explanation: outcome.explanation });
    return outcome;
  };
  const replace = (outcome: Outcome) =>
    setResults(prev => prev?.map(x => (x.target.tripId === outcome.target.tripId ? outcome : x)) ?? null);

  // Retrying and forcing use the driver of the attempt, not `driverId`: after a refresh the list is requested again and changes.
  const retry = async (outcome: Outcome) => {
    setBusy(true);
    replace(report(await repeat(outcome, api.allocate)));
    setBusy(false);
    onRefresh();
  };

  const force = async (outcome: Outcome, request: ForceRequest) => {
    replace(report(await forced(outcome, request, api.allocate), t.assign.forcedExplained));
    onRefresh();
  };

  const driverNameOf = (id: number) =>
    (id === trip.AssignedDriverId ? trip.AssignedDriverName : options?.Candidates.find(c => c.DriverId === id)?.FullName) ?? `#${id}`;
  const plates = new Map([
    ...vehicles.map(v => [v.VehicleId, v.LicensePlate] as const),
    ...(proposal ? [[proposal.vehicleId, proposal.plate] as const] : []),
    ...[trip, ...others].map(t => [t.AssignedVehicleId, t.AssignedVehiclePlate] as const),
  ]);
  const plateOf = (id: number) => plates.get(id) ?? `#${id}`;

  const assign = async () => {
    if (driverId == null || vehicleId == null) return;
    setBusy(true);
    const driverName = driverNameOf(driverId);
    const outcomes: Outcome[] = [];
    for (const queued of assignmentQueue(trip, wholeBooking ? others : [], driverId, vehicleId)) {
      const plate = plateOf(queued.vehicleId);
      const target: AttemptTarget = {
        tripId: queued.trip.Id,
        driverId,
        driverName,
        vehicleId: queued.vehicleId,
        plate,
        fromReport: proposal != null && queued.trip.Id === trip.Id,
        done: doneText(queued.trip, driverId, queued.vehicleId, plate),
      };
      outcomes.push(report(await attempt(target, api.allocate)));
    }
    setResults(outcomes);
    setBusy(false);
    onRefresh();
  };

  const buses = useMemo(
    () => busItems(trip, vehicles, pick?.VehicleId ?? null, proposal, fleet),
    [trip, vehicles, pick, proposal, fleet],
  );
  const unchanged = !wholeBooking && driverId != null && vehicleId != null && changeOf(trip, driverId, vehicleId) === 'none';
  const forcedNote = forcedNotice(trip, driverId);

  if (results) {
    return <AssignResults results={results} busy={busy} onRetry={result => void retry(result)} onForce={force} onDone={onClose} />;
  }

  return (
    <div className="mt-3 space-y-4">
      {error && <ErrorBox>{t.assign.candidatesFailed(error.message)}</ErrorBox>}
      {!options && !error && <SkeletonLines lines={3} label={t.assign.searching} />}
      {listedAt && (
        <div className="flex flex-wrap items-center gap-2 text-xs text-ink-2">
          <span role="status">{t.assign.listedAt(formatTime(listedAt))}</span>
          <button type="button" className="btn btn-sm" disabled={listing || busy} onClick={() => setRound(n => n + 1)}>
            <RefreshCw className="h-3.5 w-3.5" aria-hidden />
            {listing ? t.common.calculating : t.common.recalculate}
          </button>
        </div>
      )}
      {options && <DriverPicker trip={trip} options={options} driverId={driverId} recommendedId={pick?.DriverId} onChoose={setDriverId} />}
      {hours && <HoursBar hours={hours} />}
      {options && proposal && (
        <ProposalNote
          trip={trip}
          proposal={proposal}
          chosen={vehicleId === proposal.vehicleId}
          onChoose={() => setVehicleId(proposal.vehicleId)}
        />
      )}
      {options && (
        <SearchPicker
          label={t.common.vehicle}
          value={vehicleId}
          items={buses}
          texts={BUS_TEXTS}
          onChange={setVehicleId}
          clearable={false}
          inputClass={FIELD_BORDER}
        />
      )}
      {options && <BusesOut trip={trip} fleet={fleet} candidates={vehicles} />}
      {others.length > 0 && (
        <label className="flex items-center gap-2 text-meta text-ink">
          <input
            type="checkbox"
            className="h-4 w-4 accent-[rgb(var(--blue))]"
            checked={wholeBooking}
            onChange={e => setWholeBooking(e.target.checked)}
          />
          {t.assign.wholeBooking(others.length + 1)}
        </label>
      )}
      {/* Always in sight. The negative margins undo the padding of the form; the bottom one is forced because `space-y` resets it. */}
      <div className="sticky bottom-0 z-10 -mx-4 !-mb-4 space-y-2 rounded-b-[9px] border-t border-ink-3/75 bg-surface px-4 py-3">
        {forcedNote && <p className="rounded-md bg-warning-soft px-3 py-2 text-meta font-medium text-warning-ink">{forcedNote}</p>}
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            className="btn btn-primary"
            disabled={busy || driverId == null || vehicleId == null || unchanged}
            onClick={() => void assign()}
          >
            {busy ? t.common.assigning : t.assign.confirm}
          </button>
          <button type="button" className="btn border-ink-3/75 hover:border-ink-3" onClick={onClose}>
            {t.common.cancel}
          </button>
          {options && (
            <p className="min-w-0 flex-1 basis-60 text-meta text-ink-2">
              {unchanged
                ? t.assign.sameAsNow
                : choiceText(
                    driverId == null ? null : driverNameOf(driverId),
                    vehicleId == null ? null : plateOf(vehicleId),
                    wholeBooking ? others.length + 1 : 1,
                  )}
            </p>
          )}
        </div>
      </div>
    </div>
  );
}

function DriverPicker({
  trip,
  options,
  driverId,
  recommendedId,
  onChoose,
}: {
  trip: BookingTrip;
  options: DriverOptions;
  driverId: number | null;
  recommendedId?: number | null;
  onChoose: (driverId: number | null) => void;
}) {
  const candidates = otherCandidates(trip, options);
  const cards = driverCards(candidates, driverId);
  const items = useMemo(() => driverItems(trip, options, recommendedId), [trip, options, recommendedId]);
  return (
    <fieldset>
      <legend className="sr-only">{t.common.driver}</legend>
      <div className="mb-3">
        <SearchPicker
          label={t.common.driver}
          value={driverId}
          items={items}
          texts={DRIVER_TEXTS}
          onChange={onChoose}
          clearable={false}
          showDetail={false} // Its card says it.
          inputClass={FIELD_BORDER}
        />
      </div>
      {trip.AssignedDriverId != null && (
        <div
          className={`mb-3 flex items-start gap-3 rounded-md bg-surface px-3 py-2.5 shadow-1 ${driverId === trip.AssignedDriverId ? 'ring-2 ring-blue' : ''}`}
        >
          <input
            type="radio"
            name={`driver-${trip.Id}`}
            id={`d-${trip.Id}-${trip.AssignedDriverId}`}
            className="mt-2.5 h-4 w-4 accent-[rgb(var(--blue))]"
            checked={driverId === trip.AssignedDriverId}
            onChange={() => onChoose(trip.AssignedDriverId)}
          />
          <Avatar name={trip.AssignedDriverName} size="sm" />
          <label htmlFor={`d-${trip.Id}-${trip.AssignedDriverId}`} className="min-w-0 flex-1 cursor-pointer text-meta">
            <span className="font-semibold text-ink">{trip.AssignedDriverName ?? `#${trip.AssignedDriverId}`}</span>{' '}
            <Badge tone="muted">{t.common.current}</Badge>
            <span className="mt-0.5 block text-xs text-ink-2">{t.assign.keepsDriver}</span>
          </label>
        </div>
      )}
      <p className="mb-2 text-meta font-semibold text-ink">
        {t.assign.legalDrivers(candidates.length)}
        {cards.length < candidates.length && (
          <span className="font-normal text-ink-2"> {t.assign.firstShown(DRIVER_CARDS)}</span>
        )}
      </p>
      {candidates.length === 0 && (
        <p className="rounded-md bg-warning-soft px-3 py-2 text-meta font-medium text-warning-ink">
          {t.assign.noLegalDriver}
        </p>
      )}
      <ul className="space-y-1.5">
        {cards.map(candidate => (
          <li
            key={candidate.DriverId}
            className={`flex items-start gap-3 rounded-md bg-surface px-3 py-2.5 shadow-1 ${driverId === candidate.DriverId ? 'ring-2 ring-blue' : ''}`}
          >
            <input
              type="radio"
              name={`driver-${trip.Id}`}
              id={`d-${trip.Id}-${candidate.DriverId}`}
              className="mt-2.5 h-4 w-4 accent-[rgb(var(--blue))]"
              checked={driverId === candidate.DriverId}
              onChange={() => onChoose(candidate.DriverId)}
            />
            <Avatar name={candidate.FullName} size="sm" />
            <label htmlFor={`d-${trip.Id}-${candidate.DriverId}`} className="min-w-0 flex-1 cursor-pointer text-meta">
              <span className="font-semibold text-ink">
                #{candidate.Rank} {candidate.FullName}
              </span>{' '}
              <span className="chip">{distanceText(candidate)}</span>{' '}
              {candidateTags(candidate, recommendedId).map(tag => (
                <Fragment key={tag}>
                  <Badge tone="ok">{tag}</Badge>{' '}
                </Fragment>
              ))}
              <span className="mt-0.5 block text-xs text-ink-2">{candidate.Reason}</span>
              {candidate.Warnings.map(warning => (
                <span key={warning} className="block text-xs text-warning-ink">
                  ⚠ {warning}
                </span>
              ))}
            </label>
            <InfoButton explanation={candidate.Explanation} label={t.assign.candidate(candidate.Rank, candidate.FullName)} />
          </li>
        ))}
      </ul>
      {options.Discarded.length > 0 && (
        <details className="group mt-2">
          <summary className="flex cursor-pointer items-center gap-1 text-meta font-medium text-ink-2 hover:text-ink">
            <ChevronDown className="h-4 w-4 transition group-open:rotate-180" aria-hidden />
            {t.assign.discarded(options.Discarded.length)}
          </summary>
          <ul className="mt-1 space-y-1 pl-5 text-xs">
            {options.Discarded.map(discarded => (
              <li key={discarded.DriverId}>
                <strong className="font-semibold text-ink">{discarded.FullName}</strong>:{' '}
                <span className="text-ink-2">{discarded.Reason}</span>
              </li>
            ))}
          </ul>
        </details>
      )}
    </fieldset>
  );
}

/** A fact about each one, not the reason why any other bus is missing. */
function BusesOut({ trip, fleet, candidates }: { trip: BookingTrip; fleet: Vehicle[]; candidates: VehicleCandidate[] }) {
  const out = busesOutByAgenda(trip, fleet, candidates);
  if (out.length === 0) return null;
  return (
    <details className="group">
      <summary className="flex cursor-pointer items-center gap-1 text-meta font-medium text-ink-2 hover:text-ink">
        <ChevronDown className="h-4 w-4 transition group-open:rotate-180" aria-hidden />
        {out.length === 1 ? t.assign.outOne : t.assign.outMany(out.length)}
      </summary>
      <ul className="mt-1 space-y-1 pl-5 text-xs">
        {out.map(({ resource, entries }) => (
          <li key={resource.Id}>
            <strong className="font-semibold text-ink">{resource.LicensePlate}</strong>:{' '}
            <span className="text-ink-2">{entries.map(unavailabilityText).join(' · ')}</span>
          </li>
        ))}
      </ul>
    </details>
  );
}

/** Shown but never preselected, and it does not claim the bus is valid: that is checked on saving (SPEC 0072). */
export function ProposalNote({
  trip,
  proposal,
  chosen,
  onChoose,
}: {
  trip: BookingTrip;
  proposal: BusProposal;
  chosen: boolean;
  onChoose: () => void;
}) {
  const state = proposalState(trip, proposal);
  return (
    <div className="space-y-2 rounded-md bg-surface px-3 py-2.5 text-meta shadow-1">
      <p className="text-ink">
        <Badge tone="muted">{proposedText()}</Badge> <strong className="font-semibold">{proposal.plate}</strong>
        {state === 'done' ? t.assign.alreadyHas : t.assign.insteadOf(trip.AssignedVehiclePlate ?? t.assign.none)}
      </p>
      {state === 'moved' && (
        <p className="text-warning-ink">{t.assign.reportWasFor(proposal.currentPlate)}</p>
      )}
      {state !== 'done' && (
        <>
          <p className="text-xs text-ink-2">
            {t.assign.notChecked}
          </p>
          <button type="button" className="btn btn-sm" aria-pressed={chosen} onClick={onChoose}>
            {chosen ? t.assign.chosen(proposal.plate) : t.assign.choose(proposal.plate)}
          </button>
        </>
      )}
    </div>
  );
}

export function HoursBar({ hours, note = t.assign.beforeThisService }: { hours: DriverHours; note?: string }) {
  return (
    <section aria-label={t.common.drivingHours} className="rounded-md bg-sunken p-3">
      <div className="grid grid-cols-3 gap-4">
        {hourLimits(hours).map(limit => {
          const ratio = limit.max > 0 ? limit.value / limit.max : 0;
          return (
            <div key={limit.label} className="min-w-0">
              <div className="text-xs text-ink-2">{limit.label}</div>
              <div className="text-meta">
                <b className="text-module font-bold text-ink">{formatDecimal(limit.value)}</b>
                <span className="text-ink-2"> / {limit.max} h</span>
              </div>
              <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-muted" aria-hidden>
                <div
                  className={`h-full rounded-full ${ratio >= 1 ? 'bg-critical' : ratio >= 0.9 ? 'bg-warning' : 'bg-blue'}`}
                  style={{ width: `${Math.min(100, ratio * 100)}%` }}
                />
              </div>
              {ratio >= 0.9 && (
                <div className={`mt-1 text-xs font-medium ${ratio >= 1 ? 'text-critical-ink' : 'text-warning-ink'}`}>
                  {ratio >= 1 ? t.common.limitReached : t.common.nearLimit}
                </div>
              )}
            </div>
          );
        })}
      </div>
      <p className="mt-3 text-xs text-ink-2">
        {t.assign.sinceWeeklyRest(formatDecimal(hours.SinceLastWeeklyRest), note)}
      </p>
      <p className="mt-1 text-xs text-ink-3">{hoursNote()}</p>
    </section>
  );
}
