import { t } from './i18n/t';
import { whoText } from './people';
import * as Dialog from '@radix-ui/react-dialog';
import * as Tabs from '@radix-ui/react-tabs';
import {
  ArrowLeft,
  ArrowRight,
  BusFront,
  CalendarClock,
  CalendarRange,
  ChevronLeft,
  ChevronRight,
  CircleCheck,
  CircleSlash,
  Clock,
  History,
  ListChecks,
  MapPin,
  Route,
  ShieldOff,
  X,
  type LucideIcon,
} from 'lucide-react';
import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AgendaTab } from './AgendaTab';
import { api, control, engine } from './api';
import type { MasterData } from './App';
import { DayBars } from './charts';
import { DetailsSection } from './DetailsSection';
import { ruleIndex, rangeText } from './engine';
import { addDays, formatTime, parseApiDate, TIME_ZONE } from './gantt';
import {
  auditParams,
  barTone,
  chartCeiling,
  dayLabel,
  dayMarks,
  formatDecimal,
  formatHours,
  formatKm,
  historyText,
  weeklyRestText,
} from './history';
import { hoursNote, hourLimits } from './hours';
import { OriginBadge, OutcomeBadge } from './home/Activity';
import type { Loaded } from './home/data';
import { AttemptDetail } from './pages/EnginePage';
import { shortOfSeatsText } from './resourceDetails';
import {
  isUnavailableAt,
  overlappingUnavailabilities,
  unavailabilityText,
  upcomingTrips,
  vehiclePosition,
} from './resources';
import type { ResourceKind, ResourceRef, ResourceTab } from './resourceUrl';
import { tripStatus } from './status';
import type {
  ActivityKind,
  Driver,
  DriverHours,
  EngineAttempt,
  EngineAuditPage,
  HistoryTrip,
  OperatingBase,
  ResourceHistory,
  RuleGroup,
  Trip,
  Vehicle,
} from './types';
import { Avatar, EmptyState, ErrorState, IconBadge, Section, SkeletonLines, StatusBadge } from './ui';
import { useLoad } from './useLoad';
import { locale } from './language';

const HISTORY_PAGE = 15;
const AUDIT_PAGE = 15;
const TABS: { id: ResourceTab; label: string; icon: LucideIcon }[] = [
  { id: 'resumen', get label() { return t.card.summary; }, icon: CircleCheck },
  { id: 'historial', get label() { return t.card.history; }, icon: History }, // The API says how many days (`DaysCount`).
  { id: 'auditoria', get label() { return t.card.audit; }, icon: ListChecks },
  { id: 'agenda', get label() { return t.card.agenda; }, icon: CalendarRange },
];

/** Everything that leaves the card closes it first (no stacked modals). */
export interface ResourceModalActions {
  onTab: (tab: ResourceTab) => void;
  onClose: () => void;
  onOpenTrip: (bookingId: number, tripId: number) => void;
  /** For when only the trip is known (audit attempts). */
  onOpenTripId: (tripId: number) => void;
  onCatalog: (groupKey?: string) => void;
  /** Something was stored: the whole app reloads. */
  onChanged: () => void;
}

const fmtDate = (instant: string) =>
  parseApiDate(instant).toLocaleString(locale(), {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: TIME_ZONE,
  });
const fmtWhen = (instant: string) =>
  parseApiDate(instant).toLocaleString(locale(), {
    weekday: 'short',
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: TIME_ZONE,
  });
const fmtDay = (instant: string) =>
  parseApiDate(instant).toLocaleDateString(locale(), { weekday: 'short', day: 'numeric', month: 'short', timeZone: TIME_ZONE });

/** Bases rarely change: one request per visit. */
let basesOnce: Promise<OperatingBase[]> | null = null;
const loadBases = () =>
  (basesOnce ??= control.bases().catch(cause => {
    basesOnce = null;
    throw cause;
  }));

function titleOf(target: ResourceRef | null, driver: Driver | null, vehicle: Vehicle | null): string {
  if (driver) return `${driver.FirstName} ${driver.LastName}`;
  if (vehicle) return `${vehicle.LicensePlate} · ${vehicle.Brand} ${vehicle.Model}`;
  return target?.kind === 'vehicle' ? t.common.vehicle : t.common.driver;
}

