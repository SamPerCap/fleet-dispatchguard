import { t } from './i18n/t';
import { BusFront, CalendarOff, ShieldOff } from 'lucide-react';
import { spanIn, type Range, type Scale } from './calendar';
import { InfoButton } from './ExplanationDialog';
import { formatTime, parseApiDate, startOfDay, TIME_ZONE } from './gantt';
import { agendaConflictText, unavailabilityText, UNAVAILABILITY_LABEL, type AgendaConflict } from './resources';
import { STATUS_TEXT, tripStatus, type StatusKey } from './status';
import type { BookingTrip, Explanation, Unavailability } from './types';
import { Avatar, BarLegend, ConflictMark, EmptyState } from './ui';
import { locale } from './language';

export interface BarExplanation {
  label: string;
  explanation: Explanation;
}
export interface TimelineTrip extends BookingTrip {
  bookingId: number;
  customer: string;
}
export interface TimelineRow {
  key: string;
  label: string;
  sub?: string;
  kind?: 'driver' | 'vehicle';
  trips: TimelineTrip[];
  absences: Unavailability[];
}

interface Props {
  rows: TimelineRow[];
  range: Range;
  scale: Scale;
  describe: (trip: TimelineTrip) => string;
  onOpen: (trip: TimelineTrip) => void;
  explain?: (trip: TimelineTrip) => BarExplanation | undefined;
  /** By trip: of its driver or its vehicle, whichever the rows are. */
  conflicts: ReadonlyMap<number, AgendaConflict[]>;
}

const STATUS_CLASS: Record<StatusKey, string> = {
  assigned: 'bg-navy-tint border-transparent text-ink',
  running: 'bg-navy border-navy text-white',
  completed: 'bg-muted border-transparent text-ink-3',
  uncovered: 'bg-critical-soft border-dashed border-critical text-critical-ink',
  planned: 'bg-sunken border-dashed border-ink-3/50 text-ink-2',
  forced: 'bg-navy-tint border-transparent text-ink',
  partial: 'bg-navy-tint border-transparent text-ink',
};

