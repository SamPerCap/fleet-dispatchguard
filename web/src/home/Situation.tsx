import { t } from '../i18n/t';
import { AlertTriangle, ArrowDownRight, ArrowUpRight, BusFront, Clock4, MapPin, Minus, Navigation, RefreshCw } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import type { Scope } from '../api';
import { BrandScene } from '../brand/BrandArt';
import { Ring, Sparkline } from '../charts';
import { seriesText } from '../chartmath';
import { freshness, operationStatus, pct, trend } from '../control';
import { formatTime, parseApiDate, startOfDay, TIME_ZONE } from '../gantt';
import type { OperatingBase } from '../types';
import { AnimatedNumber, ErrorState, Hint, IconBadge, Skeleton, StaleNote, StatusPill, type IconTone } from '../ui';
import { staleOf, valueOf, type HomeData } from './data';
import type { LucideIcon } from 'lucide-react';
import { locale } from '../language';

/** The artwork is decorative; the dot of the pill is the "Semáforo …" for screen readers. */
export function HomeHero({
  data,
  now,
  bases,
  scope,
  onScope,
  basesError,
  updatedAt,
  loading,
  paused,
  onRefresh,
}: {
  data: HomeData | null;
  now: Date;
  bases: OperatingBase[];
  scope: Scope;
  onScope: (scope: Scope) => void;
  basesError: string | null;
  updatedAt: Date | null;
  loading: boolean;
  paused: boolean;
  onRefresh: () => void;
}) {
  const summary = data?.summary;
  const summaryData = valueOf(summary);
  const status = operationStatus(summaryData?.Light);
  const days = valueOf(data?.trends)?.Days ?? [];
  const totals = days.map(d => d.TotalTrips);
  const coverage = days.map(d => pct(d.CoveredTrips, d.TotalTrips));
  const covered = summaryData ? pct(summaryData.CoveredTrips, summaryData.TotalTrips) : 0;
  const services = summaryData ? trend(summaryData.TotalTrips, summaryData.YesterdayTotalTrips) : null;
  const departedUncovered = summaryData
    ? summaryData.TotalTrips - summaryData.InProgressBySchedule - summaryData.CompletedBySchedule - summaryData.Upcoming
    : 0;
  const stale = staleOf(summary, now);

  return (
    <section aria-label={t.situation.title} className="relative isolate overflow-hidden rounded-xl bg-surface shadow-1">
      <div
        aria-hidden
        className="pointer-events-none absolute inset-0 -z-10 bg-[linear-gradient(100deg,rgb(var(--surface))_30%,rgb(var(--blue-soft)/0.9)_100%)]"
      />
      <div
        aria-hidden
        className="pointer-events-none absolute -right-24 -top-40 -z-10 h-[420px] w-[min(900px,70%)] rounded-full bg-[radial-gradient(closest-side,rgb(var(--blue)/0.10),transparent)]"
      />
      <BrandScene className="pointer-events-none absolute right-0 top-0 h-[200px] w-[55%] xl:w-[min(760px,62%)] 3xl:h-[230px] 3xl:w-[min(1040px,58%)] [mask-image:linear-gradient(to_right,transparent,black_40%)]" />
      <div className="relative flex flex-wrap items-start gap-x-6 gap-y-4 px-6 pt-6 lg:px-8 lg:pt-7">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-page font-bold text-ink">{t.app.control}</h1>
            {summaryData && <StatusPill tone={status.tone} text={status.text} light={status.light} />}
          </div>
          <p className="mt-1 text-meta text-ink-2">
            {t.situation.operatingDay}{' '}
            <time className="inline-block font-medium text-ink first-letter:uppercase" dateTime={startOfDay(now).toISOString()}>
              {now.toLocaleDateString(locale(), { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: TIME_ZONE })}
            </time>
            <span aria-hidden> · </span>{t.situation.madridTime}
          </p>
          {stale && (
            <div className="mt-3">
              <StaleNote text={stale} />
            </div>
          )}
        </div>
        <div className="flex basis-full flex-wrap items-center gap-2">
          <div className="flex flex-wrap items-center gap-2 rounded-lg bg-surface/90 p-1.5 shadow-1 backdrop-blur">
            <label className="flex items-center gap-2 pl-2">
              <MapPin className="h-4 w-4 text-ink-3" aria-hidden />
              <span className="text-meta font-medium text-ink-2">{t.common.base}</span>
              <select
                className="input min-w-[200px] font-medium"
                aria-label={t.common.base}
                value={String(scope)}
                onChange={e => onScope(e.target.value === 'all' ? 'all' : Number(e.target.value))}
              >
                <option value="all">{t.common.allBasesCount(bases.length ? ` (${bases.length})` : '')}</option>
                {bases.map(base => (
                  <option key={base.Id} value={base.Id}>
                    {base.Name}
                  </option>
                ))}
              </select>
            </label>
            <Freshness updatedAt={updatedAt} loading={loading} paused={paused} onRefresh={onRefresh} />
          </div>
        </div>
      </div>
      {basesError && (
        <p role="alert" className="relative px-6 pt-2 text-meta text-critical-ink lg:px-8">
          {t.common.basesFailed(basesError)}
        </p>
      )}

      <div className="relative grid gap-4 p-6 pt-10 sm:grid-cols-2 lg:px-8 lg:pb-8 xl:grid-cols-4 3xl:gap-6">
        {summary && !summary.ok && (
          <div className="sm:col-span-2 xl:col-span-4">
            <ErrorState title={t.situation.failed} onRetry={onRefresh}>
              {summary.error}
            </ErrorState>
          </div>
        )}
        {(!summary || summary.ok) && (
          <>
            <Kpi
              icon={BusFront}
              tone="blue"
              label={t.situation.servicesToday}
              value={summaryData && <AnimatedNumber value={summaryData.TotalTrips} />}
              loading={!summaryData}
              hint={t.situation.servicesWhat}
              chart={
                totals.length > 1 && (
                  <Sparkline values={totals} tone="blue" label={seriesText(t.situation.servicesCurve, totals)} width={96} />
                )
              }
              foot={
                services && <TrendText delta={services.delta} text={services.text} title={t.common.yesterday(summaryData!.YesterdayTotalTrips)} />
              }
            />

            <Kpi
              label={t.situation.coverage}
              loading={!summaryData}
              lead={
                <Ring
                  percent={covered}
                  size={48}
                  stroke={6}
                  tone={covered === 100 ? 'success' : covered >= 80 ? 'blue' : 'warning'}
                  label={t.situation.coverageValue(covered)}
                />
              }
              value={
                summaryData && (
                  <>
                    <AnimatedNumber value={covered} />
                    <span className="ml-0.5 text-page font-bold text-ink-2">%</span>
                  </>
                )
              }
              hint={t.situation.coverageWhat}
              chart={
                coverage.length > 1 && (
                  <Sparkline
                    values={coverage}
                    tone="success"
                    label={seriesText(t.situation.coverageCurve, coverage, v => `${v} %`)}
                    width={96}
                  />
                )
              }
              foot={
                summaryData && (
                  <span>
                    {t.situation.servicesOf(summaryData.CoveredTrips, summaryData.TotalTrips)}
                  </span>
                )
              }
            />

            <Kpi
              icon={Navigation}
              tone="navy"
              label={t.situation.running}
              value={summaryData && <AnimatedNumber value={summaryData.InProgressBySchedule} />}
              loading={!summaryData}
              hint={t.situation.runningWhat}
              foot={
                summaryData && (
                  <span>
                    {t.situation.completed(summaryData.CompletedBySchedule, departedUncovered > 0 ? t.situation.notLeft(departedUncovered) : '')}
                  </span>
                )
              }
            />

            <Kpi
              icon={Clock4}
              tone="warning"
              label={t.situation.toLeave}
              value={summaryData && <AnimatedNumber value={summaryData.Upcoming} />}
              loading={!summaryData}
              hint={t.situation.toLeaveWhat}
              foot={
                summaryData && (
                  <span className="block truncate" title={summaryData.NextDepartureLabel ?? undefined}>
                    {summaryData.NextDeparture
                      ? t.situation.next(formatTime(parseApiDate(summaryData.NextDeparture)), summaryData.NextDepartureLabel ? ` · ${summaryData.NextDepartureLabel}` : '')
                      : t.situation.noneLeft}
                  </span>
                )
              }
            />
          </>
        )}
      </div>
    </section>
  );
}

