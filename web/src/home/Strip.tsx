import { t } from '../i18n/t';
import { BusFront, CalendarClock, CalendarOff } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import {
  type Fresh,
  STRIP_MAX_HOURS,
  STRIP_MIN_HOURS,
  stripBlocks,
  stripRows,
  stripTicks,
  stripWindow,
  type StripBlock,
  type StripMode,
} from '../control';
import { formatTime, parseApiDate } from '../gantt';
import { STATUS_TEXT } from '../status';
import type { Trip } from '../types';
import { Avatar, BarLegend, EmptyState, ErrorState, Module, SkeletonLines, StatusBadge } from '../ui';
import { staleOf } from './data';

const BLK: Record<StripBlock['status'], string> = {
  assigned: 'blk blk-assigned',
  running: 'blk blk-running',
  completed: 'blk blk-completed',
  uncovered: 'blk blk-uncovered',
  planned: 'blk blk-uncovered',
  forced: 'blk blk-assigned',
  partial: 'blk blk-assigned',
};
const MODES: { id: StripMode; label: string }[] = [
  { id: 'driver', get label() { return t.common.driver; } },
  { id: 'vehicle', get label() { return t.common.vehicle; } },
  { id: 'service', get label() { return t.common.service; } },
];

export function StripModule({
  trips,
  now,
  hours,
  onHours,
  mode,
  onMode,
  driverName,
  plate,
  onOpen,
  onRetry,
}: {
  trips?: Fresh<Trip[]>;
  now: Date;
  hours: number;
  onHours: (hours: number) => void;
  mode: StripMode;
  onMode: (mode: StripMode) => void;
  driverName: (id: number) => string;
  plate: (id: number) => string;
  onOpen: (trip: Trip) => void;
  onRetry: () => void;
}) {
  const { from, to } = stripWindow(now, hours);
  const list = trips?.ok ? trips.value : null;
  const rows = useMemo(
    () => (list ? stripRows(stripBlocks(list, from, to, now), mode, mode === 'driver' ? driverName : plate) : []),
    [list, from.getTime(), to.getTime(), now.getTime(), mode, driverName, plate], // eslint-disable-line react-hooks/exhaustive-deps
  );
  const [hover, setHover] = useState<{ row: string; block: StripBlock; rect: DOMRect } | null>(null);
  useEffect(() => {
    setHover(null);
  }, [rows]); // With another mode, other hours or new data, the highlighted block is no longer the same one.
  // After scrolling, the fixed tooltip would no longer be next to its block.
  useEffect(() => {
    if (!hover) return;
    const close = () => setHover(null);
    addEventListener('scroll', close, true);
    return () => removeEventListener('scroll', close, true);
  }, [hover]);
  const marks = stripTicks(hours);
  const tickTime = (hour: number) => formatTime(new Date(from.getTime() + hour * 3_600_000));
  const nowLeft = ((now.getTime() - from.getTime()) / (to.getTime() - from.getTime())) * 100;
  const uncovered = rows.reduce((sum, row) => sum + row.blocks.filter(b => b.status === 'uncovered').length, 0);

  const who = (trip: Trip) => ({
    driver: trip.AssignedDriverId == null ? t.common.noDriver : driverName(trip.AssignedDriverId),
    vehicle: trip.AssignedVehicleId == null ? t.common.noVehicle : plate(trip.AssignedVehicleId),
  });
  const blockText = (block: StripBlock) => {
    const assigned = who(block.trip),
      name = block.trip.Description ?? t.common.serviceOf(block.trip.Id);
    if (mode === 'driver') return [name, assigned.vehicle];
    if (mode === 'vehicle') return [name, assigned.driver];
    return [assigned.driver, assigned.vehicle];
  };

  return (
    <Module
      title={t.strip.nextHours(hours)}
      label={t.strip.title}
      icon={CalendarClock}
      stale={staleOf(trips, now)}
      sub={
        <span className="text-meta text-ink-2">
          {tickTime(0)}–{tickTime(hours)}
          {uncovered > 0 && (
            <>
              {' '}
              · <b className="font-semibold text-critical-ink">{t.strip.uncovered(uncovered)}</b>
            </>
          )}
        </span>
      }
      actions={
        <div className="flex flex-wrap items-center gap-4">
          <div className="flex items-center gap-2">
            <span className="text-meta text-ink-2" id="strip-rows">
              {t.common.rows}
            </span>
            <div className="seg" role="group" aria-labelledby="strip-rows">
              {MODES.map(option => (
                <button key={option.id} type="button" aria-pressed={mode === option.id} onClick={() => onMode(option.id)}>
                  {option.label}
                </button>
              ))}
            </div>
          </div>
          <label className="flex items-center gap-2 text-meta text-ink-2">
            {t.strip.hours}
            <input
              type="range"
              min={STRIP_MIN_HOURS}
              max={STRIP_MAX_HOURS}
              step={1}
              value={hours}
              aria-label={t.strip.hoursOfStrip}
              aria-valuetext={`${hours} h`}
              className="h-11 w-32 accent-[rgb(var(--blue))]"
              onChange={e => onHours(Number(e.target.value))}
            />
            <span className="w-9 text-right font-medium text-ink">{hours} h</span>
          </label>
        </div>
      }
    >
      {!trips && <SkeletonLines lines={5} label={t.strip.loading} />}
      {trips && !trips.ok && (
        <ErrorState title={t.strip.failed} onRetry={onRetry}>
          {trips.error}
        </ErrorState>
      )}
      {list && rows.length === 0 && (
        <EmptyState icon={CalendarOff} title={t.strip.none}>
          {t.strip.widen}
        </EmptyState>
      )}
      {list && rows.length > 0 && (
        <div
          className="max-h-[460px] overflow-y-auto 3xl:max-h-[560px] overflow-x-hidden"
          role="region"
          aria-label={t.strip.services}
          tabIndex={0}
        >
          <div className="grid grid-cols-[200px_minmax(0,1fr)] 3xl:grid-cols-[240px_minmax(0,1fr)]">
            <div className="sticky top-0 z-20 border-b border-line bg-surface" />
            <div className="sticky top-0 z-20 h-14 border-b border-line bg-surface">
              {/* "Now" goes in its own row, above the hours: it never covers a label. */}
              <div className="relative h-full text-xs text-ink-3" aria-hidden>
                {nowLeft >= 0 && nowLeft <= 100 && (
                  <span
                    className="absolute top-1 z-10 -translate-x-1/2 whitespace-nowrap rounded-full bg-blue px-2 py-0.5 text-xs font-semibold text-white"
                    style={{ left: `clamp(44px, ${nowLeft}%, calc(100% - 44px))` }}
                  >
                    {t.strip.nowAt(formatTime(now))}
                  </span>
                )}
                {marks.map((hour, index) => (
                  <span
                    key={hour}
                    className={`absolute bottom-1.5 ${index === 0 ? '' : index === marks.length - 1 ? '-translate-x-full' : '-translate-x-1/2'}`}
                    style={{ left: `${(hour / hours) * 100}%` }}
                  >
                    {tickTime(hour)}
                  </span>
                ))}
              </div>
            </div>
            {rows.map(row => (
              <div key={row.key} className="contents">
                <div
                  className={`flex h-10 min-w-0 items-center gap-2 pr-3 text-meta ${row.label ? 'border-t border-line' : ''} ${row.missing ? 'font-semibold text-critical-ink' : 'text-ink'}`}
                >
                  {row.label && !row.missing && mode === 'driver' && <Avatar name={row.label} size="sm" />}
                  {row.label && !row.missing && mode === 'vehicle' && (
                    <span aria-hidden className="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-muted text-ink-2">
                      <BusFront className="h-4 w-4" />
                    </span>
                  )}
                  <span className="truncate" title={row.label}>
                    {row.label}
                  </span>
                  {row.sub && <span className="ml-2 shrink-0 text-xs text-ink-3">{row.sub}</span>}
                </div>
                <div className={`relative h-10 ${row.label ? 'border-t border-line' : ''}`}>
                  {marks.map(hour => (
                    <span
                      key={hour}
                      aria-hidden
                      className="pointer-events-none absolute inset-y-0 w-px bg-line/70"
                      style={{ left: `${(hour / hours) * 100}%` }}
                    />
                  ))}
                  {row.blocks.map(block => {
                    const trip = block.trip,
                      [main, second] = blockText(block);
                    const time = `${formatTime(parseApiDate(trip.Start))}–${formatTime(parseApiDate(trip.End))}`;
                    const shown = hover?.row === row.key && hover.block.trip.Id === trip.Id;
                    const label = `${trip.Description ?? t.common.serviceOf(trip.Id)}, ${time}, ${who(trip).driver}, ${who(trip).vehicle}, ${STATUS_TEXT[block.status].label}${block.forced ? ', forzado' : ''}`;
                    return (
                      <button
                        key={trip.Id}
                        type="button"
                        aria-label={t.strip.openLabel(label)}
                        aria-describedby={shown ? `tip-${row.key}-${trip.Id}` : undefined}
                        className={`${BLK[block.status]}${block.forced ? ' blk-forced' : ''}${block.clipStart ? ' blk-clip-start' : ''}${block.clipEnd ? ' blk-clip-end' : ''}`}
                        style={{ left: `${block.left}%`, width: `max(${block.width}%, 6px)` }}
                        onMouseEnter={event => setHover({ row: row.key, block, rect: event.currentTarget.getBoundingClientRect() })}
                        onMouseLeave={() => setHover(null)}
                        onFocus={event => setHover({ row: row.key, block, rect: event.currentTarget.getBoundingClientRect() })}
                        onBlur={() => setHover(null)}
                        onKeyDown={event => {
                          if (event.key === 'Escape') setHover(null);
                        }}
                        onClick={() => onOpen(trip)}
                      >
                        <span className="truncate">{main}</span>
                        {second && block.width >= 18 && (
                          <span className={`truncate font-normal ${block.status === 'running' ? 'text-white/80' : 'opacity-80'}`}>
                            {second}
                          </span>
                        )}
                      </button>
                    );
                  })}
                  {nowLeft >= 0 && nowLeft <= 100 && (
                    <span
                      aria-hidden
                      className="pointer-events-none absolute inset-y-0 z-10 w-px bg-blue shadow-[0_0_0_1px_rgb(255_255_255/0.85)]"
                      style={{ left: `${nowLeft}%` }}
                    />
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
      {hover && (
        <Tooltip id={`tip-${hover.row}-${hover.block.trip.Id}`} block={hover.block} rect={hover.rect} who={who(hover.block.trip)} />
      )}
      {list && (
        <div className="mt-4">
          <BarLegend nowText={t.strip.nowIn(formatTime(now))} />
        </div>
      )}
    </Module>
  );
}

const TIP_W = 288,
  TIP_H = 190;

/** Fixed to the window (the strip scroll does not clip it); it flips up or left if it does not fit. */
function Tooltip({ id, block, rect, who }: { id: string; block: StripBlock; rect: DOMRect; who: { driver: string; vehicle: string } }) {
  const trip = block.trip;
  const left = Math.min(Math.max(8, rect.left), window.innerWidth - TIP_W - 8);
  const below = rect.bottom + 6 + TIP_H < window.innerHeight;
  return (
    <div
      id={id}
      role="tooltip"
      className="pointer-events-none fixed z-50 w-72 rounded-md bg-surface p-3 text-meta shadow-pop"
      style={below ? { left, top: rect.bottom + 6 } : { left, bottom: window.innerHeight - rect.top + 6 }}
    >
      <p className="truncate font-semibold text-ink">{trip.Description ?? t.common.serviceOf(trip.Id)}</p>
      <p className="text-ink-2">
        {t.strip.timesAndSeats(formatTime(parseApiDate(trip.Start)), formatTime(parseApiDate(trip.End)), trip.RequiredSeats)}
      </p>
      <dl className="mt-2 grid grid-cols-[72px_1fr] gap-x-2 gap-y-0.5">
        <dt className="text-ink-3">{t.common.driver}</dt>
        <dd className={trip.AssignedDriverId == null ? 'font-medium text-critical-ink' : 'text-ink'}>{who.driver}</dd>
        <dt className="text-ink-3">{t.common.vehicle}</dt>
        <dd className={trip.AssignedVehicleId == null ? 'font-medium text-critical-ink' : 'text-ink'}>{who.vehicle}</dd>
      </dl>
      <div className="mt-2 flex flex-wrap gap-1.5">
        <StatusBadge status={block.status} />
        {block.forced && <StatusBadge status="forced" />}
      </div>
      <p className="mt-2 text-xs text-ink-3">{t.strip.pressToOpen}</p>
    </div>
  );
}