export function Timeline({ rows, range, scale, describe, onOpen, explain, conflicts }: Props) {
  const now = new Date();
  const span = range.to.getTime() - range.from.getTime();
  const nowLeft = ((now.getTime() - range.from.getTime()) / span) * 100;
  const today = startOfDay(now).getTime();
  const ticks =
    scale === 'day'
      ? Array.from({ length: 12 }, (_, index) => ({
          key: index,
          left: ((index * 2) / 24) * 100,
          text: `${String(index * 2).padStart(2, '0')}:00`,
          today: false,
          weekend: false,
        }))
      : range.days.map((day, index) => ({
          key: index,
          left: (index / range.days.length) * 100,
          today: day.getTime() === today,
          weekend: day.getDay() === 0 || day.getDay() === 6,
          text: day.toLocaleDateString(
            locale(),
            scale === 'week' ? { weekday: 'short', day: 'numeric', timeZone: TIME_ZONE } : { day: 'numeric', timeZone: TIME_ZONE },
          ),
        }));
  const colWidth = scale === 'day' ? 0 : 100 / range.days.length;
  const busy = rows.filter(r => r.trips.length > 0).length;

  return (
    <div className="card overflow-hidden">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 border-b border-line px-4 py-3 text-xs text-ink-2">
        <span className="font-medium text-ink">
          {t.timeline.rows(rows.length, rows[0]?.kind === 'vehicle' ? t.common.vehiclesLower : t.common.driversLower, busy)}
        </span>
        <span className="flex-1" />
        <BarLegend nowText="Ahora" agenda />
      </div>
      <div className="max-h-[70vh] overflow-auto">
        <table className="w-full min-w-[760px] border-separate border-spacing-0" aria-label={t.timeline.title}>
          <thead>
            <tr>
              <th
                scope="col"
                className="sticky left-0 top-0 z-30 w-56 border-b border-line bg-surface px-4 py-2 text-left text-xs font-medium text-ink-3"
              >
                {t.timeline.resource}
              </th>
              <th scope="col" className="sticky top-0 z-20 h-10 border-b border-line bg-surface p-0">
                <div className="relative h-full">
                  {ticks.map(tick => (
                    <span
                      key={tick.key}
                      className={`absolute top-1/2 -translate-y-1/2 whitespace-nowrap rounded-full px-1.5 text-xs capitalize ${tick.today ? 'bg-blue font-semibold text-white' : 'font-normal text-ink-3'}`}
                      style={{ left: `calc(${tick.left}% + 4px)` }}
                    >
                      {tick.text}
                    </span>
                  ))}
                </div>
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map(row => (
              <tr key={row.key} className="group">
                <th
                  scope="row"
                  className="sticky left-0 z-10 border-b border-line bg-surface px-4 py-2 text-left align-middle font-normal group-hover:bg-sunken"
                >
                  <div className="flex items-center gap-2.5">
                    {row.kind === 'vehicle' ? (
                      <span aria-hidden className="grid h-8 w-8 shrink-0 place-items-center rounded-full bg-muted text-ink-2">
                        <BusFront className="h-4 w-4" />
                      </span>
                    ) : (
                      <Avatar name={row.label} size="sm" />
                    )}
                    <div className="min-w-0">
                      <div className="truncate font-medium text-ink" title={row.label}>
                        {row.label}
                      </div>
                      {row.sub && (
                        <div className="truncate text-xs text-ink-3" title={row.sub}>
                          {row.sub}
                        </div>
                      )}
                      <RowConflicts count={row.trips.filter(trip => conflicts.get(trip.Id)?.length).length} />
                    </div>
                  </div>
                </th>
                <td className="relative h-14 border-b border-line p-0 group-hover:bg-sunken/60">
                  {scale !== 'day' &&
                    ticks.map(
                      tick =>
                        (tick.weekend || tick.today) && (
                          <span
                            key={`bg${tick.key}`}
                            aria-hidden
                            className={`absolute inset-y-0 ${tick.today ? 'bg-blue-soft/60' : 'bg-sunken'}`}
                            style={{ left: `${tick.left}%`, width: `${colWidth}%` }}
                          />
                        ),
                    )}
                  {ticks.map(tick => (
                    <span
                      key={tick.key}
                      aria-hidden
                      className="absolute inset-y-0 border-l border-line/70"
                      style={{ left: `${tick.left}%` }}
                    />
                  ))}
                  {nowLeft >= 0 && nowLeft <= 100 && (
                    <span
                      aria-hidden
                      className="pointer-events-none absolute inset-y-0 z-[5] w-px bg-blue"
                      style={{ left: `${nowLeft}%` }}
                    />
                  )}
                  {row.absences.map(absence => (
                    <AbsenceBand key={absence.Id} absence={absence} range={range} />
                  ))}
                  {row.trips.map(trip => (
                    <TripBar
                      key={trip.Id}
                      trip={trip}
                      range={range}
                      scale={scale}
                      now={now}
                      description={describe(trip)}
                      conflicts={conflicts.get(trip.Id) ?? []}
                      info={explain?.(trip)}
                      onOpen={() => onOpen(trip)}
                    />
                  ))}
                </td>
              </tr>
            ))}
            {rows.length === 0 && (
              <tr>
                <td colSpan={2}>
                  <EmptyState icon={CalendarOff} title={t.timeline.noResources}>
                    {t.timeline.addResources}
                  </EmptyState>
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function RowConflicts({ count }: { count: number }) {
  if (count === 0) return null;
  return (
    <div className="mt-0.5 flex items-center gap-1 text-xs font-medium text-warning-ink">
      <ConflictMark />
      {t.timeline.inConflict(count)}
    </div>
  );
}

function TripBar({
  trip,
  range,
  scale,
  now,
  description,
  conflicts,
  info,
  onOpen,
}: {
  trip: TimelineTrip;
  range: Range;
  scale: Scale;
  now: Date;
  description: string;
  conflicts: AgendaConflict[];
  info?: BarExplanation;
  onOpen: () => void;
}) {
  const start = parseApiDate(trip.Start);
  const end = parseApiDate(trip.End);
  const position = spanIn(start, end, range);
  if (!position) return null;
  const status = tripStatus(trip, now) ?? 'assigned';
  const title = [
    `${description} · ${formatTime(start)}–${formatTime(end)} · ${STATUS_TEXT[status].label}`,
    ...(trip.ForcedDescription ? [trip.ForcedDescription] : []),
    ...conflicts.map(conflict => t.timeline.conflictWith(agendaConflictText(conflict))),
  ].join(' · ');
  return (
    <>
      <div
        className={`bar absolute top-2.5 z-[6] flex h-9 items-center overflow-hidden rounded-md border text-xs font-medium shadow-[0_1px_2px_rgb(16_27_48/0.08)] transition-shadow hover:shadow-[0_0_0_2px_rgb(var(--blue))] ${STATUS_CLASS[status]}${trip.ForcedDescription ? ' border-l-4 border-l-warning' : ''}${conflicts.length ? ' outline outline-2 outline-warning' : ''}`}
        style={{ left: `${position.left}%`, width: `max(${position.width}%, 6px)` }}
        title={title}
      >
        <button
          type="button"
          className="h-full min-w-0 flex-1 truncate px-2 text-left"
          aria-label={t.timeline.openBooking(title, trip.customer)}
          onClick={onOpen}
        >
          {scale === 'day' ? `${formatTime(start)} ${description}` : ''}
        </button>
        {info && <InfoButton explanation={info.explanation} label={info.label} />}
      </div>
      {conflicts.length > 0 && (
        // Outside the bar, which clips its content and may be only a few pixels wide.
        <span className="pointer-events-none absolute top-0.5 z-[7]" style={{ left: `${position.left}%` }}>
          <ConflictMark />
        </span>
      )}
    </>
  );
}

function AbsenceBand({ absence, range }: { absence: Unavailability; range: Range }) {
  const position = spanIn(parseApiDate(absence.Start), parseApiDate(absence.End), range);
  if (!position) return null;
  const text = unavailabilityText(absence);
  return (
    <span
      role="img"
      aria-label={text}
      title={text}
      className="agenda-band absolute inset-y-0.5 z-[4] flex items-end overflow-hidden rounded-[4px] px-1 text-xs font-medium leading-4 text-warning-ink"
      style={{ left: `${position.left}%`, width: `max(${position.width}%, 4px)` }}
    >
      <ShieldOff className="mb-0.5 mr-1 h-3 w-3 shrink-0" aria-hidden />
      <span className="truncate">{UNAVAILABILITY_LABEL[absence.Type] ?? absence.Type}</span>
    </span>
  );
}
