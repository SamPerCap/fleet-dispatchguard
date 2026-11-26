import { t } from '../i18n/t';
import {
  ArrowRight,
  ChevronLeft,
  ChevronRight,
  CircleCheck,
  FilterX,
  ListChecks,
  MapPin,
  Scale,
  ShieldAlert,
  ShieldX,
  type LucideIcon,
} from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactElement } from 'react';
import type { MasterData } from '../App';
import { api, control, engine, type Scope } from '../api';
import { GapLines } from '../charts';
import { merge, type Fresh } from '../control';
import {
  addsUp,
  auditableRules,
  axisMarks,
  defaultFilters,
  engineParams,
  hourlySeries,
  hourlyText,
  KPIS,
  madridDate,
  narrowed,
  originText,
  PAGE_SIZE,
  periodText,
  rangeText,
  ruleIndex,
  type EngineFilters,
  type PeriodDays,
} from '../engine';
import { ExplanationBody } from '../ExplanationDialog';
import { formatDayTime, formatTime, parseApiDate, TIME_ZONE } from '../gantt';
import { OriginBadge, OutcomeBadge, Row } from '../home/Activity';
import { settle, staleOf, type Loaded } from '../home/data';
import { Freshness } from '../home/Situation';
import { createLatestGuard } from '../latest';
import { readScope, writeScope } from '../storage';
import type { ActivityKind, EngineAttempt, EngineAttemptDetail, EngineAuditPage, EngineHourly, OperatingBase, RuleGroup } from '../types';
import {
  AnimatedNumber,
  EmptyState,
  ErrorState,
  Hint,
  IconBadge,
  Module,
  PageHeader,
  Sheet,
  Skeleton,
  SkeletonLines,
  StaleNote,
  Toolbar,
  type IconTone,
} from '../ui';
import { useRefresh } from '../useRefresh';
import { GROUP_ICON, RULE_KIND } from './RulesPage';
import { locale } from '../language';
import { whoText } from '../people';

const KPI_LOOK: Record<ActivityKind, { icon: LucideIcon; tone: IconTone; hint: string }> = {
  All: {
    icon: Scale,
    tone: 'blue',
    get hint() { return t.engine.periodWhat; },
  },
  Assigned: { icon: CircleCheck, tone: 'success', get hint() { return t.engine.acceptedWhat; } },
  Blocked: {
    icon: ShieldX,
    tone: 'critical',
    get hint() { return t.engine.blockedPeriodWhat; },
  },
  Forced: {
    icon: ShieldAlert,
    tone: 'warning',
    get hint() { return t.engine.forcedPeriodWhat; },
  },
};
const KIND_FILTER: { id: ActivityKind; label: string }[] = [
  { id: 'All', get label() { return t.common.all; } },
  { id: 'Assigned', get label() { return t.card.accepted; } },
  { id: 'Blocked', get label() { return t.card.blocked; } },
  { id: 'Forced', get label() { return t.card.forced; } },
];

