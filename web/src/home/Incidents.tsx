import { t } from '../i18n/t';
import {
  AlertOctagon,
  AlertTriangle,
  Armchair,
  ArrowRight,
  CalendarClock,
  ChevronDown,
  CircleCheck,
  Clock,
  Info,
  ShieldAlert,
  Truck,
  UserX,
  type LucideIcon,
} from 'lucide-react';
import { useState } from 'react';
import {
  alertTarget,
  impactText,
  incidentTypeText,
  incidentTier,
  relative,
  TIER_TEXT,
  type AlertTarget,
  type Fresh,
  type IncidentTier,
} from '../control';
import { formatTime, parseApiDate } from '../gantt';
import { staleOf } from './data';
import type { AlertType, ControlIncident } from '../types';
import { EmptyState, ErrorState, Module, SkeletonLines } from '../ui';

const TYPE_ICON: Record<AlertType, LucideIcon> = {
  UnassignedTrip: UserX,
  PartialBooking: CalendarClock,
  UnavailabilityGap: AlertTriangle,
  DriverNearLimit: Clock,
  VehicleUnavailable: Truck,
  ForcedAssignment: ShieldAlert,
  InsufficientSeats: Armchair,
};

/** Literal classes, so that Tailwind keeps them. */
const TIER: Record<IncidentTier, { bar: string; icon: string; tag: string; tagIcon: LucideIcon }> = {
  critical: {
    bar: 'bg-critical',
    icon: 'bg-critical-soft text-critical-ink',
    tag: 'bg-critical-soft text-critical-ink',
    tagIcon: AlertOctagon,
  },
  high: { bar: 'bg-warning', icon: 'bg-warning-soft text-warning-ink', tag: 'bg-warning-soft text-warning-ink', tagIcon: AlertTriangle },
  preventive: { bar: 'bg-blue', icon: 'bg-blue-soft text-blue-ink', tag: 'bg-blue-soft text-blue-ink', tagIcon: Clock },
  notice: { bar: 'bg-ink-3/50', icon: 'bg-muted text-ink-2', tag: 'bg-muted text-ink-2', tagIcon: Info },
};

const VISIBLE = 4;

/** ONE strong button on the whole screen: the first incident (red if critical). The rest are discreet. */
export function IncidentsModule({
  incidents,
  now,
  scopeName,
  onAction,
  onRetry,
  className,
}: {
  incidents?: Fresh<ControlIncident[]>;
  now: Date;
  scopeName: string;
  onAction: (target: AlertTarget) => void;
  onRetry: () => void;
  className?: string;
}) {
  const [all, setAll] = useState(false);
  const list = incidents?.ok ? incidents.value : null;
  const urgent = list?.filter(i => i.Severity === 'Urgent').length ?? 0;
  const shown = list ? (all ? list : list.slice(0, VISIBLE)) : [];
  return (
    <Module
      title={t.incidents.title}
      icon={AlertTriangle}
      iconTone={urgent > 0 ? 'critical' : 'warning'}
      tone={urgent > 0 ? 'critical' : 'light'}
      className={className}
      stale={staleOf(incidents, now)}
      sub={
        list &&
        list.length > 0 && (
          <span className="flex flex-wrap items-center gap-2 text-meta text-ink-2">
            {urgent > 0 && (
              <span className="rounded-full bg-critical-ink px-2.5 py-0.5 text-xs font-semibold text-white">
                {t.incidents.urgentCount(urgent)}
              </span>
            )}
            <span>
              {t.incidents.warningCount(list.length - urgent)} · {scopeName}
            </span>
          </span>
        )
      }
      actions={
        list &&
        list.length > VISIBLE && (
          <button type="button" className="btn btn-link" aria-expanded={all} onClick={() => setAll(a => !a)}>
            {all ? t.incidents.onlyFirst : t.incidents.seeAll(list.length)}
            <ArrowRight className={`h-4 w-4 transition-transform ${all ? '-rotate-90' : ''}`} aria-hidden />
          </button>
        )
      }
    >
      {!incidents && <SkeletonLines lines={6} label={t.incidents.loading} />}
      {incidents && !incidents.ok && (
        <ErrorState title={t.incidents.failed} onRetry={onRetry}>
          {incidents.error}
        </ErrorState>
      )}
      {list && list.length === 0 && (
        <EmptyState icon={CircleCheck} tone="ok" title={t.incidents.none}>
          {t.incidents.allLegal(scopeName === t.common.allBases ? t.common.allBasesLower : scopeName)}
        </EmptyState>
      )}
      {list && list.length > 0 && (
        <ol className={`space-y-2 ${all ? 'max-h-[560px] overflow-y-auto pr-1' : ''}`}>
          {shown.map((incident, index) => (
            <IncidentRow key={incident.Key} incident={incident} now={now} primary={index === 0} onAction={onAction} />
          ))}
        </ol>
      )}
    </Module>
  );
}

