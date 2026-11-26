import { t } from './i18n/t';
import { AlertOctagon, Ban, BusFront, CalendarX2, CircleDot, Clock, Lock, MapPin, User, type LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import type { MasterData } from './App';
import { arrivalDayText, clockChanges, dayText, durationText, tripName, type NumberedTrip } from './booking';
import { isUnassigned, serviceTypeLabel } from './calendar';
import { formatTime, parseApiDate } from './gantt';
import { agendaConflicts, agendaConflictText } from './resources';
import { tripStatus } from './status';
import type { BookingTrip } from './types';
import { Avatar, StatusBadge } from './ui';

/** The panel looks them up to move the view and the focus. */
export const tripBlockId = (tripId: number) => `bp-trip-${tripId}`;
export const tripTitleId = (tripId: number) => `bp-trip-${tripId}-title`;
export const tripActionId = (tripId: number) => `bp-trip-${tripId}-action`;
export const tripFormTitleId = (tripId: number) => `bp-trip-${tripId}-form`;

export const canAssign = (trip: Pick<BookingTrip, 'Status' | 'IsLocked'>) => trip.Status !== 'Cancelled' && !trip.IsLocked;

/**
 * `marked`: the panel was opened from this trip. `form`: the assign form, open inside the block.
 * The title sticks while the form is open, so a long list of candidates never hides which trip it is for.
 */
export function TripBlock({
  item,
  master,
  now,
  headingLevel: Heading,
  marked,
  form,
  onAssign,
}: {
  item: NumberedTrip;
  master: Pick<MasterData, 'drivers' | 'vehicles' | 'locName'>;
  now: Date;
  headingLevel: 'h4' | 'h5';
  marked: boolean;
  form: ReactNode | null;
  onAssign: () => void;
}) {
  const { trip, ordinal } = item;
  const cancelled = trip.Status === 'Cancelled';
  const open = form != null;
  const status = tripStatus(trip, now);
  const action = isUnassigned(trip) ? t.common.assign : t.common.reassign;
  return (
    <article
      id={tripBlockId(trip.Id)}
      aria-labelledby={tripTitleId(trip.Id)}
      aria-current={marked || undefined}
      className={`-scroll-mt-9 rounded-md border border-ink-3/75 bg-surface shadow-1 [overflow-wrap:anywhere] ${marked || open ? 'ring-2 ring-blue' : ''}`}
    >
      <header
        className={`flex flex-wrap items-center gap-x-3 gap-y-1 rounded-t-[9px] px-4 py-2 ${cancelled ? 'bg-muted' : 'bg-navy-tint'} ${open ? 'sticky top-0 z-10' : ''}`}
      >
        <Heading id={tripTitleId(trip.Id)} tabIndex={-1} className="min-w-0 flex-1 text-data text-ink">
          <span className="font-bold">{tripName(item)}</span> · <span className="font-medium">{dayText(parseApiDate(trip.Start))}</span>
        </Heading>
        {marked && item.total > 1 && (
          <span className="inline-flex h-6 items-center rounded-full bg-navy px-2.5 text-xs font-semibold text-white">{t.common.selected}</span>
        )}
        {cancelled ? (
          <span className="inline-flex items-center gap-1 text-xs font-semibold text-ink">
            <Ban className="h-3.5 w-3.5" aria-hidden />
            {t.common.cancelled}
          </span>
        ) : (
          status && <StatusBadge status={status} />
        )}
      </header>
      <div className="space-y-4 p-4">
        <Notices trip={trip} master={master} now={now} />
        <Itinerary trip={trip} locName={master.locName} />
        <TripData trip={trip} />
        {canAssign(trip) && !open && (
          <button
            type="button"
            id={tripActionId(trip.Id)}
            aria-label={t.trip.actionOf(action, ordinal)}
            className={`btn btn-sm ${isUnassigned(trip) ? 'btn-primary' : 'border-ink-3/75 hover:border-ink-3'}`}
            onClick={onAssign}
          >
            {action}
          </button>
        )}
      </div>
      {open && (
        <section aria-labelledby={tripFormTitleId(trip.Id)} className="rounded-b-[9px] border-t-2 border-blue bg-blue-soft p-4">
          <p id={tripFormTitleId(trip.Id)} tabIndex={-1} className="text-data font-bold text-ink">
            {t.trip.actionOf(action, ordinal)}
          </p>
          {form}
        </section>
      )}
    </article>
  );
}

const NOTICE = {
  plain: 'border-ink-3 bg-muted text-ink',
  warning: 'border-warning-ink bg-warning-soft text-warning-ink',
} as const;

const Notice = ({
  tone,
  icon: Icon,
  title,
  children,
}: {
  tone: keyof typeof NOTICE;
  icon?: LucideIcon;
  title: ReactNode;
  children: ReactNode;
}) => (
  <div className={`flex items-start gap-2 rounded-sm border-l-4 px-3 py-2 text-meta ${NOTICE[tone]}`}>
    {Icon && <Icon className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />}
    <div className="min-w-0 flex-1 space-y-0.5">
      <div className="font-semibold">{title}</div>
      {children}
    </div>
  </div>
);

function Notices({ trip, master, now }: { trip: BookingTrip; master: Pick<MasterData, 'drivers' | 'vehicles'>; now: Date }) {
  const cancelled = trip.Status === 'Cancelled';
  const conflicts = agendaConflicts(trip, master.drivers, master.vehicles, now);
  if (!cancelled && !trip.IsLocked && !trip.ForcedDescription && conflicts.length === 0) return null;
  return (
    <div className="space-y-2">
      {cancelled && (
        <Notice tone="plain" icon={Ban} title={t.common.cancelled}>
          <p>{t.trip.notDone}</p>
        </Notice>
      )}
      {trip.IsLocked && !cancelled && (
        <Notice tone="plain" icon={Lock} title={t.common.locked}>
          <p>{t.trip.lockedNote}</p>
        </Notice>
      )}
      {trip.ForcedDescription && (
        <Notice tone="warning" title={<StatusBadge status="forced" />}>
          <p>{trip.ForcedDescription}</p>
        </Notice>
      )}
      {conflicts.length > 0 && (
        <Notice tone="warning" icon={CalendarX2} title={t.trip.inConflict}>
          <ul>
            {conflicts.map(conflict => (
              <li key={`${conflict.kind}${conflict.entry.Id}`}>{agendaConflictText(conflict)}</li>
            ))}
          </ul>
        </Notice>
      )}
    </div>
  );
}

const LABEL = 'flex items-center gap-1.5 text-xs font-medium text-ink-2';
const ITINERARY_ROW = 'grid grid-cols-[5.5rem_minmax(0,1fr)] items-baseline gap-x-3';

function Stop({
  icon: Icon,
  label,
  date,
  place,
  note,
}: {
  icon: LucideIcon;
  label: string;
  date: Date;
  place: string;
  note?: string | null;
}) {
  return (
    <div className={ITINERARY_ROW}>
      <dt className={LABEL}>
        <Icon className="h-4 w-4 shrink-0 text-ink-3" aria-hidden />
        {label}
      </dt>
      <dd className="grid grid-cols-[3.25rem_minmax(0,1fr)] items-baseline gap-x-2 text-ink">
        <time dateTime={date.toISOString()} className="text-module font-bold">
          {formatTime(date)}
        </time>
        <span className="text-data font-semibold">{place}</span>
        {note && <span className="col-start-2 text-meta font-semibold text-blue-ink">{note}</span>}
      </dd>
    </div>
  );
}

function Itinerary({ trip, locName }: { trip: BookingTrip; locName: (id: number) => string }) {
  const start = parseApiDate(trip.Start);
  const end = parseApiDate(trip.End);
  return (
    <dl className="space-y-1.5">
      <Stop icon={CircleDot} label={t.common.departure} date={start} place={locName(trip.OriginLocationId)} />
      <Stop icon={MapPin} label={t.common.arrival} date={end} place={locName(trip.DestinationLocationId)} note={arrivalDayText(start, end)} />
      <div className={ITINERARY_ROW}>
        <dt className={LABEL}>
          <Clock className="h-4 w-4 shrink-0 text-ink-3" aria-hidden />
          {t.common.duration}
        </dt>
        <dd className="text-meta text-ink">
          {durationText(start, end)}
          {clockChanges(start, end) && <span className="text-ink-2"> {t.trip.clockChange}</span>}
        </dd>
      </div>
    </dl>
  );
}

const Field = ({ label, children, className = '' }: { label: string; children: ReactNode; className?: string }) => (
  <div className={`min-w-0 space-y-0.5 ${className}`}>
    <dt className={LABEL}>{label}</dt>
    <dd className="text-data font-semibold text-ink">{children}</dd>
  </div>
);

/** Missing stands out only while it can still be solved. */
function Assigned({ value, missing, pending, icon }: { value: string | null; missing: string; pending: boolean; icon: ReactNode }) {
  if (value)
    return (
      <span className="flex min-w-0 items-center gap-2">
        {icon}
        <span className="min-w-0">{value}</span>
      </span>
    );
  return pending ? (
    <span className="inline-flex items-center gap-1.5 rounded-sm bg-critical-soft px-2 py-1 text-critical-ink">
      <AlertOctagon className="h-4 w-4 shrink-0" aria-hidden />
      {missing}
    </span>
  ) : (
    <span className="font-medium text-ink-2">{missing}</span>
  );
}

function TripData({ trip }: { trip: BookingTrip }) {
  const pending = isUnassigned(trip);
  return (
    <dl className="grid grid-cols-2 gap-x-4 gap-y-3 border-t border-line pt-4 sm:grid-cols-6">
      <Field label={t.common.driver} className="col-span-2 sm:col-span-3">
        <Assigned
          value={trip.AssignedDriverName ?? (trip.AssignedDriverId == null ? null : `#${trip.AssignedDriverId}`)}
          missing={t.common.noDriver}
          pending={pending}
          icon={
            trip.AssignedDriverName ? (
              <Avatar name={trip.AssignedDriverName} size="sm" />
            ) : (
              <User className="h-4 w-4 shrink-0 text-ink-3" aria-hidden />
            )
          }
        />
      </Field>
      <Field label={t.common.vehicle} className="col-span-2 sm:col-span-3">
        <Assigned
          value={trip.AssignedVehiclePlate ?? (trip.AssignedVehicleId == null ? null : `#${trip.AssignedVehicleId}`)}
          missing={t.common.noVehicle}
          pending={pending}
          icon={<BusFront className="h-4 w-4 shrink-0 text-ink-3" aria-hidden />}
        />
      </Field>
      <Field label={t.common.seats} className="sm:col-span-2">
        {trip.RequiredSeats}
      </Field>
      <Field label={t.common.type} className="sm:col-span-2">
        {serviceTypeLabel(trip.ServiceType)}
      </Field>
      <Field label={t.common.service} className="sm:col-span-2">
        #{trip.Id}
      </Field>
    </dl>
  );
}
