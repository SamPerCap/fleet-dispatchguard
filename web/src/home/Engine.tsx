import { t } from '../i18n/t';
import { ArrowRight, CircleCheck, Scale } from 'lucide-react';
import { AreaChart } from '../charts';
import { peakIndex } from '../chartmath';
import { blockReason, hourIndex, trend, type Fresh } from '../control';
import { formatTime, parseApiDate } from '../gantt';
import { staleOf, type Loaded } from './data';
import type { EngineHourly, EngineMetrics, RuleGroup } from '../types';
import { ErrorState, Hint, Module, Skeleton } from '../ui';

export function EngineModule({
  metrics,
  yesterday,
  hourly,
  rules,
  now,
  onOpen,
  onRetry,
  className,
}: {
  metrics?: Fresh<EngineMetrics>;
  yesterday?: Fresh<EngineMetrics>;
  hourly?: Fresh<EngineHourly>;
  rules: Loaded<RuleGroup[]> | null;
  now: Date;
  onOpen: () => void;
  onRetry: () => void;
  className?: string;
}) {
  const data = metrics?.ok ? metrics.value : null;
  const vsYesterday = data && yesterday?.ok ? trend(data.Validations, yesterday.value.Validations) : null;
  const allRules = rules?.ok ? rules.value.flatMap(g => g.Rules) : null;
  const mandatory = allRules?.filter(r => r.Kind === 'Blocking').length;
  const advisory = allRules?.filter(r => r.Kind === 'Ranking' || r.Kind === 'Warning').length;
  const reasons = data ? Object.entries(data.BlockedByReason).filter(([, count]) => count > 0) : [];
  const hourlyData = hourly?.ok ? hourly.value : null;

  return (
    <Module
      title={t.engine.title}
      icon={Scale}
      tone="navy"
      className={`flex flex-col ${className ?? ''}`}
      stale={staleOf(metrics, now) ?? staleOf(hourly, now)}
      sub={
        <span className="inline-flex items-center gap-1.5 rounded-full bg-success-bright/15 px-2.5 py-0.5 text-xs font-semibold text-success-bright">
          <span aria-hidden className="h-1.5 w-1.5 rounded-full bg-success-bright" />
          {t.engine.active}
        </span>
      }
    >
      <p className="-mt-2 mb-4 text-meta text-on-navy-2">{t.engine.validatesFirst}</p>

      {metrics && !metrics.ok && (
        <ErrorState title={t.engine.figuresFailed} onRetry={onRetry}>
          {metrics.error}
        </ErrorState>
      )}
      {!metrics && (
        <div className="space-y-3">
          <Skeleton className="h-10 w-24 bg-white/10" />
          <Skeleton className="h-20 w-full bg-white/10" />
        </div>
      )}
      {data && (
        <div role="group" aria-label={t.engine.todayTitle}>
          <dl className="grid grid-cols-3 gap-x-3 gap-y-4">
            <Figure
              big
              className="col-span-3"
              label={t.engine.todayKpi}
              value={data.Validations}
              hintLabel={t.engine.regulatory}
              hint={t.engine.todayWhat}
              sub={vsYesterday?.text}
              subTitle={yesterday?.ok ? t.common.yesterday(yesterday.value.Validations) : undefined}
            />
            <Figure
              label={t.engine.blocked}
              value={data.IllegalBlocked}
              valueClass="text-critical-bright"
              hint={t.engine.blockedWhat}
            />
            <Figure
              label={t.engine.accepted}
              value={data.Accepted}
              valueClass="text-success-bright"
              hint={t.engine.acceptedToday}
            />
            <Figure
              label={t.engine.forced}
              value={data.Forced}
              valueClass="text-warning-bright"
              hint={t.engine.forcedWhat}
            />
          </dl>
          <p className="mt-2 text-xs text-on-navy-2">
            {t.engine.sum(data.Validations, data.Accepted, data.IllegalBlocked, data.Forced)}
            {reasons.length > 0 && (
              <> {t.engine.blocksBy(reasons.map(([reason, count]) => `${blockReason(reason)} (${count})`).join(', '))}</>
            )}
          </p>
        </div>
      )}

      <div className="mt-4 flex-1">
        {hourlyData ? (
          <HourlyCurve hourly={hourlyData} now={now} />
        ) : hourly && !hourly.ok ? (
          <p role="alert" className="text-meta text-critical-bright">
            {t.engine.hourlyFailed(hourly.error)}
          </p>
        ) : (
          <Skeleton className="h-24 w-full bg-white/10" />
        )}
      </div>

      {/* The link to the rules engine is always there, even if the figures fail to load. */}
      <div className="mt-4 space-y-2 border-t border-white/10 pt-4">
        {data && (
          <p className="flex min-w-0 items-center gap-2 text-meta font-medium text-white">
            <CircleCheck className="h-4 w-4 shrink-0 text-success-bright" aria-hidden />
            <span>
              {t.engine.noneIllegal(data.Forced > 0 ? t.engine.andForced(data.Forced) : '')}
            </span>
            <Hint label={t.engine.noneIllegalTitle}>
              {t.engine.noneIllegalWhat}
            </Hint>
          </p>
        )}
        <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
          {allRules ? (
            <p className="text-xs text-on-navy-2">
              {t.engine.ruleCounts(mandatory ?? 0, advisory ?? 0)}
            </p>
          ) : (
            <span />
          )}
          <button
            type="button"
            className="relative -mr-2 inline-flex h-8 items-center gap-1 rounded-md px-2 text-meta font-semibold text-white hover:bg-white/10 after:absolute after:-inset-1.5 after:content-['']"
            onClick={onOpen}
          >
            {t.tour.seeEngine}
            <ArrowRight className="h-4 w-4" aria-hidden />
          </button>
        </div>
      </div>
    </Module>
  );
}