function TrendText({ delta, text, title }: { delta: number; text: string; title: string }) {
  const Icon = delta > 0 ? ArrowUpRight : delta < 0 ? ArrowDownRight : Minus;
  return (
    <span className="inline-flex items-center gap-1 font-medium text-ink-2" title={title}>
      <Icon className="h-4 w-4" aria-hidden />
      {text}
    </span>
  );
}

function Kpi({
  icon,
  tone = 'blue',
  lead,
  label,
  value,
  foot,
  hint,
  chart,
  loading,
}: {
  icon?: LucideIcon;
  tone?: IconTone;
  lead?: ReactNode;
  label: string;
  value: ReactNode;
  foot?: ReactNode;
  hint: string;
  chart?: ReactNode;
  loading: boolean;
}) {
  // Fixed shape: no layout shifts on loading or refreshing.
  return (
    <div className="flex min-w-0 flex-col gap-3 rounded-lg bg-surface p-5 shadow-2" role="group" aria-label={label}>
      <div className="flex h-12 items-center justify-between gap-3">
        {lead ?? (icon && <IconBadge icon={icon} tone={tone} size="lg" />)}
        {!loading && chart}
      </div>
      <div className="min-w-0">
        {loading ? <Skeleton className="h-10 w-20" /> : <p className="text-display font-bold text-ink">{value}</p>}
        <p className="mt-1 flex items-center gap-1.5 text-meta font-semibold text-ink">
          {label}
          <Hint label={label}>{hint}</Hint>
        </p>
        <div className="mt-0.5 min-h-[16px] truncate text-xs text-ink-2">{loading ? <Skeleton className="h-3 w-28" /> : foot}</div>
      </div>
    </div>
  );
}

export function Freshness({
  updatedAt,
  loading,
  paused,
  onRefresh,
}: {
  updatedAt: Date | null;
  loading: boolean;
  paused: boolean;
  onRefresh: () => void;
}) {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 1_000);
    return () => clearInterval(timer);
  }, []);
  const age = updatedAt ? freshness(updatedAt, now) : null;
  return (
    <div className="flex items-center gap-1">
      <p
        className={`inline-flex h-9 items-center gap-2 rounded-md px-3 text-meta ${age?.stale ? 'bg-warning-soft font-medium text-warning-ink' : 'text-ink-2'}`}
      >
        {age?.stale ? <AlertTriangle className="h-4 w-4" aria-hidden /> : <span aria-hidden className="h-2 w-2 rounded-full bg-success" />}
        {paused && t.situation.paused}
        {!age ? (loading ? t.situation.loadingData : t.situation.noCurrentData) : t.situation.updated(age.text)}
        {age?.stale && <span className="sr-only"> {t.situation.maybeStale}</span>}
      </p>
      <button type="button" className="btn btn-text btn-sm" onClick={onRefresh} disabled={loading} aria-label={t.situation.updateNow}>
        <RefreshCw className="h-4 w-4" aria-hidden />
        <span className="hidden sm:inline">{t.situation.update}</span>
      </button>
    </div>
  );
}