/** KPIs, curve and rows come from the same filter of the audit log, so every figure matches the rows. */
export function EnginePage({
  master,
  version,
  onOpenTrip,
  onCatalog,
}: {
  master: MasterData;
  version: number;
  onOpenTrip: (tripId: number) => void;
  onCatalog: (groupKey?: string) => void;
}) {
  const [bases, setBases] = useState<OperatingBase[]>([]);
  const [basesError, setBasesError] = useState<string | null>(null);
  const [filters, setFilters] = useState<EngineFilters>(() => defaultFilters(new Date(), readScope()));
  const [page, setPage] = useState(1);
  // Kept with its query: if loading another filter fails, rows of a different filter are not shown as stale.
  const [rows, setRows] = useState<{ key: string; data: Fresh<EngineAuditPage> }>();
  const [hourly, setHourly] = useState<Fresh<EngineHourly>>();
  const [rules, setRules] = useState<Fresh<RuleGroup[]>>();
  const [updatedAt, setUpdatedAt] = useState<Date | null>(null);
  const [loading, setLoading] = useState(false);
  const [clock, setClock] = useState(() => new Date());
  const [open, setOpen] = useState<EngineAttempt | null>(null);
  const guard = useRef(createLatestGuard()).current;
  const register = useRef<HTMLDivElement>(null);

  useEffect(() => {
    control
      .bases()
      .then(list => {
        setBases(list);
        setBasesError(null);
        setFilters(current => (current.scope === 'all' || list.some(b => b.Id === current.scope) ? current : { ...current, scope: 'all' }));
      })
      .catch(e => setBasesError((e as Error).message));
  }, []);

  // Everything but the outcome and the page: if it changes, it is other data (skeletons); if not, what is shown stays while loading.
  const scopeKey = engineParams(filters, {}, false).toString();
  const rowsKey = engineParams(filters, { page, pageSize: PAGE_SIZE }).toString();
  useEffect(() => {
    setRows(undefined);
    setHourly(undefined);
    setUpdatedAt(null);
  }, [scopeKey]);

  const load = useCallback(async () => {
    const current = guard.begin();
    setLoading(true);
    const at = new Date();
    const results = await Promise.allSettled([
      engine.attempts(new URLSearchParams(rowsKey)),
      engine.hourly(new URLSearchParams(scopeKey)),
      api.rules(),
    ]);
    if (!current()) return;
    // What fails keeps its last good value, marked as stale.
    setRows(prev => ({ key: rowsKey, data: merge(prev?.key === rowsKey ? prev.data : undefined, settle(results[0]), at) }));
    setHourly(prev => merge(prev, settle(results[1]), at));
    setRules(prev => merge(prev, settle(results[2]), at));
    if (results.every(x => x.status === 'fulfilled')) setUpdatedAt(at);
    setClock(at);
    setLoading(false);
  }, [guard, rowsKey, scopeKey]);

  useEffect(() => {
    void load();
  }, [load, version]);
  const paused = useRefresh(() => void load());
  useEffect(() => {
    const timer = setInterval(() => setClock(new Date()), 30_000);
    return () => clearInterval(timer);
  }, []);

  const audit = rows?.data;
  // If a refresh brings fewer rows and the page no longer exists, go to the last one.
  const totalPages = audit?.ok ? audit.value.TotalPages : null;
  useEffect(() => {
    if (totalPages != null && totalPages > 0 && page > totalPages) setPage(totalPages);
  }, [page, totalPages]);

  const change = (patch: Partial<EngineFilters>) => {
    setFilters(f => ({ ...f, ...patch }));
    setPage(1);
  };
  const chooseScope = (nextScope: Scope) => {
    writeScope(nextScope);
    change({ scope: nextScope });
  };
  const chooseKind = (kind: ActivityKind) => change({ kind });
  const clear = () => change({ rule: null, driverId: null, vehicleId: null, by: null, origin: null, kind: 'All' });

  const driverName = useCallback(
    (id: number) => {
      const driver = master.drivers.find(x => x.Id === id);
      return driver ? `${driver.FirstName} ${driver.LastName}` : t.common.driverOf(id);
    },
    [master.drivers],
  );
  const plate = useCallback((id: number) => master.vehicles.find(v => v.Id === id)?.LicensePlate ?? t.common.vehicleOf(id), [master.vehicles]);
  const baseName = useCallback((id: number) => bases.find(b => b.Id === id)?.Name ?? t.common.baseId(id), [bases]);
  const scopeName = filters.scope === 'all' ? t.common.allBasesLower : baseName(filters.scope);
  const groups = rules?.ok ? rules.value : null;
  const index = useMemo(() => ruleIndex(groups ?? []), [groups]);
  const auditPage = audit?.ok ? audit.value : null;
  const period = auditPage ? periodText(parseApiDate(auditPage.From), parseApiDate(auditPage.To), filters.days) : null;
  const today = madridDate(clock);
  const driverOptions = master.drivers.map(d => ({ id: d.Id, name: `${d.FirstName} ${d.LastName}` })).sort(byName);
  const vehicleOptions = master.vehicles.map(v => ({ id: v.Id, name: v.LicensePlate })).sort(byName);

  return (
    <div className="page space-y-6">
      <PageHeader
        title={t.engine.title}
        icon={Scale}
        subtitle={
          <>{t.engine.intro}</>
        }
      />
      <Toolbar label={t.engine.scopeAndPeriod}>
        <label className="flex items-center gap-2">
          <MapPin className="h-4 w-4 text-ink-3" aria-hidden />
          <span className="text-meta font-medium text-ink-2">{t.common.base}</span>
          <select
            className="input min-w-[200px] font-medium"
            value={String(filters.scope)}
            onChange={e => chooseScope(e.target.value === 'all' ? 'all' : Number(e.target.value))}
          >
            <option value="all">{t.common.allBasesCount(bases.length ? ` (${bases.length})` : '')}</option>
            {bases.map(base => (
              <option key={base.Id} value={base.Id}>
                {base.Name}
              </option>
            ))}
          </select>
        </label>
        <div className="seg" role="group" aria-label={t.engine.periodLabel}>
          {([1, 7] as PeriodDays[]).map(days => (
            <button key={days} type="button" aria-pressed={filters.days === days} onClick={() => change({ days })}>
              {days === 1 ? '24 h' : '7 d'}
              <span className="sr-only">{days === 1 ? t.engine.chosenDay : t.engine.chosenWeek}</span>
            </button>
          ))}
        </div>
        <input
          type="date"
          className="input"
          aria-label={filters.days === 1 ? t.common.day : t.engine.lastDay}
          value={filters.date}
          max={today}
          onChange={event => {
            if (event.target.value) change({ date: event.target.value });
          }}
        />
        <span className="flex-1" />
        <Freshness updatedAt={updatedAt} loading={loading} paused={paused} onRefresh={() => void load()} />
      </Toolbar>
      {basesError && (
        <p role="alert" className="-mt-4 text-meta text-critical-ink">
          {t.common.basesFailed(basesError)}
        </p>
      )}

      <Kpis
        audit={audit}
        now={clock}
        kind={filters.kind}
        period={period}
        scopeName={scopeName}
        onKind={nextKind => {
          chooseKind(nextKind);
          register.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }}
        onRetry={() => void load()}
      />

      <div className="grid gap-6 xl:grid-cols-12">
        <CurveModule
          className="xl:col-span-8 3xl:col-span-9"
          hourly={hourly}
          days={filters.days}
          now={clock}
          period={period}
          filtered={narrowed(filters)}
          onRetry={() => void load()}
        />
        <CategoriesModule
          className="xl:col-span-4 3xl:col-span-3"
          rules={rules}
          now={clock}
          onCatalog={onCatalog}
          onRetry={() => void load()}
        />
      </div>

      <div ref={register} className="scroll-mt-6">
        <RegisterModule
          audit={audit}
          now={clock}
          filters={filters}
          page={page}
          loading={loading && rows?.key !== rowsKey}
          groups={groups}
          index={index}
          drivers={driverOptions}
          vehicles={vehicleOptions}
          driverName={driverName}
          plate={plate}
          onChange={change}
          onKind={chooseKind}
          onClear={clear}
          onPage={setPage}
          onOpen={setOpen}
          onRetry={() => void load()}
        />
      </div>

      <AttemptSheet
        attempt={open}
        onClose={() => setOpen(null)}
        index={index}
        driverName={driverName}
        plate={plate}
        baseName={baseName}
        onOpenTrip={id => {
          setOpen(null);
          onOpenTrip(id);
        }}
        onCatalog={key => {
          setOpen(null);
          onCatalog(key);
        }}
      />
    </div>
  );
}

