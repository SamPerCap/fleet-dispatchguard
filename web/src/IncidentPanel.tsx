import { t } from './i18n/t';
import type { BarExplanation } from './Timeline';
import { useEffect, useMemo, useRef, useState } from 'react';
import { api, ApiError } from './api';
import { busItems, distanceText, driverCards, leaveItems, replacementItems } from './assignItems';
import { failureTone } from './retry';
import { formatDayTime, parseApiDate } from './gantt';
import { InfoButton } from './ExplanationDialog';
import { Search, UserX } from 'lucide-react';
import { Avatar, IconBadge } from './ui';
import { affectedTrips, defaultLeaveWindow } from './incident';
import { createLatestGuard } from './latest';
import { SearchPicker, type PickerTexts } from './SearchPicker';
import type { Driver, DriverCandidate, DriverOptions, Explanation, Trip, Vehicle, VehicleCandidate } from './types';
import { pickVehicle } from './vehicleChange';

const DRIVER_TEXTS: PickerTexts = { get placeholder() { return t.assign.searchDriver; }, get singular() { return t.picker.driver; }, get plural() { return t.picker.drivers; } };
const BUS_TEXTS: PickerTexts = { get placeholder() { return t.assign.searchBus; }, get singular() { return t.assign.bus; }, get plural() { return t.picker.buses; } };
/** The ones the engine suggests by default; the rest are asked for only if someone searches. */
const SUGGESTED_CARDS = 3;

interface Props {
  onAllocated?: (tripId: number, info: BarExplanation) => void;
  day: Date;
  drivers: Driver[];
  fleet: Vehicle[];
  trips: Trip[];
  describe: (trip: Trip) => string;
  driverName: (id: number | null) => string;
  onChanged: () => Promise<void>;
}

interface TripState {
  loading: boolean;
  candidates?: DriverCandidate[];
  /** Every legal driver and every discarded one, once someone searches. */
  options?: DriverOptions;
  /** Found with the search: it only adds their card. */
  pickedId?: number | null;
  vehicles?: VehicleCandidate[];
  vehicleId?: number;
  error?: Error;
  done?: string;
  doneExplanation?: Explanation | null;
}

const toInput = (date: Date) => new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
const fmt = (instant: string) => formatDayTime(parseApiDate(instant));

