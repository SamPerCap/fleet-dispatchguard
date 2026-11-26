import { t } from './i18n/t';
import { ArrowRight, CalendarCheck2, Check, ChevronDown, Square, TriangleAlert } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api, recommendations } from './api';
import type { MasterData } from './App';
import { createApplyRunner, type ApplyReport } from './applyRun';
import {
  assignLabel,
  confirmation,
  outcome,
  retryLabel,
  retryQueue,
  type AssignTarget,
  type LeftKey,
  type LeftTrip,
  type Names,
} from './assignPeriod';
import type { PanelTarget } from './BookingPanel';
import { InfoButton } from './ExplanationDialog';
import { formatDateTime, formatTime, parseApiDate } from './gantt';
import { createLatestGuard } from './latest';
import { applyQueue, toLines, withResult, type ProposalLine } from './recommend';
import type { BarExplanation } from './Timeline';
import type { Recommendation, Trip } from './types';
import { Badge, EmptyState, ErrorBox, Sheet, SkeletonLines } from './ui';
import type { Tone } from './calendar';

type Phase = 'proposing' | 'failed' | 'confirm' | 'nothing' | 'applying' | 'done';

const KEY: Record<LeftKey, { text: string; tone: Tone }> = {
  noSolution: { get text() { return t.common.noSolution; }, tone: 'warn' },
  rejected: { get text() { return t.common.rejected; }, tone: 'danger' },
  busy: { get text() { return t.common.busy; }, tone: 'warn' },
  limited: { get text() { return t.common.waiting; }, tone: 'warn' },
  unknown: { get text() { return t.common.unconfirmed; }, tone: 'warn' },
  untried: { get text() { return t.period.untried; }, tone: 'muted' },
  leftOut: { get text() { return t.period.overCap; }, tone: 'muted' },
  new: { get text() { return t.period.appearedLater; }, tone: 'muted' },
  departed: { get text() { return t.common.departed; }, tone: 'muted' },
  locked: { get text() { return t.common.locked; }, tone: 'muted' },
  partial: { text: 'A medias', tone: 'warn' },
  elsewhere: { get text() { return t.common.assigned; }, tone: 'ok' },
  cancelled: { get text() { return t.common.cancelled; }, tone: 'muted' },
};