function Kpis({
  audit,
  now,
  kind,
  period,
  scopeName,
  onKind,
  onRetry,
}: {
  audit?: Fresh<EngineAuditPage>;
  now: Date;
  kind: ActivityKind;
  period: string | null;
  scopeName: string;
  onKind: (kind: ActivityKind) => void;
  onRetry: () => void;
}) {
  const counts = audit?.ok ? audit.value.Counts : null;
  const stale = staleOf(audit, now);
  if (audit && !audit.ok)
    return (
      <div className="card p-6">
        <ErrorState title={t.engine.figuresFailed} onRetry={onRetry}>
          {audit.error}
        </ErrorState>
      </div>
    );
  return (
    <section aria-label={t.engine.figures}>
      <div className="mb-3 flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <h2 className="module-title">{t.engine.validations}</h2>
        <div className="text-meta text-ink-2">
          {period ? (
            <>
              {period} · {scopeName}
            </>
          ) : (
            <Skeleton className="inline-block h-3 w-48 align-middle" />
          )}
        </div>
      </div>
      {stale && <StaleNote text={stale} />}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4" role="group" aria-label={t.engine.filterByResult}>
        {KPIS.map(kpi => {
          const look = KPI_LOOK[kpi.kind];
          const pressed = kind === kpi.kind;
          return (
            <div key={kpi.kind} className={`card lift relative flex min-w-0 items-center gap-4 p-5 ${pressed ? 'ring-2 ring-blue' : ''}`}>
              <IconBadge icon={look.icon} tone={look.tone} size="lg" />
              <div className="min-w-0 flex-1">
                {counts ? (
                  <p className="text-display font-bold text-ink">
                    <AnimatedNumber value={kpi.value(counts)} />
                  </p>
                ) : (
                  <Skeleton className="h-10 w-16" />
                )}
                <p className="mt-1 flex items-center gap-1.5 text-meta font-semibold text-ink">
                  {/* The button covers the card; the ⓘ stays above it so that it can be opened. */}
                  <button
                    type="button"
                    aria-pressed={pressed}
                    onClick={() => onKind(kpi.kind)}
                    disabled={!counts}
                    className="text-left after:absolute after:inset-0 after:rounded-lg after:content-[''] focus-visible:outline-none focus-visible:after:ring-2 focus-visible:after:ring-blue"
                  >
                    {kpi.label}
                    <span className="sr-only">
                      {counts ? `: ${kpi.value(counts)}. ` : '. '}
                      {pressed ? t.engine.filtering : t.engine.filterRegister}
                    </span>
                  </button>
                  <span className="relative z-10">
                    <Hint label={kpi.label}>{look.hint}</Hint>
                  </span>
                </p>
                <p className="mt-0.5 truncate text-xs text-ink-2">{pressed ? t.engine.filtersBelow : t.engine.seeInRegister}</p>
              </div>
            </div>
          );
        })}
      </div>
      {counts && (
        <p className={`mt-2 text-xs ${addsUp(counts) ? 'text-ink-2' : 'font-medium text-critical-ink'}`}>
          {t.engine.sumValidations(counts.Validations, counts.Accepted, counts.Blocked, counts.Forced, addsUp(counts) ? '' : t.engine.mismatch)}
        </p>
      )}
    </section>
  );
}