export function IncidentPanel({ day, drivers, fleet, trips, describe, driverName, onChanged, onAllocated }: Props) {
  const [driverId, setDriverId] = useState<number | null>(null);
  const [start, setStart] = useState('');
  const [end, setEnd] = useState('');

  const [notes, setNotes] = useState('');
  const [registered, setRegistered] = useState<{ driverId: number; trips: Trip[] } | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [busy, setBusy] = useState(false);
  const [byTrip, setByTrip] = useState<Record<number, TripState>>({});
  const doneIds = useRef(new Set<number>());
  const searchedIds = useRef(new Set<number>());
  const guards = useRef(new Map<number, ReturnType<typeof createLatestGuard>>());
  const people = useMemo(() => leaveItems(drivers), [drivers]);

  // When the day shown changes, the proposed leave covers that day again.
  useEffect(() => {
    const leave = defaultLeaveWindow(day, new Date());
    setStart(toInput(leave.start));
    setEnd(toInput(leave.end));
    setRegistered(null);
  }, [day]);

  const preview = useMemo(
    () => (driverId == null ? [] : affectedTrips(trips, driverId, { start: new Date(start), end: new Date(end) })),
    [trips, driverId, start, end],
  );

  const patch = (id: number, changes: Partial<TripState>) =>
    setByTrip(prev => ({ ...prev, [id]: { ...(prev[id] ?? { loading: false }), ...changes } }));

  // What was chosen in this service survives the recalculation that follows assigning another one, if it is still offered.
  async function loadCandidates(trip: Trip) {
    const guard = guards.current.get(trip.Id) ?? createLatestGuard();
    guards.current.set(trip.Id, guard);
    const current = guard.begin();
    patch(trip.Id, { loading: true, error: undefined });
    try {
      const [options, suggested, vehicles] = await Promise.all([
        searchedIds.current.has(trip.Id) ? api.driverOptions(trip.Id) : undefined,
        searchedIds.current.has(trip.Id) ? undefined : api.driverCandidates(trip.Id),
        trip.AssignedVehicleId == null ? api.everyVehicleCandidate(trip.Id) : undefined,
      ]);
      if (!current()) return;
      const candidates = options?.Candidates ?? suggested ?? [];
      setByTrip(previous => {
        const before = previous[trip.Id];
        const stillOffered = candidates.some(candidate => candidate.DriverId === before?.pickedId);
        return {
          ...previous,
          [trip.Id]: {
            ...before,
            loading: false,
            candidates,
            options,
            pickedId: stillOffered ? before?.pickedId : null,
            vehicles,
            vehicleId: pickVehicle(before?.vehicleId ?? null, assignable(trip), vehicles ?? [], null) ?? undefined,
          },
        };
      });
    } catch (cause) {
      if (current()) patch(trip.Id, { loading: false, error: cause as Error });
    }
  }

  const searchMore = (trip: Trip) => {
    searchedIds.current.add(trip.Id);
    void loadCandidates(trip);
  };

  async function register() {
    if (driverId == null) return;
    const leave = { start: new Date(start), end: new Date(end) };
    setBusy(true);
    setError(null);
    setByTrip({});
    doneIds.current.clear();
    searchedIds.current.clear();
    try {
      if (!(leave.end > leave.start)) throw new Error(t.incident.endAfterStart);
      await api.sickLeave(driverId, leave.start, leave.end, notes);
      // The leave may span other days, so its services come from the API, not only from the day shown. The list is fixed BEFORE
      // reassigning: after a reload those services no longer belong to the driver on leave.
      const affected = affectedTrips(await api.trips(leave.start, leave.end), driverId, leave);
      setRegistered({ driverId, trips: affected });
      await Promise.all(affected.map(loadCandidates));
    } catch (cause) {
      setError(cause as Error);
    } finally {
      setBusy(false);
    }
  }

  async function assign(trip: Trip, candidate: DriverCandidate) {
    const vehicleId = byTrip[trip.Id]?.vehicleId;
    if (vehicleId == null) {
      patch(trip.Id, { error: new Error(t.incident.chooseVehicle) });
      return;
    }
    patch(trip.Id, { loading: true, error: undefined });
    try {
      const res = await api.allocate(trip.Id, candidate.DriverId, vehicleId);
      doneIds.current.add(trip.Id);
      patch(trip.Id, { loading: false, done: candidate.FullName, doneExplanation: res.Explanation });
      if (res.Explanation)
        onAllocated?.(trip.Id, { label: t.incident.explainedFor(candidate.FullName, trip.Id), explanation: res.Explanation });
      // An assignment changes the history of the chosen driver, so the candidates of the other services may be stale.
      const pending = (registered?.trips ?? []).filter(o => !doneIds.current.has(o.Id));
      await Promise.all([onChanged(), ...pending.map(loadCandidates)]);
    } catch (cause) {
      patch(trip.Id, { loading: false, error: cause as Error });
    }
  }

  return (
    <section className="card space-y-4 p-5" aria-labelledby="incident-title">
      <header className="flex items-center gap-3">
        <IconBadge icon={UserX} tone="critical" size="sm" />
        <div className="min-w-0">
          <h2 id="incident-title" className="module-title">
            {t.incident.title}
          </h2>
          <p className="text-xs text-ink-2">{t.incident.intro}</p>
        </div>
      </header>
      <div className="grid gap-3">
        <SearchPicker
          label={t.common.driver}
          value={driverId}
          items={people}
          texts={DRIVER_TEXTS}
          onChange={id => {
            if (id === driverId) return; // Choosing the same one again must not drop the services being reassigned.
            setDriverId(id);
            setRegistered(null);
          }}
        />
        <div className="grid gap-3">
          <label className="block">
            <span className="label">{t.incident.from}</span>
            <input type="datetime-local" className="input mt-1 w-full" value={start} onChange={e => setStart(e.target.value)} />
          </label>
          <label className="block">
            <span className="label">{t.incident.until}</span>
            <input type="datetime-local" className="input mt-1 w-full" value={end} onChange={e => setEnd(e.target.value)} />
          </label>
        </div>
        <label className="block">
          <span className="label">{t.incident.notes}</span>
          <input className="input mt-1 w-full" value={notes} onChange={e => setNotes(e.target.value)} placeholder={t.incident.optional} />
        </label>
        <button type="button" className="btn btn-primary" disabled={driverId == null || busy} onClick={register}>
          {busy ? t.incident.recording : t.incident.record}
        </button>
      </div>
      {driverId != null && !registered && (
        <p className="rounded-md bg-sunken px-3 py-2 text-meta text-ink-2">
          {t.incident.wouldBeLeft(preview.length)}
        </p>
      )}
      {error && <ErrorBox error={error} />}

      {registered && (
        <div className="space-y-3 border-t border-line pt-4">
          <h3 className="text-meta font-semibold text-ink">
            {t.incident.recorded(driverName(registered.driverId), registered.trips.length)}
          </h3>
          {registered.trips.length === 0 && <p className="text-meta text-ink-2">{t.incident.noneOverlaps}</p>}
          {registered.trips.map(trip => {
            const state = byTrip[trip.Id] ?? { loading: false };
            return (
              <article key={trip.Id} className="space-y-3 rounded-md bg-sunken p-3">
                <header className="text-meta">
                  <p className="font-semibold text-ink">
                    #{trip.Id} {describe(trip)}
                  </p>
                  <p className="text-xs text-ink-2">
                    {fmt(trip.Start)} → {fmt(trip.End)}
                  </p>
                </header>
                {state.done && (
                  <p className="flex items-center gap-1 font-medium text-success-ink">
                    {t.incident.reassigned(state.done)}{' '}
                    <InfoButton explanation={state.doneExplanation} label={t.incident.explainedFor(state.done, trip.Id)} />
                  </p>
                )}
                {state.loading && <p className="text-meta text-ink-2">{t.incident.asking}</p>}
                {state.error && <ErrorBox error={state.error} />}
                {!state.done && state.vehicles && (
                  <BusField
                    trip={trip}
                    vehicles={state.vehicles}
                    vehicleId={state.vehicleId ?? null}
                    fleet={fleet}
                    onChoose={vehicleId => patch(trip.Id, { vehicleId: vehicleId ?? undefined })}
                  />
                )}
                {!state.done && state.candidates?.length === 0 && (
                  <p className="text-meta font-medium text-warning-ink">{t.incident.noLegalDrivers}</p>
                )}
                {!state.done &&
                  state.candidates &&
                  (state.options ? (
                    <ReplacementField
                      options={state.options}
                      onLeave={{ id: registered.driverId, name: driverName(registered.driverId) }}
                      pickedId={state.pickedId ?? null}
                      onPick={pickedId => patch(trip.Id, { pickedId })}
                    />
                  ) : (
                    <button type="button" className="btn btn-sm" disabled={state.loading} onClick={() => searchMore(trip)}>
                      <Search className="h-3.5 w-3.5" aria-hidden />
                      {t.incident.anotherDriver}
                    </button>
                  ))}
                {!state.done && state.candidates && state.candidates.length > 0 && (
                  <ol className="space-y-2">
                    {driverCards(state.candidates, state.pickedId ?? null, SUGGESTED_CARDS).map(candidate => (
                      <li
                        key={candidate.DriverId}
                        className={`flex items-start gap-3 rounded-md bg-surface p-2.5 shadow-1 ${candidate.DriverId === state.pickedId ? 'ring-2 ring-blue' : ''}`}
                      >
                        <Avatar name={candidate.FullName} size="sm" />
                        <div className="min-w-0 flex-1 text-meta">
                          <p className="flex flex-wrap items-center gap-1.5">
                            <strong className="font-semibold text-ink">
                              {candidate.Rank}. {candidate.FullName}
                            </strong>
                            <InfoButton explanation={candidate.Explanation} label={t.assign.candidate(candidate.Rank, candidate.FullName)} />
                            <span className="chip">{distanceText(candidate)}</span>
                          </p>
                          <p className="text-xs text-ink-2">{candidate.Reason}</p>
                          {candidate.Warnings.map(warning => (
                            <p key={warning} className="text-xs text-warning-ink">
                              ⚠ {warning}
                            </p>
                          ))}
                        </div>
                        <button
                          type="button"
                          className={`btn btn-sm ${isMain(candidate, state.pickedId) ? 'btn-primary' : ''}`}
                          disabled={state.loading}
                          onClick={() => assign(trip, candidate)}
                        >
                          {t.common.assign}
                        </button>
                      </li>
                    ))}
                  </ol>
                )}
              </article>
            );
          })}
        </div>
      )}
    </section>
  );
}