/** Lives in `App`, so switching view does not close it; `hidden` sets it aside (losing nothing) while a trip is assigned by hand. */
export function AssignPeriodPanel({
  target,
  hidden,
  version,
  master,
  openPanel,
  onChanged,
  onAllocated,
  onClose,
}: {
  target: AssignTarget;
  hidden: boolean;
  version: number;
  master: MasterData;
  openPanel: (target: PanelTarget) => void;
  onChanged: () => void;
  onAllocated: (tripId: number, info: BarExplanation) => void;
  onClose: () => void;
}) {
  const { request } = target;
  const [phase, setPhase] = useState<Phase>('proposing');
  const [proposal, setProposal] = useState<Recommendation | null>(null);
  const [lines, setLines] = useState<ProposalLine[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState({ done: 0, total: 0 });
  const [stopping, setStopping] = useState(false);
  const [end, setEnd] = useState<ApplyReport['end'] | null>(null);
  /** The period as stored now; null while it could not be read. */
  const [saved, setSaved] = useState<Trip[] | null>(null);
  const [reading, setReading] = useState<'idle' | 'busy' | 'failed'>('idle');
  const [guard] = useState(createLatestGuard);
  const [readGuard] = useState(createLatestGuard);
  const [runner] = useState(() => createApplyRunner(recommendations.apply));
  const heading = useRef<HTMLHeadingElement>(null);
  const away = useRef(hidden);
  away.current = hidden;
  useEffect(() => () => runner.stop(), [runner]);

  const propose = useCallback(async () => {
    const current = guard.begin();
    setPhase('proposing');
    setError(null);
    setEnd(null);
    setSaved(null);
    readGuard.begin();
    try {
      const recommendation = await recommendations.propose(request);
      if (!current()) return;
      const next = toLines(recommendation);
      setProposal(recommendation);
      setLines(next);
      setPhase(applyQueue(next).length === 0 ? 'nothing' : 'confirm');
    } catch (cause) {
      if (!current()) return;
      setError(cause instanceof Error ? cause.message : String(cause));
      setPhase('failed');
    }
  }, [guard, readGuard, request]);
  useEffect(() => {
    void propose();
    return () => {
      guard.begin();
    };
  }, [propose, guard]);

  /** If the re-read fails, the summary keeps what the operation itself knows and says so. */
  const read = useCallback(async (): Promise<Trip[] | null> => {
    const current = readGuard.begin();
    setReading('busy');
    try {
      const list = await api.trips(new Date(request.From), new Date(request.To));
      if (current()) {
        setSaved(list);
        setReading('idle');
      }
      return list;
    } catch {
      if (current()) {
        setSaved(null);
        setReading('failed');
      }
      return null;
    }
  }, [readGuard, request]);
  // Also when something changes behind it (a trip assigned by hand from the summary).
  const closed = phase === 'done' || phase === 'nothing';
  useEffect(() => {
    if (closed) void read();
  }, [closed, version, read]);

  // Closing or reloading the tab halfway would leave the rest unapplied and without a summary.
  useEffect(() => {
    if (phase !== 'applying') return;
    const warnBeforeLeaving = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = '';
    };
    addEventListener('beforeunload', warnBeforeLeaving);
    return () => removeEventListener('beforeunload', warnBeforeLeaving);
  }, [phase]);

  // Each step changes the buttons: focus goes to the headline that says where we are (so a repeated Enter confirms nothing).
  useEffect(() => {
    if (phase !== 'proposing') heading.current?.focus();
  }, [phase]);

  const apply = async (queue: ProposalLine[]) => {
    if (runner.running) return;
    setProgress({ done: 0, total: queue.length });
    setStopping(false);
    setEnd(null);
    setSaved(null);
    readGuard.begin();
    setPhase('applying');
    const report = await runner.run(queue, {
      onResult: (line, result) => {
        setLines(prev => withResult(prev, line.view.TripId, result));
        setProgress(p => ({ ...p, done: p.done + 1 }));
      },
      onAllocated,
    });
    if (!report) return;
    setEnd(report.end);
    setPhase('done');
    onChanged();
  };
  // Re-reads first: what was stored without a response, or what someone else assigned, is not sent again. If meanwhile it is closed,
  // a booking opens or a new proposal is requested (or it is pressed again), this run does not go on.
  const retry = async () => {
    const current = guard.begin();
    const list = await read();
    if (!list || !current() || away.current) return; // Nothing is sent if it cannot be re-read: the summary says so.
    const queue = retryQueue(lines, list);
    if (queue.length > 0) await apply(queue);
  };
  const stop = () => {
    setStopping(true);
    runner.stop();
  };

  const names: Names = useMemo(() => {
    const drivers = new Map(master.drivers.map(d => [d.Id, `${d.FirstName} ${d.LastName}`]));
    const vehicles = new Map(master.vehicles.map(v => [v.Id, v.LicensePlate]));
    return {
      driver: id => drivers.get(id) ?? t.common.driverId(id),
      vehicle: id => vehicles.get(id) ?? t.common.vehicleId(id),
      location: master.locName,
      customer: id => target.customers.get(id) ?? t.common.bookingId(id),
    };
  }, [master, target.customers]);
  const result =
    proposal && closed ? outcome({ proposal, lines, trips: saved, baseId: request.BaseId ?? null, end, now: new Date(), names }) : null;
  const ask = proposal && (phase === 'confirm' || phase === 'nothing') ? confirmation(proposal, lines) : null;
  const again = phase === 'done' ? retryQueue(lines, saved) : [];
  const againLabel = retryLabel(again);
  const close = () => {
    if (phase === 'applying') stop();
    else onClose();
  };

  return (
    <Sheet open={!hidden} onClose={close} title={assignLabel(target.scale)} wide>
      <div className="space-y-4">
        <dl className="grid gap-3 rounded-lg bg-sunken p-4 text-meta sm:grid-cols-2">
          <div>
            <dt className="text-xs text-ink-2">{t.common.period}</dt>
            <dd className="font-semibold text-ink first-letter:uppercase">{target.period}</dd>
          </div>
          <div>
            <dt className="text-xs text-ink-2">{t.period.tripsBase}</dt>
            <dd className="font-semibold text-ink">{target.base}</dd>
          </div>
        </dl>

        {phase === 'proposing' && <SkeletonLines lines={4} label={t.period.searching} />}

        {phase === 'failed' && (
          <div className="space-y-3">
            <h3 ref={heading} tabIndex={-1} className="text-module font-semibold">
              {t.common.proposalFailed}
            </h3>
            <ErrorBox>{error}</ErrorBox>
            <p className="text-meta text-ink-2">{t.period.nothingSaved}</p>
            <div className="flex gap-2">
              <button type="button" className="btn btn-primary" onClick={() => void propose()}>
                {t.common.retry}
              </button>
              <button type="button" className="btn" onClick={onClose}>
                {t.common.close}
              </button>
            </div>
          </div>
        )}

        {phase === 'confirm' && ask && proposal && (
          <div className="space-y-4">
            <div className="space-y-1.5 rounded-lg bg-blue-soft/60 p-4 text-meta ring-1 ring-blue/25">
              <h3 ref={heading} tabIndex={-1} className="text-module font-semibold text-ink">
                {ask.headline}
              </h3>
              {ask.notes.map(note => (
                <p key={note} className="font-medium text-warning-ink">
                  {note}
                </p>
              ))}
              <p className="text-ink-2">
                {t.period.calculatedAt(formatTime(parseApiDate(proposal.CalculatedAt)), proposal.Note)}
              </p>
            </div>
            <div className="flex flex-wrap gap-2">
              <button type="button" className="btn btn-primary" onClick={() => void apply(applyQueue(lines))}>
                <Check className="h-4 w-4" aria-hidden />
                {ask.button}
              </button>
              <button type="button" className="btn" onClick={onClose}>
                {t.common.cancel}
              </button>
            </div>
          </div>
        )}

        {phase === 'applying' && (
          <div className="space-y-4">
            <h3 ref={heading} tabIndex={-1} className="text-module font-semibold">
              {t.period.assigningTrips(progress.total)}
            </h3>
            <div
              className="h-2 overflow-hidden rounded-full bg-muted"
              role="progressbar"
              aria-label={t.period.triedTrips}
              aria-valuemin={0}
              aria-valuemax={progress.total}
              aria-valuenow={progress.done}
              aria-valuetext={`${progress.done} de ${progress.total}`}
            >
              <div
                className="h-full rounded-full bg-blue"
                style={{ width: `${progress.total ? (progress.done / progress.total) * 100 : 0}%` }}
              />
            </div>
            <p className="text-meta tabular-nums text-ink-2">
              {t.period.triedOf(progress.done, progress.total)}
            </p>
            <p className="text-meta text-ink-2" role="status">
              {stopping
                ? t.period.stoppingNote
                : t.period.closingStops}
            </p>
            <button type="button" className="btn" aria-disabled={stopping} onClick={stop}>
              <Square className="h-4 w-4" aria-hidden />
              {stopping ? t.period.stopping : t.period.stop}
            </button>
          </div>
        )}

        {result && proposal && (
          <div className="space-y-4">
            <div className="space-y-1.5 rounded-lg bg-blue-soft/60 p-4 text-meta ring-1 ring-blue/25">
              <h3 ref={heading} tabIndex={-1} className="text-module font-semibold text-ink">
                {phase === 'nothing' ? ask?.headline : result.headline}
              </h3>
              {phase === 'nothing' && (
                <p className="text-ink-2">{t.period.whichTrips}</p>
              )}
              {phase === 'nothing' &&
                ask?.notes
                  .filter(n => n !== result.note)
                  .map(note => (
                    <p key={note} className="font-medium text-warning-ink">
                      {note}
                    </p>
                  ))}
              {result.note && <p className="font-medium text-warning-ink">{result.note}</p>}
              <p className="text-ink-2" role="status">
                {reading === 'failed'
                  ? t.period.checkFailed
                  : saved
                    ? t.period.checkedNow
                    : t.period.checkingPeriod}
                {reading === 'failed' && (
                  <button type="button" className="btn btn-sm btn-link" onClick={() => void read()}>
                    {t.period.checkAgain}
                  </button>
                )}
              </p>
            </div>

            {result.left.length > 0 ? (
              <LeftList label={t.period.notAssigned} rows={result.left} onOpen={openPanel} />
            ) : (
              phase === 'done' &&
              saved &&
              result.untouched.length === 0 && <EmptyState icon={CalendarCheck2} tone="ok" title={t.period.allAssigned} />
            )}
            {result.untouched.length > 0 && (
              <details className="group rounded-lg bg-sunken">
                <summary className="flex cursor-pointer items-center gap-2 rounded-lg px-4 py-3 text-meta font-semibold text-ink hover:bg-muted">
                  <ChevronDown className="h-4 w-4 transition group-open:rotate-180" aria-hidden />
                  {t.period.untouched(result.untouched.length)}
                </summary>
                <div className="px-4 pb-4">
                  <LeftList label={t.period.untouchedList} rows={result.untouched} onOpen={openPanel} />
                </div>
              </details>
            )}

            <div className="sticky bottom-0 -mx-1 flex flex-wrap items-center gap-2 border-t border-line bg-surface px-1 py-3">
              {againLabel && (
                <button type="button" className="btn btn-primary" disabled={reading === 'busy'} onClick={() => void retry()}>
                  {againLabel}
                </button>
              )}
              <button type="button" className={`btn ${againLabel ? '' : 'btn-primary'}`} onClick={onClose}>
                {t.common.close}
              </button>
              {result.left.some(r => r.pending) && (
                <button type="button" className="btn btn-text" onClick={() => void propose()}>
                  {t.period.proposeAgain}
                </button>
              )}
            </div>
          </div>
        )}
      </div>
    </Sheet>
  );
}