function CurveModule({
  hourly,
  days,
  now,
  period,
  filtered,
  onRetry,
  className,
}: {
  hourly?: Fresh<EngineHourly>;
  days: PeriodDays;
  now: Date;
  period: string | null;
  filtered: boolean;
  onRetry: () => void;
  className?: string;
}) {
  const data = hourly?.ok ? hourly.value : null;
  const series = data ? hourlySeries(data.Hours, now) : null;
  const sum = (field: 'Accepted' | 'Blocked' | 'Forced') => data?.Hours.reduce((total, hour) => total + hour[field], 0) ?? 0;
  const legend = [
    { name: t.engine.accepted, tone: 'success' as const, dot: 'bg-success', total: sum('Accepted') },
    { name: t.engine.blocked, tone: 'critical' as const, dot: 'bg-critical', total: sum('Blocked') },
    { name: t.engine.forced, tone: 'warning' as const, dot: 'bg-warning', total: sum('Forced') },
  ];
  return (
    <Module
      title={t.engine.perHour}
      icon={ListChecks}
      className={className}
      stale={staleOf(hourly, now)}
      sub={<span className="text-meta text-ink-2">{period ?? ''}</span>}
    >
      {hourly && !hourly.ok && (
        <ErrorState title={t.engine.curveFailed} onRetry={onRetry}>
          {hourly.error}
        </ErrorState>
      )}
      {!hourly && <Skeleton className="h-[200px] w-full" />}
      {data &&
        series &&
        (series.withData === 0 ? (
          <EmptyState icon={ListChecks} title={filtered ? t.engine.noneFiltered : t.engine.noneInPeriod}>
            {filtered
              ? t.engine.sameFilters
              : t.engine.willAppearByHour}
          </EmptyState>
        ) : (
          <figure>
            <ul className="mb-3 flex flex-wrap gap-x-5 gap-y-1 text-meta text-ink-2" aria-label={t.common.legend}>
              {legend.map(entry => (
                <li key={entry.name} className="flex items-center gap-2">
                  <span aria-hidden className={`h-2.5 w-2.5 rounded-full ${entry.dot}`} />
                  {entry.name}
                  <span className="font-semibold text-ink">{entry.total}</span>
                </li>
              ))}
              <li className="flex items-center gap-1.5 text-xs text-ink-3">
                {t.engine.noLine}
                <Hint label={t.engine.gaps}>
                  {t.engine.gapsWhat}
                </Hint>
              </li>
            </ul>
            <GapLines
              label={hourlyText(data.Hours, days)}
              marks={axisMarks(data.Hours, days)}
              height={200}
              series={legend.map((entry, index) => ({
                name: entry.name,
                tone: entry.tone,
                values: [series.accepted, series.blocked, series.forced][index],
              }))}
            />
          </figure>
        ))}
    </Module>
  );
}