/** Only one main button per service: the driver found with the search, or the first of the engine. */
const isMain = (candidate: DriverCandidate, pickedId: number | null | undefined) =>
  pickedId == null ? candidate.Rank === 1 : candidate.DriverId === pickedId;

/** The names are only needed to show what is assigned, and here neither the driver nor the bus of the service is an option. */
const assignable = (trip: Trip) => ({ ...trip, AssignedDriverName: null, AssignedVehiclePlate: null });

function BusField({
  trip,
  vehicles,
  vehicleId,
  fleet,
  onChoose,
}: {
  trip: Trip;
  vehicles: VehicleCandidate[];
  vehicleId: number | null;
  fleet: Vehicle[];
  onChoose: (vehicleId: number | null) => void;
}) {
  const items = useMemo(() => busItems(assignable(trip), vehicles, null, null, fleet), [trip, vehicles, fleet]);
  const chosen = vehicles.find(vehicle => vehicle.VehicleId === vehicleId);
  return (
    <div className="flex items-start gap-1">
      <div className="min-w-0 flex-1">
        <SearchPicker label={t.common.vehicle} value={vehicleId} items={items} texts={BUS_TEXTS} onChange={onChoose} clearable={false} />
      </div>
      {chosen && (
        <span className="pt-7">
          <InfoButton explanation={chosen.Explanation} label={t.incident.vehicleOf(chosen.LicensePlate, chosen.Rank)} />
        </span>
      )}
    </div>
  );
}