function HourlyCurve({ hourly, now }: { hourly: EngineHourly; now: Date }) {
  const values = hourly.Hours.map(x => x.Validations);
  const start = parseApiDate(hourly.From);
  const current = Math.min(values.length - 1, Math.max(0, hourIndex(start, now)));
  const peak = peakIndex(values.slice(0, current + 1));
  const total = values.reduce((sum, value) => sum + value, 0);
  const label =
    total === 0
      ? t.engine.todayNone
      : t.engine.todayCurve(total, values[peak], formatTime(parseApiDate(hourly.Hours[peak].Hour)), formatTime(parseApiDate(hourly.Hours[current].Hour)));
  const marks = [0, 6, 12, 18, values.length - 1]
    .filter((value, index, all) => value < values.length && all.indexOf(value) === index)
    .map(index => ({ index, text: index === values.length - 1 ? '24:00' : formatTime(parseApiDate(hourly.Hours[index].Hour)) }));
  return (
    <figure>
      <figcaption className="mb-2 flex items-baseline justify-between text-xs text-on-navy-2">
        <span className="font-medium text-white">{t.engine.perHour}</span>
        {total > 0 && (
          <span>
            {t.engine.peakAt(values[peak], formatTime(parseApiDate(hourly.Hours[peak].Hour)))}
          </span>
        )}
      </figcaption>
      <AreaChart values={values} upTo={current} label={label} marks={marks} onNavy height={88} />
    </figure>
  );
}

function Figure({
  label,
  value,
  hint,
  hintLabel,
  dot,
  big,
  sub,
  subTitle,
  valueClass = 'text-white',
  className = '',
}: {
  className?: string;
  label: string;
  value: number;
  hint: string;
  hintLabel?: string;
  dot?: string;
  big?: boolean;
  sub?: string;
  subTitle?: string;
  valueClass?: string;
}) {
  return (
    <div className={`flex min-w-0 flex-col ${className}`}>
      <dt className="order-2 flex items-center gap-1.5 text-xs text-on-navy-2">
        {dot && <span aria-hidden className={`h-2 w-2 shrink-0 rounded-full ${dot}`} />}
        <span className="whitespace-nowrap">{label}</span>
        <Hint label={hintLabel ?? label}>{hint}</Hint>
      </dt>
      <dd className={`order-1 ${big ? 'text-display' : 'text-page'} font-bold ${valueClass}`}>{value}</dd>
      {sub && (
        <dd className="order-3 mt-0.5 text-xs font-medium text-white" title={subTitle}>
          {sub}
        </dd>
      )}
    </div>
  );
}