function CategoriesModule({
  rules,
  now,
  onCatalog,
  onRetry,
  className,
}: {
  rules?: Fresh<RuleGroup[]>;
  now: Date;
  onCatalog: (groupKey?: string) => void;
  onRetry: () => void;
  className?: string;
}) {
  const groups = rules?.ok ? rules.value : null;
  const total = groups?.reduce((sum, group) => sum + group.Rules.length, 0) ?? 0;
  return (
    <Module
      title={t.engine.rulesTitle}
      icon={Scale}
      className={className}
      stale={staleOf(rules, now)}
      sub={groups && <span className="text-meta text-ink-2">{t.engine.rulesCount(total)}</span>}
      actions={
        <button type="button" className="btn btn-link" onClick={() => onCatalog()}>
          {t.engine.catalogue}
          <ArrowRight className="h-4 w-4" aria-hidden />
        </button>
      }
    >
      {rules && !rules.ok && (
        <ErrorState title={t.engine.rulesFailed} onRetry={onRetry}>
          {rules.error}
        </ErrorState>
      )}
      {!rules && <SkeletonLines lines={6} label={t.engine.loadingRules} />}
      {groups?.length === 0 && <EmptyState icon={Scale} title={t.common.noRules} />}
      {groups && groups.length > 0 && (
        <ul className="-mx-2 divide-y divide-line">
          {groups.map(group => {
            const look = GROUP_ICON[group.Key] ?? { icon: Scale, tone: 'neutral' as IconTone };
            const kinds = (['Blocking', 'Warning', 'Ranking', 'Informative'] as const)
              .map(kind => ({ kind, count: group.Rules.filter(r => r.Kind === kind).length }))
              .filter(x => x.count > 0);
            return (
              <li key={group.Key}>
                <button
                  type="button"
                  onClick={() => onCatalog(group.Key)}
                  aria-label={t.engine.groupRules(group.Title, group.Rules.length)}
                  className="group flex w-full items-center gap-3 rounded-md px-2 py-3 text-left hover:bg-sunken"
                >
                  <IconBadge icon={look.icon} tone={look.tone} size="sm" />
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-meta font-semibold text-ink">{group.Title}</span>
                    <span className="block truncate text-xs text-ink-2">
                      {kinds.map(x => `${x.count} ${RULE_KIND[x.kind].text.toLowerCase()}`).join(' · ')}
                    </span>
                  </span>
                  <span className="text-data font-bold text-ink">{group.Rules.length}</span>
                  <ChevronRight className="h-4 w-4 shrink-0 text-ink-3 group-hover:text-ink" aria-hidden />
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </Module>
  );
}

type Option = { id: number; name: string };
const byName = (a: Option, b: Option) => a.name.localeCompare(b.name, 'es');
type RuleInfo = ReturnType<typeof ruleIndex>;

function RegisterModule({
  audit,
  now,
  filters,
  page,
  loading,
  groups,
  index,
  drivers,
  vehicles,
  driverName,
  plate,
  onChange,
  onKind,
  onClear,
  onPage,
  onOpen,
  onRetry,
}: {
  audit?: Fresh<EngineAuditPage>;
  now: Date;
  filters: EngineFilters;
  page: number;
  loading: boolean;
  groups: RuleGroup[] | null;
  index: RuleInfo;
  drivers: Option[];
  vehicles: Option[];
  driverName: (id: number) => string;
  plate: (id: number) => string;
  onChange: (patch: Partial<EngineFilters>) => void;
  onKind: (kind: ActivityKind) => void;
  onClear: () => void;
  onPage: (page: number) => void;
  onOpen: (attempt: EngineAttempt) => void;
  onRetry: () => void;
}) {
  const auditPage = audit?.ok ? audit.value : null;
  const ruleOptions = auditPage && groups ? auditableRules(groups, auditPage.Rules) : [];
  const chosenPerson = filters.by ? [filters.by] : [];
  const people = [...new Set([...(auditPage?.People ?? []), ...chosenPerson])];
  const busy = loading && auditPage != null;
  const anyFilter = filters.kind !== 'All' || filters.rule != null || narrowed(filters);
  const showDay = filters.days === 7;

  return (
    <Module
      title={t.engine.register}
      icon={ListChecks}
      stale={staleOf(audit, now)}
      sub={auditPage && <span className="text-meta text-ink-2">{rangeText(auditPage.Page, auditPage.PageSize, auditPage.TotalCount)}</span>}
    >
      <RegisterFilters
        filters={filters}
        anyFilter={anyFilter}
        ruleOptions={ruleOptions}
        index={index}
        drivers={drivers}
        vehicles={vehicles}
        people={people}
        onChange={onChange}
        onKind={onKind}
        onClear={onClear}
      />

      {audit && !audit.ok && (
        <ErrorState title={t.engine.registerFailed} onRetry={onRetry}>
          {audit.error}
        </ErrorState>
      )}
      {!audit && <SkeletonLines lines={8} label={t.engine.loadingRegister} />}
      {auditPage && auditPage.Items.length === 0 && (
        <EmptyState icon={ListChecks} title={anyFilter ? t.engine.noneWithFilters : t.engine.noAttempts}>
          {anyFilter ? (
            <button type="button" className="btn btn-link mt-1" onClick={onClear}>
              {t.engine.removeFilters}
            </button>
          ) : (
            t.engine.willAppear
          )}
        </EmptyState>
      )}
      {auditPage && auditPage.Items.length > 0 && (
        <>
          <div className={`-mx-2 overflow-x-auto transition-opacity ${busy ? 'opacity-60' : ''}`} aria-busy={busy}>
            <table className="w-full min-w-[600px] table-fixed border-separate border-spacing-0 text-left text-data">
              <caption className="sr-only">
                {t.engine.caption}
              </caption>
              <thead>
                <tr className="text-xs text-ink-3">
                  <th scope="col" className={`border-b border-line px-2 pb-2 font-medium ${showDay ? 'w-28' : 'w-20'}`}>
                    {showDay ? t.common.dayAndTime : t.common.time}
                  </th>
                  <th scope="col" className="w-[220px] border-b border-line px-2 pb-2 font-medium">
                    {t.common.result}
                  </th>
                  <th scope="col" className="border-b border-line px-2 pb-2 font-medium">
                    {t.engine.serviceAndResources}
                  </th>
                  <th scope="col" className="w-[24%] border-b border-line px-2 pb-2 font-medium">
                    {t.engine.rule}
                  </th>
                  <th scope="col" className="hidden w-[14%] border-b border-line px-2 pb-2 font-medium xl:table-cell">
                    {t.common.who}
                  </th>
                </tr>
              </thead>
              <tbody>
                {auditPage.Items.map(attempt => (
                  <AttemptRow
                    key={attempt.Id}
                    attempt={attempt}
                    index={index}
                    showDay={showDay}
                    driverName={driverName}
                    plate={plate}
                    onOpen={onOpen}
                  />
                ))}
              </tbody>
            </table>
          </div>
          <nav className="mt-4 flex flex-wrap items-center justify-between gap-3" aria-label={t.engine.pages}>
            <p className="text-meta text-ink-2" aria-live="polite">
              {t.engine.rangeAndPage(rangeText(auditPage.Page, auditPage.PageSize, auditPage.TotalCount), auditPage.Page, Math.max(1, auditPage.TotalPages))}
            </p>
            <div className="flex gap-2">
              <button type="button" className="btn btn-sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>
                <ChevronLeft className="h-4 w-4" aria-hidden />
                {t.common.previous}
              </button>
              <button type="button" className="btn btn-sm" disabled={page >= auditPage.TotalPages} onClick={() => onPage(page + 1)}>
                {t.common.next}
                <ChevronRight className="h-4 w-4" aria-hidden />
              </button>
            </div>
          </nav>
        </>
      )}
    </Module>
  );
}

const idOf = (value: string) => (value === '' ? null : Number(value));

function RegisterFilters({
  filters,
  anyFilter,
  ruleOptions,
  index,
  drivers,
  vehicles,
  people,
  onChange,
  onKind,
  onClear,
}: {
  filters: EngineFilters;
  anyFilter: boolean;
  ruleOptions: RuleGroup[];
  index: RuleInfo;
  drivers: Option[];
  vehicles: Option[];
  people: string[];
  onChange: (patch: Partial<EngineFilters>) => void;
  onKind: (kind: ActivityKind) => void;
  onClear: () => void;
}) {
  return (
    <div className="mb-4 flex flex-wrap items-end gap-3" role="group" aria-label={t.engine.filters}>
      <div>
        <span className="mb-1 block text-xs font-medium text-ink-2" id="kind-label">
          {t.common.result}
        </span>
        <div className="seg" role="group" aria-labelledby="kind-label">
          {KIND_FILTER.map(option => (
            <button key={option.id} type="button" aria-pressed={filters.kind === option.id} onClick={() => onKind(option.id)}>
              {option.label}
            </button>
          ))}
        </div>
      </div>
      <Field label={t.engine.rule}>
        <select className="input w-44" value={filters.rule ?? ''} onChange={e => onChange({ rule: e.target.value || null })}>
          <option value="">{t.engine.allRules}</option>
          {ruleOptions.map(group => (
            <optgroup key={group.Key} label={group.Title}>
              {group.Rules.map(rule => (
                <option key={rule.Id} value={rule.Id}>
                  {rule.Name}
                </option>
              ))}
            </optgroup>
          ))}
          {filters.rule && !ruleOptions.some(group => group.Rules.some(r => r.Id === filters.rule)) && (
            <option value={filters.rule}>{index.get(filters.rule)?.rule.Name ?? filters.rule}</option>
          )}
        </select>
      </Field>
      <Field label={t.common.driver}>
        <select className="input w-44" value={filters.driverId ?? ''} onChange={e => onChange({ driverId: idOf(e.target.value) })}>
          <option value="">{t.common.all}</option>
          {drivers.map(driver => (
            <option key={driver.id} value={driver.id}>
              {driver.name}
            </option>
          ))}
        </select>
      </Field>
      <Field label={t.common.vehicle}>
        <select className="input w-36" value={filters.vehicleId ?? ''} onChange={e => onChange({ vehicleId: idOf(e.target.value) })}>
          <option value="">{t.common.all}</option>
          {vehicles.map(vehicle => (
            <option key={vehicle.id} value={vehicle.id}>
              {vehicle.name}
            </option>
          ))}
        </select>
      </Field>
      <Field label={t.engine.whoAssigned}>
        <select className="input w-36" value={filters.by ?? ''} onChange={e => onChange({ by: e.target.value || null })}>
          <option value="">{t.engine.anyone}</option>
          {people.map(person => (
            <option key={person} value={person}>
              {whoText(person)}
            </option>
          ))}
        </select>
      </Field>
      <Field label={t.engine.origin}>
        <select
          className="input w-36"
          value={filters.origin ?? ''}
          onChange={event => onChange({ origin: (event.target.value || null) as EngineFilters['origin'] })}
        >
          <option value="">{t.common.all}</option>
          <option value="Recommended">{originText('Recommended')}</option>
          <option value="Manual">{originText('Manual')}</option>
        </select>
      </Field>
      {anyFilter && (
        <button type="button" className="btn btn-text" onClick={onClear}>
          <FilterX className="h-4 w-4" aria-hidden />
          {t.engine.removeFilters}
        </button>
      )}
    </div>
  );
}

function AttemptRow({
  attempt,
  index,
  showDay,
  driverName,
  plate,
  onOpen,
}: {
  attempt: EngineAttempt;
  index: RuleInfo;
  showDay: boolean;
  driverName: (id: number) => string;
  plate: (id: number) => string;
  onOpen: (attempt: EngineAttempt) => void;
}) {
  const at = parseApiDate(attempt.At);
  const service = attempt.TripLabel?.trim() || t.common.serviceOf(attempt.TripId);
  const names = attempt.RuleIds.map(id => index.get(id)?.rule.Name ?? id);
  return (
    <tr className="cursor-pointer align-top hover:bg-sunken" onClick={() => onOpen(attempt)}>
      <td className="whitespace-nowrap border-b border-line px-2 py-3 text-ink-2">
        <time dateTime={at.toISOString()} title={formatDayTime(at)}>
          {showDay ? formatDayTime(at) : formatTime(at)}
        </time>
      </td>
      <td className="border-b border-line px-2 py-3">
        <span className="flex flex-wrap gap-1">
          <OutcomeBadge outcome={attempt.Outcome} />
          <OriginBadge origin={attempt.Origin} />
        </span>
      </td>
      <td className="border-b border-line px-2 py-3">
        <button
          type="button"
          aria-haspopup="dialog"
          onClick={event => {
            event.stopPropagation();
            onOpen(attempt);
          }}
          className="block max-w-full truncate text-left font-medium text-ink hover:underline"
          title={service}
        >
          {service}
        </button>
        <span className="block truncate text-xs text-ink-2">
          {driverName(attempt.DriverId)} · {plate(attempt.VehicleId)}
          <span className="xl:hidden"> {t.engine.byWhoDot(whoText(attempt.Who))}</span>
        </span>
      </td>
      <td className="border-b border-line px-2 py-3">
        {names.length > 0 ? (
          <span className="block truncate text-ink" title={names.join(', ')}>
            {names.join(', ')}
          </span>
        ) : (
          <span className="text-ink-3">{attempt.Outcome === 'Assigned' ? t.common.allMet : t.common.notRecorded}</span>
        )}
      </td>
      <td className="hidden truncate border-b border-line px-2 py-3 text-ink-2 xl:table-cell">{whoText(attempt.Who)}</td>
    </tr>
  );
}

const Field = ({ label, children }: { label: string; children: ReactElement }) => (
  <label className="block">
    <span className="mb-1 block text-xs font-medium text-ink-2">{label}</span>
    {children}
  </label>
);

function AttemptSheet({
  attempt,
  onClose,
  ...rest
}: {
  attempt: EngineAttempt | null;
  onClose: () => void;
  index: RuleInfo;
  driverName: (id: number) => string;
  plate: (id: number) => string;
  baseName: (id: number) => string;
  onOpenTrip: (tripId: number) => void;
  onCatalog: (groupKey?: string) => void;
}) {
  return (
    <Sheet open={attempt != null} onClose={onClose} title={t.common.attemptDetail} wide>
      {attempt && <AttemptDetail attempt={attempt} {...rest} />}
    </Sheet>
  );
}

export function AttemptDetail({
  attempt,
  index,
  driverName,
  plate,
  baseName,
  onOpenTrip,
  onCatalog,
}: {
  attempt: EngineAttempt;
  index: RuleInfo;
  driverName: (id: number) => string;
  plate: (id: number) => string;
  baseName: (id: number) => string;
  onOpenTrip: (tripId: number) => void;
  onCatalog: (groupKey?: string) => void;
}) {
  const [detail, setDetail] = useState<Loaded<EngineAttemptDetail> | null>(null);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let live = true;
    setDetail(null);
    engine.attempt(attempt.Id).then(
      d => live && setDetail({ ok: true, value: d }),
      e => live && setDetail({ ok: false, error: (e as Error).message }),
    );
    return () => {
      live = false;
    };
  }, [attempt, retry]);

  const shown = attempt;
  const forced = shown.Outcome === 'Forced';
  const at = parseApiDate(shown.At);
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-2">
        <OutcomeBadge outcome={shown.Outcome} />
        <OriginBadge origin={shown.Origin} />
        <time className="text-meta text-ink-2" dateTime={at.toISOString()}>
          {at.toLocaleString(locale(), {
            weekday: 'long',
            day: 'numeric',
            month: 'long',
            hour: '2-digit',
            minute: '2-digit',
            timeZone: TIME_ZONE,
          })}
        </time>
      </div>
      <dl className="grid grid-cols-[140px_minmax(0,1fr)] gap-x-4 gap-y-3 text-data">
        <Row label={forced ? t.engine.skippedRules : t.engine.rule}>
          {shown.RuleIds.length === 0 ? (
            <span className="text-ink-2">
              {shown.Outcome === 'Assigned'
                ? t.engine.metAll
                : t.engine.noRule}
            </span>
          ) : (
            <ul className="space-y-2">
              {shown.RuleIds.map(id => {
                const info = index.get(id);
                return (
                  <li key={id} className="well px-3 py-2">
                    <span className="font-semibold text-ink">{info?.rule.Name ?? id}</span>
                    {info && (
                      <span className="ml-2 text-meta text-ink-2">
                        {info.rule.Reference} · {info.rule.Figure}
                      </span>
                    )}
                    {info && (
                      <button type="button" className="btn btn-link -ml-2 mt-1 flex" onClick={() => onCatalog(info.group.Key)}>
                        {t.engine.seeInCatalogue}
                        <ArrowRight className="h-4 w-4" aria-hidden />
                      </button>
                    )}
                  </li>
                );
              })}
            </ul>
          )}
        </Row>
        <Row label={t.common.service}>{shown.TripLabel?.trim() || t.common.serviceOf(shown.TripId)}</Row>
        <Row label={t.common.driver}>{driverName(shown.DriverId)}</Row>
        <Row label={t.common.vehicle}>{plate(shown.VehicleId)}</Row>
        <Row label={t.common.base}>{baseName(shown.BaseId)}</Row>
        <Row label={t.common.who}>
          {whoText(shown.Who)}{' '}
          <span className="text-ink-3">
            {t.engine.sessionAndOrigin(shown.Session ? t.engine.sessionOf(shown.Session) : t.engine.noSession, originText(shown.Origin).toLowerCase())}
          </span>
        </Row>
        {forced && (
          <Row label={t.common.reason}>
            {shown.ForcedReason ?? <span className="text-ink-2">{t.engine.reasonInDetail}</span>}
          </Row>
        )}
        {/* A forced attempt with its reason and rules already stored apart does not repeat the old text that joined them. */}
        {!(forced && shown.ForcedReason && shown.RuleIds.length > 0) && (
          <Row label={t.engine.detail}>
            {shown.Detail ?? (
              <span className="text-ink-2">
                {shown.Outcome === 'Assigned' ? t.engine.nothingBroken : t.engine.noDetail}
              </span>
            )}
          </Row>
        )}
      </dl>
      <section aria-label={t.common.engineExplanation} className="border-t border-line pt-4">
        <h3 className="mb-2 font-semibold text-ink">{t.common.engineExplanation}</h3>
        {!detail && <SkeletonLines lines={4} label={t.common.loadingExplanation} />}
        {detail && !detail.ok && (
          <ErrorState title={t.engine.explanationFailed} onRetry={() => setRetry(n => n + 1)}>
            {detail.error}
          </ErrorState>
        )}
        {detail?.ok &&
          (detail.value.Explanation ? (
            <ExplanationBody explanation={detail.value.Explanation} />
          ) : (
            <p className="text-meta text-ink-2">
              {t.engine.olderAttempt}
            </p>
          ))}
      </section>
      <button type="button" className="btn" onClick={() => onOpenTrip(shown.TripId)}>
        {t.common.openServiceBooking}
        <ArrowRight className="h-4 w-4" aria-hidden />
      </button>
    </div>
  );
}