function ReplacementField({
  options,
  onLeave,
  pickedId,
  onPick,
}: {
  options: DriverOptions;
  onLeave: { id: number; name: string };
  pickedId: number | null;
  onPick: (driverId: number | null) => void;
}) {
  const items = useMemo(() => replacementItems(options, onLeave), [options, onLeave.id, onLeave.name]); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <SearchPicker
      label={t.incident.otherDriver}
      value={pickedId}
      items={items}
      texts={DRIVER_TEXTS}
      onChange={onPick}
      showDetail={false} // Its card says it.
      autoFocus // It replaces the button that was pressed.
    />
  );
}

function ErrorBox({ error }: { error: Error }) {
  const violations = error instanceof ApiError ? error.violations.filter(x => x !== error.detail) : [];
  return (
    <div className={`rounded-md px-3 py-2 text-meta ${failureTone(error instanceof ApiError && error.retryable)}`} role="alert">
      <p className="font-medium">{error.message}</p>
      {violations.length > 0 && (
        <ul className="mt-1 list-disc pl-5">
          {violations.map(violation => (
            <li key={violation}>{violation}</li>
          ))}
        </ul>
      )}
      {error instanceof ApiError && <InfoButton explanation={error.explanation} label={t.common.whyRejected} />}
    </div>
  );
}