export function ResourceModal({
  target,
  master,
  masterError,
  version,
  actions,
}: {
  target: ResourceRef | null;
  master: MasterData;
  masterError?: string | null;
  version: number;
  actions: ResourceModalActions;
}) {
  const driver = target?.kind === 'driver' ? (master.drivers.find(d => d.Id === target.id) ?? null) : null;
  const vehicle = target?.kind === 'vehicle' ? (master.vehicles.find(v => v.Id === target.id) ?? null) : null;
  const loadedList = target?.kind === 'driver' ? master.drivers.length > 0 : master.vehicles.length > 0;
  const title = titleOf(target, driver, vehicle);
  // Radix returns focus only to a Dialog.Trigger and this card opens from many places (list, alerts, Ctrl+K, URL): the focused
  // element is kept on opening and gets it back on closing, unless another dialog (the booking panel) already has it.
  const returnFocus = useRef<HTMLElement | null>(null);
  return (
    <Dialog.Root
      open={target != null}
      onOpenChange={nextOpen => {
        if (!nextOpen) actions.onClose();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-navy/40" />
        <Dialog.Content
          aria-describedby={undefined}
          onOpenAutoFocus={() => {
            returnFocus.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
          }}
          onCloseAutoFocus={event => {
            event.preventDefault();
            const element = returnFocus.current;
            returnFocus.current = null;
            const free = !document.activeElement || document.activeElement === document.body;
            if (free && element?.isConnected) element.focus();
          }}
          className="fixed left-1/2 top-1/2 z-40 flex h-[min(920px,calc(100vh-48px))] w-[min(1180px,calc(100vw-48px))] -translate-x-1/2 -translate-y-1/2 flex-col overflow-hidden rounded-lg bg-surface shadow-pop 3xl:w-[min(1320px,calc(100vw-96px))]"
        >
          {target &&
            (driver || vehicle ? (
              <ResourceCard
                key={`${target.kind}-${target.id}`}
                target={target}
                driver={driver}
                vehicle={vehicle}
                title={title}
                master={master}
                version={version}
                actions={actions}
              />
            ) : (
              <>
                <header className="flex items-center justify-between border-b border-line px-6 py-4">
                  <Dialog.Title className="text-module font-semibold">{title}</Dialog.Title>
                  <Dialog.Close className="btn btn-text btn-icon" aria-label={t.card.close}>
                    <X className="h-4 w-4" aria-hidden />
                  </Dialog.Close>
                </header>
                <div className="p-6">
                  {masterError && !loadedList ? (
                    <ErrorState title={t.card.masterFailed}>{masterError}</ErrorState>
                  ) : loadedList ? (
                    <EmptyState
                      icon={CircleSlash}
                      title={t.card.missing(target.kind === 'driver' ? t.card.theDriver : t.card.theVehicle, target.id)}
                    >
                      {t.card.oldLink}
                    </EmptyState>
                  ) : (
                    <SkeletonLines lines={6} label={t.card.loading} />
                  )}
                </div>
              </>
            ))}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function ResourceCard({
  target,
  driver,
  vehicle,
  title,
  master,
  version,
  actions,
}: {
  target: ResourceRef;
  driver: Driver | null;
  vehicle: Vehicle | null;
  title: string;
  master: MasterData;
  version: number;
  actions: ResourceModalActions;
}) {
  const kind = target.kind;
  const id = target.id;
  const now = useMemo(() => new Date(), [version]); // eslint-disable-line react-hooks/exhaustive-deps
  // 7 days back decide the location (SPEC 0032); 15 ahead, the upcoming services and today's state.
  const [trips, retryTrips] = useLoad(() => api.trips(addDays(now, -7), addDays(now, 15)), [now]);
  const [bases, retryBases] = useLoad(loadBases, []);
  const own = useMemo(
    () => (trips?.ok ? trips.value.filter(t => (kind === 'driver' ? t.AssignedDriverId : t.AssignedVehicleId) === id) : []),
    [trips, kind, id],
  );
  const baseName = (baseId?: number) =>
    baseId == null ? '—' : bases?.ok ? (bases.value.find(b => b.Id === baseId)?.Name ?? t.common.baseId(baseId)) : '…';
  const resource = driver ?? vehicle!;
  const unavailable = isUnavailableAt(resource.Unavailabilities, now);
  const running = own.find(t => tripStatus(t, now) === 'running');
  const laterToday =
    !running && own.find(trip => tripStatus(trip, now) === 'assigned' && parseApiDate(trip.Start).toDateString() === now.toDateString());

  return (
    <>
      <header className="flex items-start gap-4 border-b border-line px-6 pb-4 pt-5">
        {driver ? (
          <span className="[&>span]:h-12 [&>span]:w-12 [&>span]:text-module">
            <Avatar name={title} />
          </span>
        ) : (
          <IconBadge icon={BusFront} tone="navy" size="lg" />
        )}
        <div className="min-w-0 flex-1">
          <Dialog.Title className="truncate text-page font-bold text-ink">{driver ? title : vehicle!.LicensePlate}</Dialog.Title>
          <p className="mt-0.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-meta text-ink-2">
            <span>
              {driver ? t.common.idNumberOf(driver.IdentificationNumber) : t.card.vehicleInfo(vehicle!.Brand, vehicle!.Model, vehicle!.SeatingCapacity)}
            </span>
            <span aria-hidden className="text-ink-3">
              ·
            </span>
            <span className="inline-flex items-center gap-1">
              <MapPin className="h-3.5 w-3.5 text-ink-3" aria-hidden />
              {t.common.baseId(baseName(resource.BaseId))}
            </span>
          </p>
          <div className="mt-2 flex flex-wrap items-center gap-1.5" aria-label={t.card.today} role="group">
            <ResourceState active={resource.IsActive} unavailable={unavailable} external={resource.IsExternal} />
            {running && (
              <StatusBadge
                status="running"
                label={t.card.inService}
                title={t.card.runningUntil(master.locName(running.OriginLocationId), master.locName(running.DestinationLocationId), formatTime(parseApiDate(running.End)))}
              />
            )}
            {laterToday && <StatusBadge status="assigned" label={t.card.laterToday(formatTime(parseApiDate(laterToday.Start)))} />}
          </div>
        </div>
        <Dialog.Close className="btn btn-text btn-icon -mr-2" aria-label={t.card.close}>
          <X className="h-5 w-5" aria-hidden />
        </Dialog.Close>
      </header>
      <Tabs.Root value={target.tab} onValueChange={v => actions.onTab(v as ResourceTab)} className="flex min-h-0 flex-1 flex-col">
        <Tabs.List aria-label={t.card.sections} className="flex shrink-0 gap-1 overflow-x-auto border-b border-line px-4">
          {TABS.map(tab => (
            <Tabs.Trigger
              key={tab.id}
              value={tab.id}
              className="relative inline-flex h-12 shrink-0 items-center gap-2 px-3 text-meta font-medium text-ink-2 hover:text-ink data-[state=active]:text-blue-ink data-[state=active]:after:absolute data-[state=active]:after:inset-x-2 data-[state=active]:after:bottom-0 data-[state=active]:after:h-0.5 data-[state=active]:after:rounded-full data-[state=active]:after:bg-blue data-[state=active]:after:content-['']"
            >
              <tab.icon className="h-4 w-4" aria-hidden />
              {tab.label}
            </Tabs.Trigger>
          ))}
        </Tabs.List>
        <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
          <Tabs.Content value="resumen" className="outline-none">
            <Summary
              kind={kind}
              driver={driver}
              vehicle={vehicle}
              own={own}
              trips={trips}
              retryTrips={retryTrips}
              now={now}
              master={master}
              version={version}
              onOpenTrip={actions.onOpenTrip}
              details={
                <DetailsSection
                  kind={kind}
                  resource={resource}
                  state={<ResourceState active={resource.IsActive} unavailable={unavailable} />}
                  bases={bases}
                  retryBases={retryBases}
                  baseName={baseName}
                  now={now}
                  version={version}
                  master={master}
                  onOpenTrip={actions.onOpenTrip}
                  onChanged={actions.onChanged}
                />
              }
            />
          </Tabs.Content>
          <Tabs.Content value="historial" className="outline-none">
            <HistoryTab kind={kind} id={id} version={version} master={master} onOpenTrip={actions.onOpenTrip} />
          </Tabs.Content>
          <Tabs.Content value="auditoria" className="outline-none">
            <AuditTab kind={kind} id={id} version={version} master={master} baseName={baseName} actions={actions} />
          </Tabs.Content>
          <Tabs.Content value="agenda" className="outline-none">
            <AgendaTab
              kind={kind}
              id={id}
              items={resource.Unavailabilities}
              now={now}
              version={version}
              master={master}
              onOpenTrip={actions.onOpenTrip}
              onChanged={actions.onChanged}
            />
          </Tabs.Content>
        </div>
      </Tabs.Root>
    </>
  );
}

function availabilityLook(active: boolean, unavailable: boolean): { classes: string; icon: LucideIcon; text: string } {
  if (!active) return { classes: 'bg-muted text-ink-3', icon: CircleSlash, text: t.common.inactive };
  if (unavailable) return { classes: 'bg-warning-soft text-warning-ink', icon: ShieldOff, text: t.common.notAvailable };
  return { classes: 'bg-success-soft text-success-ink', icon: CircleCheck, text: t.common.available };
}

/** Not a service status, so it is not a `StatusBadge`. */
export function ResourceState({ active, unavailable, external }: { active: boolean; unavailable: boolean; external?: boolean }) {
  const { classes, icon: Icon, text } = availabilityLook(active, unavailable);
  return (
    <span className="inline-flex flex-wrap gap-1.5">
      <span className={`inline-flex h-6 items-center gap-1 whitespace-nowrap rounded-full pl-1.5 pr-2 text-xs font-medium ${classes}`}>
        <Icon className="h-3.5 w-3.5" aria-hidden />
        {text}
      </span>
      {external && <span className="inline-flex h-6 items-center rounded-full bg-muted px-2 text-xs font-medium text-ink-2">{t.common.external}</span>}
    </span>
  );
}

function Summary({
  kind,
  driver,
  vehicle,
  own,
  trips,
  retryTrips,
  now,
  master,
  version,
  onOpenTrip,
  details,
}: {
  kind: ResourceKind;
  driver: Driver | null;
  vehicle: Vehicle | null;
  own: Trip[];
  trips: Loaded<Trip[]> | null;
  retryTrips: () => void;
  now: Date;
  master: MasterData;
  version: number;
  onOpenTrip: (bookingId: number, tripId: number) => void;
  /** The data of the driver or vehicle. */
  details: ReactNode;
}) {
  const [hours, retryHours] = useLoad(driver ? () => api.driverHours(driver.Id) : null, [driver?.Id, version]);
  const upcoming = upcomingTrips(own, now).slice(0, 5);
  const absences = (driver ?? vehicle)?.Unavailabilities ?? [];
  const conflictsOf = (trip: Trip): string[] => {
    const tooFewSeats = vehicle && shortOfSeatsText(trip.RequiredSeats, vehicle.SeatingCapacity);
    return [...overlappingUnavailabilities(trip, absences).map(unavailabilityText), ...(tooFewSeats ? [tooFewSeats] : [])];
  };
  const tripsBlock = (body: ReactNode) =>
    !trips ? (
      <SkeletonLines lines={3} label={t.card.loadingServices} />
    ) : !trips.ok ? (
      <ErrorState title={t.card.servicesFailed} onRetry={retryTrips}>
        {trips.error}
      </ErrorState>
    ) : (
      body
    );

  const next = (
    <Section title={t.card.nextServices} icon={CalendarClock}>
      {tripsBlock(
        upcoming.length === 0 ? (
          <p className="rounded-md bg-sunken px-3 py-3 text-meta text-ink-2">{t.card.noServices}</p>
        ) : (
          <ul className="divide-y divide-line overflow-hidden rounded-md bg-sunken">
            {upcoming.map(trip => (
              <li key={trip.Id}>
                <button
                  type="button"
                  className="flex w-full items-center justify-between gap-3 px-3 py-2.5 text-left hover:bg-muted"
                  onClick={() => onOpenTrip(trip.BookingId, trip.Id)}
                >
                  <span className="min-w-0">
                    <span className="block truncate text-meta font-medium text-ink">
                      {master.locName(trip.OriginLocationId)} → {master.locName(trip.DestinationLocationId)}
                    </span>
                    {trip.Description && <span className="block truncate text-xs text-ink-2">{trip.Description}</span>}
                    {conflictsOf(trip).map((text, index) => (
                      <span
                        key={index}
                        className="mt-1 flex items-start gap-1 rounded-md bg-warning-soft px-2 py-0.5 text-xs font-medium text-warning-ink"
                      >
                        <ShieldOff className="mt-0.5 h-3 w-3 shrink-0" aria-hidden />
                        {text}
                      </span>
                    ))}
                  </span>
                  <span className="flex shrink-0 items-center gap-2 text-xs font-medium text-ink-2">
                    {fmtWhen(trip.Start)}
                    <ArrowRight className="h-3.5 w-3.5" aria-hidden />
                  </span>
                </button>
              </li>
            ))}
          </ul>
        ),
      )}
    </Section>
  );

  if (kind === 'driver' && driver) {
    return (
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1.2fr)_minmax(0,1fr)]">
        <div className="space-y-6">
          <Section title={t.common.drivingHours} icon={Clock}>
            {!hours ? (
              <SkeletonLines lines={3} label={t.card.loadingHours} />
            ) : !hours.ok ? (
              <ErrorState title={t.card.hoursFailed} onRetry={retryHours}>
                {hours.error}
              </ErrorState>
            ) : (
              <HoursTiles hours={hours.value} />
            )}
          </Section>
          {details}
        </div>
        {next}
      </div>
    );
  }
  const shownVehicle = vehicle!;
  const pos = vehiclePosition(own, shownVehicle.HomeLocationId, now);
  return (
    <div className="grid gap-6 xl:grid-cols-[minmax(0,1.2fr)_minmax(0,1fr)]">
      <div className="space-y-6">
        {details}
        <Section title={t.card.currentPlace} icon={MapPin}>
          {tripsBlock(
            pos.locationId ? (
              <p className="rounded-md bg-sunken px-3 py-3 text-meta">
                <span className="font-semibold text-ink">{master.locName(pos.locationId)}</span>{' '}
                <span className="text-ink-2">
                  · {pos.source === 'LastTrip' ? t.card.lastDestination : t.card.itsBase}
                  {pos.since && t.card.arrived(fmtDate(pos.since))}
                </span>
              </p>
            ) : (
              <p className="rounded-md bg-sunken px-3 py-3 text-meta text-ink-2">{t.card.unknownPlace}</p>
            ),
          )}
          <p className="text-xs text-ink-3">{t.card.bySchedule}</p>
        </Section>
      </div>
      {next}
    </div>
  );
}

function HoursTiles({ hours }: { hours: DriverHours }) {
  return (
    <>
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {hourLimits(hours).map(limit => {
          const ratio = limit.max > 0 ? limit.value / limit.max : 0;
          return (
            <div key={limit.label} className="min-w-0 rounded-md bg-sunken p-3">
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
        <div className="min-w-0 rounded-md bg-sunken p-3">
          <div className="text-xs text-ink-2">{t.card.sinceRest}</div>
          <div className="text-meta">
            <b className="text-module font-bold text-ink">{formatDecimal(hours.SinceLastWeeklyRest)}</b>
            <span className="text-ink-2"> h</span>
          </div>
          <div className="mt-1.5 text-xs text-ink-3">{weeklyRestText(hours, fmtDate)}</div>
        </div>
      </div>
      <p className="mt-2 text-xs text-ink-3">{hoursNote()}</p>
    </>
  );
}

function HistoryTab({
  kind,
  id,
  version,
  master,
  onOpenTrip,
}: {
  kind: ResourceKind;
  id: number;
  version: number;
  master: MasterData;
  onOpenTrip: (bookingId: number, tripId: number) => void;
}) {
  const [page, setPage] = useState(1);
  const [last, setLast] = useState<ResourceHistory | null>(null); // The previous page stays visible while the next one loads.
  const [data, retry] = useLoad(() => api.history(kind, id, page, HISTORY_PAGE), [kind, id, page, version]);
  useEffect(() => {
    if (data?.ok) setLast(data.value);
  }, [data]);
  const history = data?.ok ? data.value : last;
  if (data && !data.ok && !history)
    return (
      <ErrorState title={t.card.historyFailed} onRetry={retry}>
        {data.error}
      </ErrorState>
    );
  if (!history) return <SkeletonLines lines={8} label={t.card.loadingHistory} />;

  const totals = history.Totals;
  const isDriver = kind === 'driver';
  const hours = history.Days.map(d => d.Hours);
  const marks = dayMarks(history.Days);
  const busy = !data;
  const plate = (vehicleId: number | null) =>
    vehicleId == null ? '—' : (master.vehicles.find(v => v.Id === vehicleId)?.LicensePlate ?? `#${vehicleId}`);
  const person = (driverId: number | null) => {
    const driver = master.drivers.find(x => x.Id === driverId);
    if (driver) return `${driver.FirstName} ${driver.LastName}`;
    return driverId == null ? '—' : `#${driverId}`;
  };

  return (
    <div className="space-y-6">
      {data && !data.ok && (
        <ErrorState title={t.card.pageFailed} onRetry={retry}>
          {data.error}
        </ErrorState>
      )}
      <HistoryTiles history={history} isDriver={isDriver} />

      <Section
        title={isDriver ? t.card.drivingPerDay : t.card.usePerDay}
        icon={CalendarRange}
        aside={
          isDriver && (
            <DriverLegend
              daily={history.DailyLimitHours}
              extended={history.ExtendedDailyLimitHours}
              perWeek={history.MaxExtendedDaysPerWeek}
            />
          )
        }
      >
        <DayBars
          values={hours}
          label={historyText(history, kind)}
          marks={marks}
          ceiling={chartCeiling(hours, isDriver ? history.ExtendedDailyLimitHours : 0)}
          tones={isDriver ? hours.map(v => barTone(v, history.DailyLimitHours, history.ExtendedDailyLimitHours)) : undefined}
          lines={
            isDriver
              ? [
                  { value: history.DailyLimitHours, text: formatHours(history.DailyLimitHours), tone: 'warning' },
                  { value: history.ExtendedDailyLimitHours, text: formatHours(history.ExtendedDailyLimitHours), tone: 'critical' },
                ]
              : []
          }
          titles={history.Days.map(
            day =>
              t.card.dayLabel(dayLabel(day), formatHours(day.Hours), day.Trips, isDriver ? '' : t.card.estimatedKm(formatKm(day.Km))),
          )}
        />
        <p className="text-xs text-ink-3">
          {t.card.calendarDays(isDriver ? t.card.informs : '')}
        </p>
      </Section>
      {!isDriver && (
        <Section title={t.card.kmPerDay} icon={Route}>
          {totals.Km > 0 ? (
            <DayBars
              values={history.Days.map(d => d.Km)}
              marks={marks}
              ceiling={chartCeiling(history.Days.map(d => d.Km))}
              label={t.card.kmChart(history.DaysCount, formatKm(totals.Km))}
              titles={history.Days.map(d => t.card.dayKm(dayLabel(d), formatKm(d.Km)))}
              height={120}
            />
          ) : (
            <EmptyState icon={Route} title={t.card.noKm}>
              {totals.Trips > 0
                ? t.card.roundTrips
                : t.card.noneDone}
            </EmptyState>
          )}
          <p className="text-xs text-ink-3">
            {t.card.kmBasis}
          </p>
        </Section>
      )}

      <Section
        title={t.card.done}
        icon={ListChecks}
        aside={<span className="text-xs font-normal text-ink-2">{rangeText(history.Page, history.PageSize, history.TotalCount)}</span>}
      >
        {history.TotalCount === 0 ? (
          <EmptyState icon={History} title={t.card.noneDoneIn(history.DaysCount)}>
            {t.card.willAppear}
          </EmptyState>
        ) : (
          <div className={`overflow-x-auto transition-opacity ${busy ? 'opacity-60' : ''}`} aria-busy={busy}>
            <table className="tbl min-w-[720px]">
              <caption className="sr-only">{t.card.doneCaption}</caption>
              <thead>
                <tr>
                  <th scope="col">{t.common.dayAndTime}</th>
                  <th scope="col">{t.common.service}</th>
                  <th scope="col">{t.common.duration}</th>
                  <th scope="col">{isDriver ? t.common.vehicle : t.common.driver}</th>
                  {!isDriver && <th scope="col">{t.card.estimatedKmTitle}</th>}
                  <th scope="col">
                    <span className="sr-only">{t.common.open}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {history.Items.map(trip => (
                  <HistoryRow
                    key={trip.TripId}
                    trip={trip}
                    route={`${master.locName(trip.OriginLocationId)} → ${master.locName(trip.DestinationLocationId)}`}
                    isDriver={isDriver}
                    counterpart={isDriver ? plate(trip.VehicleId) : person(trip.DriverId)}
                    onOpenTrip={onOpenTrip}
                  />
                ))}
              </tbody>
            </table>
          </div>
        )}
        {history.TotalPages > 1 && (
          <Pager page={page} pages={history.TotalPages} label={t.card.donePages} onPage={setPage} busy={busy} />
        )}
      </Section>
    </div>
  );
}

function HistoryTiles({ history, isDriver }: { history: ResourceHistory; isDriver: boolean }) {
  const totals = history.Totals;
  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4" role="group" aria-label={t.card.figuresOf(history.DaysCount)}>
      <Tile
        label={isDriver ? t.common.drivingHours : t.card.useHours}
        value={formatHours(totals.Hours)}
        foot={t.card.inDays(history.DaysCount)}
      />
      <Tile label={t.card.done} value={String(totals.Trips)} foot={t.card.daysWorked(totals.DaysWorked)} />
      {isDriver ? (
        <Tile
          label={t.card.daysOver(formatHours(history.DailyLimitHours))}
          value={String(totals.DaysOverDaily)}
          foot={
            totals.DaysOverExtended
              ? t.card.overLimit(totals.DaysOverExtended, formatHours(history.ExtendedDailyLimitHours))
              : t.card.noneOver(formatHours(history.ExtendedDailyLimitHours))
          }
          tone={totals.DaysOverExtended ? 'critical' : totals.DaysOverDaily ? 'warning' : undefined}
        />
      ) : (
        <Tile label={t.card.estimatedKmTitle} value={formatKm(totals.Km)} foot={t.card.straightLine} />
      )}
      {isDriver ? (
        <Tile
          label={t.card.average}
          value={totals.DaysWorked ? formatHours(totals.Hours / totals.DaysWorked) : '—'}
          foot={t.card.madridDays}
        />
      ) : (
        <Tile label={t.card.withoutKm} value={String(totals.TripsWithoutKm)} foot={t.card.sameEnds} />
      )}
    </div>
  );
}

function HistoryRow({
  trip,
  route,
  isDriver,
  counterpart,
  onOpenTrip,
}: {
  trip: HistoryTrip;
  route: string;
  isDriver: boolean;
  counterpart: string;
  onOpenTrip: (bookingId: number, tripId: number) => void;
}) {
  return (
    <tr>
      <td className="whitespace-nowrap text-meta text-ink-2">
        {fmtDay(trip.Start)} · {formatTime(parseApiDate(trip.Start))}–{formatTime(parseApiDate(trip.End))}
      </td>
      <td className="max-w-[360px]">
        <span className="block truncate text-meta font-medium text-ink">{route}</span>
        <span className="flex min-w-0 items-center gap-2">
          {trip.Description && (
            <span className="truncate text-xs text-ink-2" title={trip.Description}>
              {trip.Description}
            </span>
          )}
          {trip.ForcedDescription && <StatusBadge status="forced" title={trip.ForcedDescription} />}
        </span>
      </td>
      <td className="whitespace-nowrap text-meta font-medium text-ink">{formatHours(trip.Hours)}</td>
      <td className="max-w-[200px] truncate text-meta text-ink-2">{counterpart}</td>
      {!isDriver && (
        <td className="whitespace-nowrap text-meta">
          {trip.Km == null ? (
            <span className="text-ink-3" title={t.card.sameEndsHint}>
              {t.card.notEstimated}
            </span>
          ) : (
            formatKm(trip.Km)
          )}
        </td>
      )}
      <td className="text-right">
        <button type="button" className="btn btn-text btn-sm" onClick={() => onOpenTrip(trip.BookingId, trip.TripId)}>
          {t.common.booking}
          <ArrowRight className="h-4 w-4" aria-hidden />
          <span className="sr-only">
            {' '}{t.card.bookingOf(' ', fmtDay(trip.Start))}
          </span>
        </button>
      </td>
    </tr>
  );
}

function DriverLegend({ daily, extended, perWeek }: { daily: number; extended: number; perWeek: number }) {
  const items = [
    { cls: 'bg-blue', text: t.card.upTo(formatHours(daily)) },
    { cls: 'bg-warning', text: t.card.overExtended(formatHours(daily), perWeek) },
    { cls: 'bg-critical', text: t.card.over(formatHours(extended)) },
  ];
  return (
    <ul className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs font-normal text-ink-2" aria-label={t.common.legend}>
      {items.map(item => (
        <li key={item.text} className="inline-flex items-center gap-1.5">
          <span aria-hidden className={`h-3 w-3 rounded-[3px] ${item.cls}`} />
          {item.text}
        </li>
      ))}
    </ul>
  );
}

function Tile({ label, value, foot, tone }: { label: string; value: string; foot?: string; tone?: 'warning' | 'critical' }) {
  const cls = tone === 'critical' ? 'text-critical-ink' : tone === 'warning' ? 'text-warning-ink' : 'text-ink';
  return (
    <div className="min-w-0 rounded-md bg-sunken p-3">
      <p className="truncate text-xs text-ink-2" title={label}>
        {label}
      </p>
      <p className={`text-page font-bold ${cls}`}>{value}</p>
      {foot && (
        <p className="truncate text-xs text-ink-3" title={foot}>
          {foot}
        </p>
      )}
    </div>
  );
}

function Pager({
  page,
  pages,
  label,
  onPage,
  busy,
}: {
  page: number;
  pages: number;
  label: string;
  onPage: (page: number) => void;
  busy?: boolean;
}) {
  return (
    <nav className="flex flex-wrap items-center justify-between gap-3" aria-label={label}>
      <p className="text-meta text-ink-2" aria-live="polite">
        {t.common.page(page, pages)}
      </p>
      <div className="flex gap-2">
        <button type="button" className="btn btn-sm" disabled={busy || page <= 1} onClick={() => onPage(page - 1)}>
          <ChevronLeft className="h-4 w-4" aria-hidden />
          {t.common.previous}
        </button>
        <button type="button" className="btn btn-sm" disabled={busy || page >= pages} onClick={() => onPage(page + 1)}>
          {t.common.next}
          <ChevronRight className="h-4 w-4" aria-hidden />
        </button>
      </div>
    </nav>
  );
}

const RESULT_FILTER: { id: ActivityKind; label: string; count: (page: EngineAuditPage) => number }[] = [
  { id: 'All', get label() { return t.common.all; }, count: p => p.Counts.Validations },
  { id: 'Assigned', get label() { return t.card.accepted; }, count: p => p.Counts.Accepted },
  { id: 'Blocked', get label() { return t.card.blocked; }, count: p => p.Counts.Blocked },
  { id: 'Forced', get label() { return t.card.forced; }, count: p => p.Counts.Forced },
];

function AuditTab({
  kind,
  id,
  version,
  master,
  baseName,
  actions,
}: {
  kind: ResourceKind;
  id: number;
  version: number;
  master: MasterData;
  baseName: (id?: number) => string;
  actions: ResourceModalActions;
}) {
  const [result, setResult] = useState<ActivityKind>('All');
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<EngineAttempt | null>(null);
  const [returnTo, setReturnTo] = useState<number | null>(null);
  const now = useMemo(() => new Date(), [version]); // eslint-disable-line react-hooks/exhaustive-deps
  // The audit window is the history window; its number of days comes from the API (`ResourceHistory.Days`), not from the web.
  const [meta, retryMeta] = useLoad(() => api.history(kind, id, 1, 1), [kind, id, now]);
  const days = meta?.ok ? meta.value.DaysCount : null;
  const [data, retryAttempts] = useLoad(
    days == null ? null : () => engine.attempts(auditParams(kind, id, result, page, AUDIT_PAGE, days, now)),
    [kind, id, result, page, now, days],
  );
  const retry = meta && !meta.ok ? retryMeta : retryAttempts;
  const failed = meta && !meta.ok ? meta : data && !data.ok ? data : null;
  const [last, setLast] = useState<EngineAuditPage | null>(null);
  useEffect(() => {
    if (data?.ok) setLast(data.value);
  }, [data]);
  const [rules] = useLoad<RuleGroup[]>(api.rules, []);
  const index = useMemo(() => ruleIndex(rules?.ok ? rules.value : []), [rules]);
  const listRef = useRef<HTMLDivElement>(null);
  const backRef = useRef<HTMLButtonElement>(null);
  // Focus goes to the "back" button on opening an attempt, and to the attempt that was opened on returning.
  useLayoutEffect(() => {
    if (open) backRef.current?.focus();
    else if (returnTo != null) listRef.current?.querySelector<HTMLButtonElement>(`[data-attempt="${returnTo}"]`)?.focus();
  }, [open, returnTo]);

  const driverName = (driverId: number) => {
    const driver = master.drivers.find(x => x.Id === driverId);
    return driver ? `${driver.FirstName} ${driver.LastName}` : t.common.driverOf(driverId);
  };
  const plate = (vehicleId: number) => master.vehicles.find(v => v.Id === vehicleId)?.LicensePlate ?? t.common.vehicleOf(vehicleId);

  if (open) {
    return (
      <div className="space-y-4">
        <button
          ref={backRef}
          type="button"
          className="btn btn-text -ml-2"
          onClick={() => {
            setReturnTo(open.Id);
            setOpen(null);
          }}
        >
          <ArrowLeft className="h-4 w-4" aria-hidden />
          {t.card.backToAudit}
        </button>
        <h3 className="text-module font-semibold text-ink">{t.common.attemptDetail}</h3>
        <AttemptDetail
          attempt={open}
          index={index}
          driverName={driverName}
          plate={plate}
          baseName={b => baseName(b)}
          onOpenTrip={actions.onOpenTripId}
          onCatalog={actions.onCatalog}
        />
      </div>
    );
  }

  const auditPage = data?.ok ? data.value : last;
  const busy = !data && auditPage != null;
  return (
    <div className="space-y-4" ref={listRef}>
      <div className="flex flex-wrap items-center gap-3">
        <div className="seg" role="group" aria-label={t.common.result}>
          {RESULT_FILTER.map(option => (
            <button
              key={option.id}
              type="button"
              aria-pressed={result === option.id}
              onClick={() => {
                setResult(option.id);
                setPage(1);
              }}
            >
              {option.label}
              {auditPage && <span className="ml-1.5 text-ink-3">{option.count(auditPage)}</span>}
            </button>
          ))}
        </div>
        <p className="text-meta text-ink-2">
          {t.card.attemptsOf(days ?? '…', kind === 'driver' ? t.card.thisDriver : t.card.thisVehicle)}
        </p>
      </div>
      {failed && !failed.ok && (
        <ErrorState title={t.card.auditFailed} onRetry={retry}>
          {failed.error}
        </ErrorState>
      )}
      {!auditPage && !failed && <SkeletonLines lines={6} label={t.card.loadingAudit} />}
      {auditPage && auditPage.Items.length === 0 && (
        <EmptyState
          icon={ListChecks}
          title={result === 'All' ? t.card.noAttempts(days ?? '…') : t.card.noAttemptsLike}
        >
          {result === 'All' ? (
            t.card.attemptsWillAppear
          ) : (
            <button
              type="button"
              className="btn btn-link mt-1"
              onClick={() => {
                setResult('All');
                setPage(1);
              }}
            >
              {t.common.seeAll}
            </button>
          )}
        </EmptyState>
      )}
      {auditPage && auditPage.Items.length > 0 && (
        <>
          <ul
            className={`divide-y divide-line overflow-hidden rounded-md border border-line transition-opacity ${busy ? 'opacity-60' : ''}`}
            aria-busy={busy}
            aria-label={t.common.attempts}
          >
            {auditPage.Items.map(attempt => {
              const names = attempt.RuleIds.map(r => index.get(r)?.rule.Name ?? r);
              const other = kind === 'driver' ? plate(attempt.VehicleId) : driverName(attempt.DriverId);
              return (
                <li key={attempt.Id}>
                  <button
                    type="button"
                    data-attempt={attempt.Id}
                    aria-haspopup="dialog"
                    onClick={() => setOpen(attempt)}
                    className="grid w-full grid-cols-[132px_minmax(0,200px)_minmax(0,1fr)_minmax(0,0.8fr)] items-center gap-3 px-3 py-2.5 text-left hover:bg-sunken"
                  >
                    <time className="text-meta text-ink-2" dateTime={parseApiDate(attempt.At).toISOString()}>
                      {fmtWhen(attempt.At)}
                    </time>
                    <span className="flex flex-wrap gap-1">
                      <OutcomeBadge outcome={attempt.Outcome} />
                      <OriginBadge origin={attempt.Origin} />
                    </span>
                    <span className="min-w-0">
                      <span className="block truncate text-meta font-medium text-ink">
                        {attempt.TripLabel?.trim() || t.common.serviceOf(attempt.TripId)}
                      </span>
                      <span className="block truncate text-xs text-ink-2">
                        {t.card.withOther(kind === 'driver' ? t.card.withVehicle : t.card.drivenBy, other, whoText(attempt.Who))}
                      </span>
                    </span>
                    <span className="truncate text-meta text-ink-2" title={names.join(', ')}>
                      {names.length ? names.join(', ') : attempt.Outcome === 'Assigned' ? t.common.allMet : t.common.notRecorded}
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-meta text-ink-2">{rangeText(auditPage.Page, auditPage.PageSize, auditPage.TotalCount)}</p>
            {auditPage.TotalPages > 1 && (
              <Pager page={page} pages={auditPage.TotalPages} label={t.card.auditPages} onPage={setPage} busy={busy} />
            )}
          </div>
        </>
      )}
    </div>
  );
}
