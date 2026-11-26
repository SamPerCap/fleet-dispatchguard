import { t } from '../i18n/t';
import { ArrowRight, FileChartColumn, Gauge } from 'lucide-react';
import { MiniBars, Ring } from '../charts';
import { seriesText } from '../chartmath';
import { hourIndex, type Fresh } from '../control';
import { formatTime, parseApiDate } from '../gantt';
import { kmText, savingText } from '../optimize';
import { staleOf } from './data';
import type { ControlEfficiency } from '../types';
import { ErrorState, Hint, Module, Skeleton } from '../ui';
import { locale } from '../language';

const num = (value: number, digits = 0) => value.toLocaleString(locale(), { maximumFractionDigits: digits, minimumFractionDigits: 0 });

/** Only calculated metrics and no composite index (user decision, 2026-11-19). */
export function EfficiencyModule({
  efficiency,
  now,
  scopeName,
  onOptimize,
  onRetry,
  className,
}: {
  efficiency?: Fresh<ControlEfficiency>;
  now: Date;
  scopeName: string;
  onOptimize: () => void;
  onRetry: () => void;
  className?: string;
}) {
  const data = efficiency?.ok ? efficiency.value : null;
  const counts = data?.VehiclesInServiceByHour.map(h => h.Count) ?? [];
  const current = data ? hourIndex(parseApiDate(data.From), now) : -1;
  const peak = counts.length ? Math.max(...counts) : 0;
  const hours =
    data?.WindowStart && data.WindowEnd
      ? `${formatTime(parseApiDate(data.WindowStart))}–${formatTime(parseApiDate(data.WindowEnd))}`
      : null;

  return (
    <Module
      title={t.efficiency.title}
      icon={Gauge}
      tone="navy"
      className={className}
      stale={staleOf(efficiency, now)}
      sub={<span className="text-meta text-on-navy-2">{t.efficiency.todayIn(scopeName)}</span>}
    >
      {efficiency && !efficiency.ok && (
        <ErrorState title={t.efficiency.failed} onRetry={onRetry}>
          {efficiency.error}
        </ErrorState>
      )}
      {!efficiency && (
        <div className="flex gap-4">
          <Skeleton className="h-24 w-24 rounded-full bg-white/10" />
          <Skeleton className="h-24 flex-1 bg-white/10" />
        </div>
      )}
      {data && (
        <div className="grid gap-5 lg:grid-cols-2 lg:gap-x-8 3xl:grid-cols-1">
          <div className="flex items-center gap-4">
            <Ring
              percent={data.FleetUtilizationPercent}
              size={96}
              stroke={9}
              tone="bright"
              onNavy
              label={t.efficiency.fleetUseValue(num(data.FleetUtilizationPercent, 1))}
            >
              <span className="text-page font-bold text-white">
                {num(data.FleetUtilizationPercent)}
                <span className="text-meta font-semibold text-on-navy-2"> %</span>
              </span>
            </Ring>
            <div className="min-w-0 flex-1">
              <p className="flex items-center gap-1.5 text-meta font-semibold text-white">
                {t.efficiency.fleetUse}
                <Hint label={t.efficiency.fleetUse}>
                  {t.efficiency.fleetUseWhat(hours ?? t.efficiency.differentHours)}
                </Hint>
              </p>
              <p className="text-xs text-on-navy-2">
                {t.efficiency.hoursOf(num(data.AssignedVehicleHours, 1), num(data.AvailableVehicleHours, 1))}
              </p>
              <p className="text-xs text-on-navy-2">
                {t.efficiency.vehiclesAnd(data.ActiveVehicles, hours ? t.efficiency.workingHours(hours) : t.efficiency.eachBase)}
              </p>
            </div>
          </div>

          {counts.length > 0 && (
            <figure>
              <figcaption className="mb-2 flex items-baseline justify-between text-xs text-on-navy-2">
                <span className="font-medium text-white">{t.efficiency.perHour}</span>
                <span>{peak > 0 ? t.efficiency.max(peak) : t.efficiency.noneToday}</span>
              </figcaption>
              <MiniBars
                values={counts}
                highlight={current}
                onNavy
                height={48}
                label={seriesText(t.efficiency.perHourRange, counts)}
              />
            </figure>
          )}

          <div className="flex flex-wrap items-center gap-x-4 gap-y-3 border-t border-white/10 pt-4 lg:col-span-2 3xl:col-span-1">
            <span aria-hidden className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-white/10 text-white">
              <FileChartColumn className="h-[18px] w-[18px]" />
            </span>
            <div className="min-w-[200px] flex-1">
              <p className="flex items-center gap-1.5 text-xs text-on-navy-2">
                {t.efficiency.kmSavingEmpty}
                <Hint label={t.common.kmSaving}>
                  {t.efficiency.kmSavingWhat}
                </Hint>
              </p>
              {data.BaselineDeadheadKm <= 0 ? (
                <p className="text-meta font-semibold text-white">{t.efficiency.noneToSave}</p>
              ) : data.ProposedChanges === 0 ? (
                <p className="text-module font-bold text-white">
                  {t.efficiency.onlyKm(kmText(data.BaselineDeadheadKm, 0))}
                  <span className="ml-2 text-meta font-semibold text-on-navy-2">{savingText(0, 0)}</span>
                </p>
              ) : (
                <p className="text-module font-bold text-white">
                  <span className="whitespace-nowrap">
                    {t.efficiency.fromTo(kmText(data.BaselineDeadheadKm, 0), kmText(data.ProposedDeadheadKm, 0))}
                  </span>
                  <span className="ml-2 whitespace-nowrap text-meta font-semibold text-success-bright">
                    {t.efficiency.saving(savingText(data.ProposedChanges, data.SavingPercent, 0))}
                  </span>
                </p>
              )}
              <p className="text-xs text-on-navy-2">
                {data.ProposedChanges === 0
                  ? data.TripsDeparted > 0 && data.TripsDeparted === data.TripsConsidered
                    ? t.efficiency.allDeparted
                    : t.efficiency.noChanges
                  : t.efficiency.changes(data.ProposedChanges)}
              </p>
            </div>
            <button
              type="button"
              onClick={onOptimize}
              className="relative inline-flex h-9 items-center gap-1.5 rounded-md bg-white/10 px-3 text-meta font-semibold text-white hover:bg-white/15 after:absolute after:-inset-1 after:content-['']"
            >
              {t.efficiency.seeReport}
              <ArrowRight className="h-4 w-4" aria-hidden />
            </button>
          </div>
        </div>
      )}
    </Module>
  );
}