const byHand = (row: LeftTrip) => row.pending && row.key !== 'locked';

function LeftList({ label, rows, onOpen }: { label: string; rows: LeftTrip[]; onOpen: (target: PanelTarget) => void }) {
  return (
    <ul aria-label={label} className="space-y-2">
      {rows.map(row => (
        <li key={row.tripId} className="rounded-lg bg-surface p-4 shadow-1">
          <div className="flex flex-wrap items-center gap-2 text-xs text-ink-2">
            <Badge tone={KEY[row.key].tone}>{KEY[row.key].text}</Badge>
            <span className="font-semibold text-ink">{row.customer}</span>
            <span>
              {formatDateTime(parseApiDate(row.start))} – {formatTime(parseApiDate(row.end))} · #{row.tripId}
            </span>
            {row.explanation && (
              <span className="ml-auto">
                <InfoButton explanation={row.explanation} label={t.period.reasonOf(row.tripId)} />
              </span>
            )}
          </div>
          <p className="mt-1.5 flex flex-wrap items-center gap-1.5 text-meta text-ink">
            {row.origin}
            <ArrowRight className="h-3.5 w-3.5 text-ink-3" aria-label={t.common.to} />
            {row.destination}
          </p>
          <div className="mt-2 flex flex-wrap items-start gap-2">
            <p className="flex min-w-0 flex-1 items-start gap-1.5 text-meta font-medium text-ink">
              {KEY[row.key].tone !== 'ok' && KEY[row.key].tone !== 'muted' && (
                <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-warning-ink" aria-hidden />
              )}
              {row.reason}
            </p>
            <button
              type="button"
              className="btn btn-sm"
              onClick={() => onOpen({ bookingId: row.bookingId, tripId: row.tripId })}
              aria-label={t.period.rowAction(byHand(row) ? t.period.byHand : t.period.seeBooking, row.tripId, row.customer)}
            >
              {byHand(row) ? t.period.byHand : t.period.seeBooking}
            </button>
          </div>
        </li>
      ))}
    </ul>
  );
}
