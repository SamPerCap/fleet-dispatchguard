import { t } from '../i18n/t';
import { BRAND } from '../brand';
import * as ToggleGroup from '@radix-ui/react-toggle-group';
import {
  AlertOctagon,
  BusFront,
  CalendarCheck2,
  CalendarRange,
  ChevronLeft,
  ChevronRight,
  CircleCheck,
  FileChartColumn,
  Info,
  RotateCcw,
  ShieldAlert,
  Wand2,
  X,
} from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, ApiError, control } from '../api';
import type { MasterData } from '../App';
import type { PanelTarget } from '../BookingPanel';
import { assignLabel, type AssignTarget } from '../assignPeriod';
import { isUnassigned, overlaps, planningStats, rangeLabel, rangeOf, shift, type Scale } from '../calendar';
import { formatDayTime, parseApiDate } from '../gantt';
import { createLatestGuard } from '../latest';
import { OptimizeSheet } from '../OptimizePanel';
import { RecommendPanel } from '../RecommendPanel';
import { agendaConflicts } from '../resources';
import { tripStatus } from '../status';
import { read, write } from '../storage';
import { Timeline, type BarExplanation, type TimelineRow, type TimelineTrip } from '../Timeline';
import { EmptyState, ErrorBox, IconBadge, PageHeader, Sheet, Skeleton, SkeletonLines, Stat, StatusBadge, Toolbar } from '../ui';
import type { Booking, OperatingBase, RecommendationRequest } from '../types';

const BANNER_KEY = 'dispatchguard.welcomeDismissed';
const readDismissed = () => read(BANNER_KEY) === '1';

interface Props {
  master: MasterData;
  version: number;
  explained: Map<number, BarExplanation>;
  openPanel: (target: PanelTarget) => void;
  onChanged: () => void;
  onAllocated: (tripId: number, info: BarExplanation) => void;
  /** `App` opens it, so that switching view does not close it. */
  onAssign: (target: AssignTarget) => void;
  /** The home report link opens "Ahorro de km" only once. */
  startOptimizing?: boolean;
  onOptimizerOpened?: () => void;
  /** A booking or a resource card is open: the report is set aside (no stacked modals). */
  covered?: boolean;
}

function WelcomeBanner() {
  const [dismissed, setDismissed] = useState(readDismissed);
  if (dismissed) return null;
  return (
    <section aria-label={t.planning.welcome} className="mb-6 flex items-start gap-3 rounded-lg bg-blue-soft px-4 py-3 text-ink">
      <IconBadge icon={Info} tone="blue" size="sm" />
      <p className="min-w-0 flex-1 pt-1.5 text-meta">
        <strong className="font-semibold">{t.planning.demoOf(BRAND.name)}</strong> {t.planning.demoWhat} <strong className="font-semibold">{t.planning.noPersonalData}</strong>.
      </p>
      <button
        type="button"
        className="btn btn-text btn-icon"
        aria-label={t.planning.closeWelcome}
        onClick={() => {
          setDismissed(true);
          write(BANNER_KEY, '1');
        }}
      >
        <X className="h-4 w-4" aria-hidden />
      </button>
    </section>
  );
}

function PeriodFigures({ stats, period }: { stats: ReturnType<typeof planningStats> | null; period: string }) {
  return (
    <div className="mb-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-4" aria-label={t.planning.figures} role="group">
      {stats === null ? (
        [0, 1, 2, 3].map(index => (
          <div key={index} className="card h-[88px] p-4">
            <Skeleton className="h-full w-full" />
          </div>
        ))
      ) : (
        <>
          <Stat icon={CalendarCheck2} label={t.planning.services} value={stats.total} foot={period} />
          <Stat
            icon={CircleCheck}
            tone="success"
            label={t.planning.covered}
            value={`${stats.coveredPct} %`}
            foot={`${stats.covered} de ${stats.total}`}
          />
          <Stat
            icon={AlertOctagon}
            tone={stats.pending ? 'critical' : 'neutral'}
            label={t.planning.unassigned}
            value={stats.pending}
            foot={stats.pending ? t.planning.pending : 'nada pendiente'}
          />
          <Stat
            icon={ShieldAlert}
            tone={stats.forced ? 'warning' : 'neutral'}
            label={t.planning.forced}
            value={stats.forced}
            foot="CE 561/2006, auditados"
          />
        </>
      )}
    </div>
  );
}

