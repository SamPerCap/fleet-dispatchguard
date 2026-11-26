import { t } from './i18n/t';
import { parseApiDate, TIME_ZONE } from './gantt';
import { useEffect, useRef, useState, useSyncExternalStore, type MutableRefObject } from 'react';
import { api, ApiError } from './api';
import { InfoButton } from './ExplanationDialog';
import { ArrowRight, CalendarX2, FileChartColumn, Info, Route, TrendingDown } from 'lucide-react';
import type { PanelTarget } from './BookingPanel';
import { createLatestGuard } from './latest';
import { saving } from './saving';
import type { VehicleChange } from './types';
import { EmptyState, ErrorBox, IconBadge, Sheet, SkeletonLines, Stat } from './ui';
import { returnNote } from './vehicleChange';
import {
  createReportLoader,
  dateParam,
  departedText,
  isOutdated,
  kmText as km,
  savingText,
  visibleReport,
  weekOf,
  type ReportRequest,
  type ReportState,
} from './optimize';
import { locale } from './language';

const servicesAssigned = (amount: number) => t.optimize.servicesAssigned(km(amount, 0), amount);
const time = (iso: string) =>
  parseApiDate(iso).toLocaleString(locale(), { weekday: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', timeZone: TIME_ZONE });

/** Cards share the width and wrap before becoming too narrow to read. */
const CARD = 'flex-[1_1_13rem]';

/** Where a service was opened from, to return there, and what was stored by then. */
interface Place {
  tripId: number;
  scrollTop: number;
  changes: string;
}

/** What to keep of the report while it is set aside to look at a service. */
export interface KmReport {
  returnToBase: boolean;
  setReturnToBase: (enabled: boolean) => void;
  state: ReportState;
  load: (request: ReportRequest) => Promise<void>;
  place: MutableRefObject<Place | null>;
}

interface Props {
  day: Date;
  assignName: string;
  version: number;
  openPanel: (target: PanelTarget) => void;
}

/** `hidden` sets it aside, losing nothing, while a booking or a resource card is open (SPEC 0072). */
export function OptimizeSheet({ hidden, onClose, ...props }: Props & { hidden: boolean; onClose: () => void }) {
  const [returnToBase, setReturnToBase] = useState(true);
  const [state, setState] = useState<ReportState>({ status: 'idle' });
  const [load] = useState(() => createReportLoader(r => api.vehicleChaining(r.from, r.to, r.returnToBase), setState));
  const place = useRef<Place | null>(null);
  return (
    <Sheet open={!hidden} onClose={onClose} title={t.common.kmSaving} wide>
      <OptimizePanel {...props} hidden={hidden} report={{ returnToBase, setReturnToBase, state, load, place }} />
    </Sheet>
  );
}

/**
 * Read only: it changes no assignment (ADR 0013), and figures of another request are never shown (SPEC 0070). The number of each
 * service opens its booking to change its bus by hand. `assignName` is the current label of the assign button.
 */
export function OptimizePanel({
  day,
  assignName,
  version,
  openPanel,
  report,
  hidden = false,
}: Props & { report: KmReport; hidden?: boolean }) {
  const { returnToBase, setReturnToBase, state, load, place } = report;
  const { pending, settled } = useSyncExternalStore(saving.subscribe, saving.state, saving.state);
  const { from, to } = weekOf(day);
  const request: ReportRequest = { from: dateParam(from), to: dateParam(to), returnToBase, changes: `${version}.${settled}` };
  const outdated = isOutdated(state, request) && pending === 0;
  useEffect(() => {
    if (outdated) void load(request);
  }); // `load` updates it when the request changes.

  const view = visibleReport(state, request, pending > 0);
  const busy = view.status === 'loading';
  const result = view.status === 'done' ? view.result : null;
  const error = view.status === 'failed' ? view.error : null;
  const departed = result ? departedText(result.TripsConsidered, result.TripsDeparted) : '';
  const allDeparted = result != null && result.TripsDeparted === result.TripsConsidered;

  const section = useRef<HTMLElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const [openFailure, setOpenFailure] = useState<{ result: typeof result; text: string } | null>(null);
  const [note, setNote] = useState<{ result: typeof result; text: string } | null>(null);
  const [openGuard] = useState(createLatestGuard);
  // If the report changes or closes while the booking is being looked up, it is no longer opened.
  useEffect(
    () => () => {
      openGuard.begin();
    },
    [openGuard, result],
  );
  const open = async (change: VehicleChange) => {
    const isCurrent = openGuard.begin();
    setOpenFailure(null);
    try {
      const trip = await api.trip(change.TripId);
      if (!isCurrent()) return;
      place.current = { tripId: change.TripId, scrollTop: section.current?.parentElement?.scrollTop ?? 0, changes: request.changes };
      openPanel({
        bookingId: trip.BookingId,
        tripId: change.TripId,
        proposal: {
          vehicleId: change.ProposedVehicleId,
          plate: change.ProposedLicensePlate,
          currentVehicleId: change.CurrentVehicleId,
          currentPlate: change.CurrentLicensePlate,
        },
      });
    } catch (cause) {
      if (isCurrent()) setOpenFailure({ result, text: t.optimize.openFailed(change.TripId, (cause as Error).message) });
    }
  };

  // Coming back from a service: same position and focus on its number; while recalculating, once there are figures.
  // eslint-disable-next-line react-hooks/exhaustive-deps -- runs after every render on purpose: `place` is a ref, consumed once.
  useEffect(() => {
    const back = place.current;
    if (hidden || !back || view.status === 'loading') return; // It is drawn once more when set aside.
    place.current = null;
    const text =
      result &&
      returnNote(
        back.tripId,
        back.changes !== request.changes,
        result.Changes.some(c => c.TripId === back.tripId),
      );
    if (text) setNote({ result, text });
    const scroller = section.current?.parentElement;
    if (scroller) scroller.scrollTop = back.scrollTop;
    const row = section.current?.querySelector<HTMLElement>(`[data-trip="${back.tripId}"]`);
    (row ?? heading.current)?.focus({ preventScroll: true });
    (row ?? heading.current)?.scrollIntoView({ block: 'nearest' });
  });

  return (
    <section ref={section} className="space-y-5" aria-label={t.common.kmSaving}>
      <header className="flex items-start gap-3 rounded-lg bg-sunken p-4">
        <IconBadge icon={FileChartColumn} tone="neutral" size="md" />
        <div className="min-w-0 flex-1 space-y-2 text-meta text-ink-2">
          <h2 ref={heading} tabIndex={-1} className="module-title">
            {t.optimize.weekReport}{' '}
            <span className="text-meta font-normal text-ink-2">
              {from.toLocaleDateString(locale())} – {new Date(to.getTime() - 1).toLocaleDateString(locale())}
            </span>
          </h2>
          <p>
            {t.optimize.whatItDoes}
          </p>
          <p>
            <strong className="font-semibold text-ink">{t.optimize.onlyAReport}</strong> {t.optimize.doesNotAssign(assignName)}
          </p>
          <p>
            {t.optimize.straightLine}
          </p>
        </div>
      </header>
      <div className="flex flex-wrap items-center gap-4">
        <label className="flex items-center gap-2 text-meta text-ink">
          <input
            type="checkbox"
            className="h-4 w-4 accent-[rgb(var(--blue))]"
            checked={returnToBase}
            onChange={e => setReturnToBase(e.target.checked)}
          />
          {t.optimize.backToBase}
        </label>
        <button type="button" className="btn" onClick={() => void load(request)} disabled={busy}>
          <FileChartColumn className="h-4 w-4" aria-hidden />
          {busy ? t.common.calculating : t.optimize.calculate}
        </button>
      </div>
      {error && (
        <div className="flex items-center gap-2 rounded-md bg-critical-soft px-3 py-2 text-meta text-critical-ink" role="alert">
          <p className="font-medium">{t.optimize.failed(error.message)}</p>
          {error instanceof ApiError && <InfoButton explanation={error.explanation} label={t.optimize.whyFailed} />}
        </div>
      )}
      {busy && (
        <SkeletonLines
          lines={4}
          label={pending > 0 ? t.optimize.savingFirst : t.optimize.calculatingReport}
        />
      )}
      {note && note.result === result && (
        <p role="status" className="rounded-md bg-blue-soft px-3 py-2 text-meta font-medium text-ink">
          {note.text}
        </p>
      )}
      {openFailure && openFailure.result === result && <ErrorBox>{openFailure.text}</ErrorBox>}
      {result && result.TripsConsidered === 0 && (
        <EmptyState icon={CalendarX2} title={t.optimize.noneAssigned}>
          {t.optimize.nothingToCompare(assignName)}
        </EmptyState>
      )}
      {result && result.TripsConsidered > 0 && (
        <>
          <div className="flex flex-wrap gap-3">
            <Stat
              className={CARD}
              icon={Route}
              tone="neutral"
              label={t.optimize.emptyNow}
              value={km(result.BaselineDeadheadKm)}
              foot={servicesAssigned(result.TripsConsidered) + (departed && ` (${departed})`)}
            />
            <Stat
              className={CARD}
              icon={FileChartColumn}
              tone={result.Changes.length ? 'blue' : 'neutral'}
              label={t.optimize.emptyProposed}
              value={km(result.ProposedDeadheadKm)}
              foot={
                result.Changes.length ? t.optimize.busChanges(km(result.Changes.length, 0), result.Changes.length) : t.optimize.same
              }
            />
            <Stat
              className={CARD}
              icon={TrendingDown}
              tone={result.Changes.length ? 'success' : 'neutral'}
              label={t.optimize.saving}
              value={savingText(result.Changes.length, result.SavingPercent)}
              foot={t.optimize.straightLineAt(result.AverageSpeedKmh)}
            />
          </div>
          <div className="flex items-center gap-1 text-meta text-ink-2">
            <p className="min-w-0">
              {result.Changes.length === 0 ? (
                <>
                  <strong className="text-ink">{t.optimize.nothingToSave}</strong> {t.optimize.emptyKm(km(result.BaselineDeadheadKm))}
                </>
              ) : (
                <>
                  {t.optimize.kmFrom(km(result.BaselineDeadheadKm))} <strong className="text-ink">{t.optimize.kmTo(km(result.ProposedDeadheadKm))}</strong> {t.optimize.emptySaving} <strong className="text-success-ink">{savingText(result.Changes.length, result.SavingPercent)}</strong>)
                </>
              )}
              {t.optimize.basis(servicesAssigned(result.TripsConsidered), departed && ` (${departed})`, result.AverageSpeedKmh)}
            </p>
            <InfoButton explanation={result.Explanation} label={t.optimize.summary} />
          </div>
          {result.Changes.length === 0 ? (
            <EmptyState icon={Info} title={t.optimize.noChanges}>
              {result.Explanation.Summary}
              {!allDeparted && t.optimize.notExhaustive}
            </EmptyState>
          ) : (
            <div className="card overflow-x-auto">
              <table className="tbl">
                <thead>
                  <tr>
                    <th scope="col">{t.common.service}</th>
                    <th scope="col">{t.common.departure}</th>
                    <th scope="col">{t.optimize.currentBus}</th>
                    <th scope="col">{t.optimize.proposedBus}</th>
                    <th scope="col">
                      <span className="sr-only">{t.common.explanation}</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {result.Changes.map(change => (
                    <tr key={change.TripId}>
                      <td>
                        <button
                          type="button"
                          data-trip={change.TripId}
                          className="btn btn-sm btn-link"
                          onClick={() => void open(change)}
                          aria-label={t.optimize.openService(change.TripId)}
                        >{`#${change.TripId}`}</button>
                      </td>
                      <td className="text-meta text-ink-2">{time(change.Start)}</td>
                      <td className="text-meta text-ink-2">{change.CurrentLicensePlate}</td>
                      <td>
                        <span className="inline-flex items-center gap-1.5 font-semibold text-ink">
                          <ArrowRight className="h-3.5 w-3.5 text-ink-3" aria-hidden />
                          {change.ProposedLicensePlate}
                        </span>
                      </td>
                      <td>
                        <InfoButton explanation={change.Explanation} label={t.optimize.changeOf(change.TripId, change.ProposedLicensePlate)} />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </section>
  );
}
