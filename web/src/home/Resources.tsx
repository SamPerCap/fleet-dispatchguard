import { t } from '../i18n/t';
import { BusFront, Users } from 'lucide-react';
import type { Fresh } from '../control';
import { staleOf } from './data';
import type { ControlResources } from '../types';
import { ErrorState, Hint, IconBadge, Module, SkeletonLines } from '../ui';
import type { LucideIcon } from 'lucide-react';

interface Part {
  count: number;
  label: string;
  swatch: string;
  hint: string;
  legal?: boolean;
}

/**
 * Each resource is in a single state, so they add up to the total. Legal limits (CE 561/2006) are marked apart from the physical
 * state.
 */
export function ResourcesModule({
  resources,
  scopeName,
  now,
  onRetry,
  className,
}: {
  resources?: Fresh<ControlResources>;
  scopeName: string;
  now: Date;
  onRetry: () => void;
  className?: string;
}) {
  const data = resources?.ok ? resources.value : null;
  return (
    <Module
      title={t.now.title}
      icon={Users}
      className={className}
      stale={staleOf(resources, now)}
      sub={<span className="text-meta text-ink-2">{scopeName}</span>}
    >
      {!resources && <SkeletonLines lines={6} label={t.now.loading} />}
      {resources && !resources.ok && (
        <ErrorState title={t.now.failed} onRetry={onRetry}>
          {resources.error}
        </ErrorState>
      )}
      {data && (
        <div className="space-y-5">
          <Group
            icon={Users}
            label={t.common.drivers}
            total={data.Drivers.Total}
            available={data.Drivers.Available}
            parts={[
              {
                count: data.Drivers.Available,
                label: t.now.available,
                swatch: 'bg-success',
                hint: t.now.driversAvailable,
              },
              {
                count: data.Drivers.LegallyRestricted,
                label: t.now.restricted,
                swatch: 'bg-warning',
                legal: true,
                hint: t.now.driversRestricted,
              },
              {
                count: data.Drivers.Assigned,
                label: t.now.laterToday,
                swatch: 'bg-navy/35',
                hint: t.now.driversLater,
              },
              {
                count: data.Drivers.InService,
                label: t.now.inService,
                swatch: 'bg-navy',
                hint: t.now.driversInService,
              },
              {
                count: data.Drivers.Unavailable,
                label: t.now.unavailable,
                swatch: 'bg-ink-3',
                hint: t.now.driversUnavailable,
              },
              {
                count: data.Drivers.OutOfHours,
                label: t.now.outOfHours,
                swatch: 'bg-critical',
                legal: true,
                hint: t.now.driversOutOfHours,
              },
            ]}
          />
          <Group
            icon={BusFront}
            label={t.common.vehicles}
            total={data.Vehicles.Total}
            available={data.Vehicles.Available}
            parts={[
              {
                count: data.Vehicles.Available,
                label: t.now.available,
                swatch: 'bg-success',
                hint: t.now.vehiclesAvailable,
              },
              {
                count: data.Vehicles.Assigned,
                label: t.now.laterToday,
                swatch: 'bg-navy/35',
                hint: t.now.driversLater,
              },
              {
                count: data.Vehicles.InService,
                label: t.now.inService,
                swatch: 'bg-navy',
                hint: t.now.vehiclesInService,
              },
              {
                count: data.Vehicles.Unavailable,
                label: t.now.unavailable,
                swatch: 'bg-ink-3',
                hint: t.now.vehiclesUnavailable,
              },
            ]}
          />
        </div>
      )}
    </Module>
  );
}

function Group({
  icon,
  label,
  total,
  available,
  parts,
}: {
  icon: LucideIcon;
  label: string;
  total: number;
  available: number;
  parts: Part[];
}) {
  return (
    <div role="group" aria-label={label} className="min-w-0">
      <div className="flex items-center gap-3">
        <IconBadge icon={icon} tone="neutral" size="sm" />
        <p className="min-w-0 flex-1">
          <span className="block text-meta font-medium text-ink-2">{label}</span>
          <span className="text-page font-bold text-ink">{total}</span>
          <span className="ml-2 text-meta text-ink-2">
            <b className="font-semibold text-success-ink">{available}</b> {t.now.availableLower}
          </span>
        </p>
      </div>
      <div className="mt-3 flex h-2.5 gap-0.5 overflow-hidden rounded-full bg-muted" aria-hidden>
        {total > 0 &&
          parts
            .filter(p => p.count > 0)
            .map(part => <span key={part.label} className={part.swatch} style={{ width: `${(part.count / total) * 100}%` }} />)}
      </div>
      <dl className="mt-3 grid gap-x-6 gap-y-1.5 sm:grid-cols-2 lg:grid-cols-1 xl:grid-cols-2">
        {parts.map(part => (
          <div key={part.label} className="flex min-w-0 items-center gap-1.5 text-xs">
            <span aria-hidden className={`h-2 w-2 shrink-0 rounded-full ${part.swatch}`} />
            <dt className="flex min-w-0 items-center gap-1 text-ink-2">
              <span className="truncate">
                {part.label}
                {part.legal && <span className="text-ink-3"> {t.now.legal}</span>}
              </span>
              <Hint label={`${label}: ${part.label}`}>{part.hint}</Hint>
            </dt>
            <dd className={`ml-auto font-semibold ${part.count === 0 ? 'text-ink-3' : 'text-ink'}`}>{part.count}</dd>
          </div>
        ))}
      </dl>
    </div>
  );
}