function IncidentRow({
  incident,
  now,
  primary,
  onAction,
}: {
  incident: ControlIncident;
  now: Date;
  primary: boolean;
  onAction: (target: AlertTarget) => void;
}) {
  const [open, setOpen] = useState(false);
  const tier = incidentTier(incident);
  const style = TIER[tier];
  const TypeIcon = TYPE_ICON[incident.Type] ?? Info; // A tab opened before a deploy may get a type it does not know.
  const TagIcon = style.tagIcon;
  const target = alertTarget(incident);
  const deadline = incident.Deadline ? parseApiDate(incident.Deadline) : null;
  const late = deadline != null && deadline < now;
  const also = incident.Types.filter(t => t !== incident.Type);
  const listId = `affected-${incident.Key.replace(/[^a-z0-9]/gi, '-')}`;
  const strong = primary ? (tier === 'critical' ? 'btn btn-danger' : 'btn btn-primary') : 'btn btn-link';
  return (
    <li
      className={`relative overflow-hidden rounded-md bg-surface shadow-1 ${primary && tier === 'critical' ? 'ring-1 ring-critical/30' : ''}`}
    >
      <span aria-hidden className={`absolute inset-y-0 left-0 w-1 ${style.bar}`} />
      <div className="grid gap-x-4 gap-y-2 py-3 pl-5 pr-4 md:grid-cols-[auto_minmax(0,1fr)_auto] md:items-center">
        <span aria-hidden className={`hidden h-10 w-10 place-items-center rounded-full md:grid ${style.icon}`}>
          <TypeIcon className="h-5 w-5" />
        </span>
        <div className="min-w-0">
          <p className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <span className="text-data font-semibold text-ink">{incidentTypeText(incident.Type)}</span>
            {also.map(alertType => (
              <span key={alertType} className="chip">
                + {incidentTypeText(alertType).toLowerCase()}
              </span>
            ))}
          </p>
          <p
            className="truncate text-meta text-ink-2"
            title={incident.Detail ? `${incident.EntityLabel} · ${incident.Detail}` : incident.EntityLabel}
          >
            {incident.EntityLabel}
            {incident.Detail && <> · {incident.Detail}</>}
          </p>
          <p className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-ink-2">
            <span className="inline-flex items-center gap-1">
              <CalendarClock className="h-3.5 w-3.5" aria-hidden />
              {deadline ? (
                <>
                  <b className="font-semibold text-ink">{formatTime(deadline)}</b>
                  <span className={late || tier === 'critical' ? 'font-semibold text-critical-ink' : ''}>{relative(deadline, now)}</span>
                </>
              ) : (
                t.incidents.noDeadline
              )}
            </span>
            <span>{impactText(incident)}</span>
            {incident.AffectedTrips.length > 0 && (
              <button
                type="button"
                className="relative inline-flex items-center gap-0.5 font-medium text-blue-ink hover:underline after:absolute after:-inset-2 after:content-['']"
                aria-expanded={open}
                aria-controls={listId}
                onClick={() => setOpen(o => !o)}
              >
                <ChevronDown className={`h-3.5 w-3.5 transition-transform ${open ? 'rotate-180' : ''}`} aria-hidden />
                {open ? t.incidents.hide : t.incidents.see}{' '}
                {incident.AffectedTrips.length === 1 ? t.incidents.theService : t.incidents.theServices(incident.AffectedTrips.length)}
              </button>
            )}
          </p>
        </div>
        <div className="flex items-center gap-3 md:justify-end">
          <span className={`inline-flex h-6 shrink-0 items-center gap-1 rounded-full pl-1.5 pr-2.5 text-xs font-semibold ${style.tag}`}>
            <TagIcon className="h-3.5 w-3.5" aria-hidden />
            {TIER_TEXT[tier]}
          </span>
          {target && (
            <button
              type="button"
              className={`${strong} ${primary ? '' : 'btn-sm'}`}
              aria-label={`${incident.RecommendedAction}: ${incident.EntityLabel}`}
              onClick={() => onAction(target)}
            >
              {incident.RecommendedAction}
              <ArrowRight className="h-4 w-4" aria-hidden />
            </button>
          )}
        </div>
      </div>
      {open && (
        <ul id={listId} className="mx-4 mb-3 ml-5 divide-y divide-line rounded-md bg-sunken px-3 text-meta">
          {incident.AffectedTrips.map(trip => (
            <li key={trip.TripId} className="flex items-center gap-3 py-2">
              <span className="w-24 shrink-0 font-medium text-ink">
                {formatTime(parseApiDate(trip.Start))}–{formatTime(parseApiDate(trip.End))}
              </span>
              <span className="min-w-0 flex-1 truncate text-ink">{trip.Label}</span>
              <span className="shrink-0 text-ink-2">{t.incidents.seats(trip.Seats)}</span>
            </li>
          ))}
        </ul>
      )}
    </li>
  );
}