function QueueItem({
  trip,
  route,
  now,
  first,
  onAssign,
}: {
  trip: TimelineTrip;
  route: string;
  now: Date;
  first: boolean;
  onAssign: () => void;
}) {
  const status = tripStatus(trip, now);
  return (
    <li className="relative w-72 shrink-0 overflow-hidden rounded-md bg-surface p-3 pl-4 shadow-1 xl:w-auto">
      <span aria-hidden className={`absolute inset-y-0 left-0 w-1 ${status === 'uncovered' ? 'bg-critical' : 'bg-ink-3/40'}`} />
      <div className="flex items-start justify-between gap-2">
        <p className="min-w-0 truncate font-semibold text-ink" title={trip.customer}>
          {trip.customer}
        </p>
        {status && <StatusBadge status={status} />}
      </div>
      <p className="mt-0.5 truncate text-xs text-ink-2" title={route}>
        {route}
      </p>
      <div className="mt-2 flex items-center justify-between gap-2">
        <span className="text-meta font-medium text-ink">{formatDayTime(parseApiDate(trip.Start))}</span>
        <button
          type="button"
          className={`btn btn-sm ${first ? 'btn-primary' : ''}`}
          aria-label={t.planning.assignService(trip.Id, trip.customer)}
          onClick={onAssign}
        >
          {t.common.assign}
        </button>
      </div>
    </li>
  );
}

export function PlanningPage({
  master,
  version,
  explained,
  openPanel,
  onChanged,
  onAllocated,
  onAssign,
  startOptimizing = false,
  onOptimizerOpened,
  covered = false,
}: Props) {
  const [anchor, setAnchor] = useState(() => new Date());
  const [scale, setScale] = useState<Scale>('week');
  const [by, setBy] = useState<'drivers' | 'vehicles'>('drivers');
  const [bookings, setBookings] = useState<Booking[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [optimizing, setOptimizing] = useState(startOptimizing);
  useEffect(() => {
    if (startOptimizing) onOptimizerOpened?.();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  const [resetOpen, setResetOpen] = useState(false);
  const [bases, setBases] = useState<OperatingBase[]>([]);
  const [baseId, setBaseId] = useState<number | null>(null);
  // The proposal is requested on opening and does not change with what happens behind it.
  const [recommending, setRecommending] = useState<RecommendationRequest | null>(null);
  useEffect(() => {
    control
      .bases()
      .then(setBases)
      .catch(() => undefined);
  }, []); // Without the list, every base is recommended.
  const range = useMemo(() => rangeOf(anchor, scale), [anchor, scale]);

  // A late response (e.g. the current week after moving to another date) does not undo the navigation.
  const [guard] = useState(createLatestGuard);
  const load = useCallback(async () => {
    const isCurrent = guard.begin();
    try {
      const result = await api.bookings(range.from, range.to);
      if (!isCurrent()) return;
      setBookings(result);
      setError(null);
    } catch (cause) {
      if (isCurrent()) setError((cause as Error).message);
    }
  }, [range, guard]);
  useEffect(() => {
    void load();
  }, [load, version]);

  const trips: TimelineTrip[] = useMemo(
    () =>
      (bookings ?? []).flatMap(booking =>
        booking.Trips.filter(t => t.Status !== 'Cancelled' && overlaps(t, range)).map(t => ({
          ...t,
          bookingId: booking.Id,
          customer: booking.CustomerName,
        })),
      ),
    [bookings, range],
  );
  const queue = useMemo(() => trips.filter(isUnassigned).sort((a, b) => a.Start.localeCompare(b.Start)), [trips]);
  const stats = useMemo(() => planningStats(trips), [trips]);
  const conflicts = useMemo(
    () => new Map(trips.map(trip => [trip.Id, agendaConflicts(trip, master.drivers, master.vehicles, new Date())])),
    [trips, master.drivers, master.vehicles],
  );

  const rows: TimelineRow[] = useMemo(
    () =>
      by === 'drivers'
        ? master.drivers
            .filter(d => d.IsActive)
            .map(driver => ({
              key: `d${driver.Id}`,
              label: `${driver.FirstName} ${driver.LastName}`,
              sub: driver.IdentificationNumber,
              kind: 'driver' as const,
              trips: trips.filter(t => t.AssignedDriverId === driver.Id),
              absences: driver.Unavailabilities,
            }))
        : master.vehicles
            .filter(v => v.IsActive)
            .map(vehicle => ({
              key: `v${vehicle.Id}`,
              label: vehicle.LicensePlate,
              sub: t.card.vehicleInfo(vehicle.Brand, vehicle.Model, vehicle.SeatingCapacity),
              kind: 'vehicle' as const,
              trips: trips.filter(t => t.AssignedVehicleId === vehicle.Id),
              absences: vehicle.Unavailabilities,
            })),
    [by, master, trips],
  );

  const route = (trip: TimelineTrip) => `${master.locName(trip.OriginLocationId)} → ${master.locName(trip.DestinationLocationId)}`;
  const describe = (trip: TimelineTrip) =>
    by === 'drivers' ? route(trip) : `${route(trip)} · ${trip.AssignedDriverName ?? t.common.noDriverLower}`;
  const now = new Date();
  const request = { BaseId: baseId, From: range.from.toISOString(), To: range.to.toISOString() };
  const baseName = (id: number | null) => (id == null ? t.common.allBases : (bases.find(b => b.Id === id)?.Name ?? t.planning.baseNumber(id)));
  const assignPeriod = () =>
    onAssign({
      request,
      scale,
      period: rangeLabel(range, scale),
      base: baseName(baseId),
      customers: new Map((bookings ?? []).map(b => [b.Id, b.CustomerName])),
    });

  return (
    <div className="page">
      <WelcomeBanner />
      <PageHeader
        title={t.app.planning}
        icon={CalendarRange}
        subtitle={t.planning.intro}
      >
        <button
          type="button"
          className="btn btn-text"
          onClick={() => setOptimizing(true)}
          title={t.planning.reportHint}
        >
          <FileChartColumn className="h-4 w-4" aria-hidden />
          {t.common.kmSaving}
        </button>
        <button type="button" className="btn" onClick={() => setResetOpen(true)}>
          <RotateCcw className="h-4 w-4" aria-hidden />
          {t.planning.resetDemo}
        </button>
        <button
          type="button"
          className="btn btn-primary"
          onClick={assignPeriod}
          title={t.planning.assignHint}
        >
          <BusFront className="h-4 w-4" aria-hidden />
          {assignLabel(scale)}
        </button>
      </PageHeader>

      <PeriodFigures stats={bookings === null ? null : stats} period={rangeLabel(range, scale)} />

      <Toolbar>
        <button type="button" className="btn" onClick={() => setAnchor(new Date())}>
          {t.common.today}
        </button>
        <button type="button" className="btn btn-text btn-icon" aria-label={t.common.previous} onClick={() => setAnchor(shift(anchor, scale, -1))}>
          <ChevronLeft className="h-4 w-4" aria-hidden />
        </button>
        <button type="button" className="btn btn-text btn-icon" aria-label={t.common.next} onClick={() => setAnchor(shift(anchor, scale, 1))}>
          <ChevronRight className="h-4 w-4" aria-hidden />
        </button>
        <input
          type="date"
          className="input"
          aria-label={t.planning.goToDate}
          value={dateInput(anchor)}
          onChange={e => e.target.value && setAnchor(new Date(`${e.target.value}T00:00`))}
        />
        <h2 className="mr-auto pl-2 text-module font-semibold first-letter:uppercase" aria-live="polite">
          {rangeLabel(range, scale)}
        </h2>
        <ToggleGroup.Root type="single" className="seg" value={scale} aria-label={t.planning.scale} onValueChange={v => v && setScale(v as Scale)}>
          <ToggleGroup.Item value="day">{t.common.day}</ToggleGroup.Item>
          <ToggleGroup.Item value="week">{t.common.week}</ToggleGroup.Item>
          <ToggleGroup.Item value="month">{t.common.month}</ToggleGroup.Item>
        </ToggleGroup.Root>
        <ToggleGroup.Root type="single" className="seg" value={by} aria-label={t.common.rows} onValueChange={v => v && setBy(v as typeof by)}>
          <ToggleGroup.Item value="drivers">{t.common.drivers}</ToggleGroup.Item>
          <ToggleGroup.Item value="vehicles">{t.common.vehicles}</ToggleGroup.Item>
        </ToggleGroup.Root>
      </Toolbar>

      {error && (
        <div className="mb-4">
          <ErrorBox>{t.planning.failed(error)}</ErrorBox>
        </div>
      )}
      <div className="grid gap-4 xl:grid-cols-[300px_minmax(0,1fr)] 2xl:grid-cols-[340px_minmax(0,1fr)] 3xl:grid-cols-[380px_minmax(0,1fr)]">
        <section aria-label={t.planning.unassignedQueue} className="card min-w-0 self-start p-4">
          <h3 className="mb-3 flex items-center gap-2 text-module font-semibold">
            {t.planning.unassignedQueue}
            <span
              className={`rounded-full px-2 py-0.5 text-xs font-semibold ${queue.length ? 'bg-critical-soft text-critical-ink' : 'bg-muted text-ink-2'}`}
            >
              {queue.length}
            </span>
          </h3>
          <div className="mb-3 flex flex-wrap items-end gap-2">
            {bases.length > 1 && (
              <label className="min-w-0 flex-1 text-xs font-medium text-ink-2">
                {t.planning.recommendBase}
                <select className="input mt-1 w-full" value={baseId ?? ''} onChange={e => setBaseId(Number(e.target.value) || null)}>
                  <option value="">{t.common.allBases}</option>
                  {bases.map(base => (
                    <option key={base.Id} value={base.Id}>
                      {base.Name}
                    </option>
                  ))}
                </select>
              </label>
            )}
            <button type="button" className="btn" disabled={queue.length === 0} onClick={() => setRecommending(request)}>
              <Wand2 className="h-4 w-4" aria-hidden />
              {t.planning.recommendAction}
            </button>
          </div>
          {bookings === null ? (
            <SkeletonLines lines={4} label={t.planning.loadingQueue} />
          ) : queue.length === 0 ? (
            <EmptyState icon={CircleCheck} tone="ok" title={t.period.allAssigned}>
              {t.planning.nonePending}
            </EmptyState>
          ) : (
            <ul className="-mx-1 flex gap-2 overflow-x-auto px-1 pb-2 pt-0.5 xl:block xl:max-h-[60vh] xl:space-y-2 xl:overflow-y-auto xl:overflow-x-visible xl:pb-0.5">
              {queue.map((trip, index) => (
                <QueueItem
                  key={trip.Id}
                  trip={trip}
                  route={route(trip)}
                  now={now}
                  first={index === 0}
                  onAssign={() => openPanel({ bookingId: trip.bookingId, tripId: trip.Id })}
                />
              ))}
            </ul>
          )}
        </section>
        {bookings === null ? (
          <div className="card p-4">
            <SkeletonLines lines={8} label={t.planning.loadingTimeline} />
          </div>
        ) : (
          <Timeline
            rows={rows}
            range={range}
            scale={scale}
            describe={describe}
            explain={t => explained.get(t.Id)}
            conflicts={conflicts}
            onOpen={t => openPanel({ bookingId: t.bookingId, tripId: t.Id })}
          />
        )}
      </div>

      {optimizing && (
        <OptimizeSheet
          hidden={covered}
          onClose={() => setOptimizing(false)}
          day={anchor}
          assignName={assignLabel(scale)}
          version={version}
          openPanel={openPanel}
        />
      )}
      <Sheet
        open={recommending != null}
        onClose={() => setRecommending(null)}
        wide
        title={t.planning.recommendTitle(recommending?.BaseId == null ? t.common.allBasesLower : baseName(recommending.BaseId), rangeLabel(range, scale))}
      >
        {recommending && (
          <RecommendPanel request={recommending} onApplied={onChanged} onAllocated={onAllocated} onClose={() => setRecommending(null)} />
        )}
      </Sheet>
      <ResetDialog open={resetOpen} onClose={() => setResetOpen(false)} onDone={onChanged} />
    </div>
  );
}

const dateInput = (date: Date) =>
  `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;

/** Without the key the backend answers 401; if it is not configured, 404. */
function ResetDialog({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const [key, setKey] = useState('');
  const [state, setState] = useState<{ busy?: boolean; ok?: boolean; error?: string }>({});
  const run = async () => {
    setState({ busy: true });
    try {
      await api.resetDemo(key);
      setState({ ok: true });
      onDone();
    } catch (cause) {
      const status = cause instanceof ApiError ? cause.status : 0;
      setState({
        error:
          status === 404 ? t.planning.noReset : status === 401 ? t.planning.wrongKey : (cause as Error).message,
      });
    }
  };
  return (
    <Sheet
      open={open}
      onClose={() => {
        setState({});
        setKey('');
        onClose();
      }}
      title={t.planning.resetDemo}
    >
      <div className="space-y-4">
        <p className="text-ink-2">
          {t.planning.resetWhat}
        </p>
        <label className="block">
          <span className="label">{t.planning.resetKey}</span>
          <input
            id="reset-key"
            type="password"
            className="input mt-1 w-full"
            value={key}
            onChange={e => setKey(e.target.value)}
            autoComplete="off"
          />
        </label>
        <button type="button" className="btn btn-danger" disabled={!key || state.busy} onClick={() => void run()}>
          <RotateCcw className="h-4 w-4" aria-hidden />
          {state.busy ? t.planning.resetting : t.planning.reset}
        </button>
        {state.ok && (
          <p role="status" className="font-medium text-success-ink">
            {t.planning.resetDone}
          </p>
        )}
        {state.error && <ErrorBox>{state.error}</ErrorBox>}
      </div>
    </Sheet>
  );
}
