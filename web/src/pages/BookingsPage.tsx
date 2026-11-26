import { t } from '../i18n/t';
import {
  AlertOctagon,
  CalendarClock,
  ChevronLeft,
  ChevronRight,
  CircleCheck,
  CircleDashed,
  ClipboardList,
  Plus,
  Search,
  SearchX,
} from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { api } from '../api';
import type { PanelTarget } from '../BookingPanel';
import { bookingSummary } from '../booking';
import { isUnassigned, rangeLabel, rangeOf, shift } from '../calendar';
import { parseApiDate, TIME_ZONE } from '../gantt';
import { bookingStatus } from '../status';
import { Avatar, EmptyState, ErrorBox, PageHeader, Skeleton, Stat, StatusBadge, Toolbar } from '../ui';
import type { Booking } from '../types';
import { locale } from '../language';

const when = (instant: string | null) =>
  instant
    ? parseApiDate(instant).toLocaleString(locale(), {
        day: 'numeric',
        month: 'short',
        hour: '2-digit',
        minute: '2-digit',
        timeZone: TIME_ZONE,
      })
    : '—';

export function BookingsPage({ version, openPanel }: { version: number; openPanel: (target: PanelTarget) => void }) {
  const [anchor, setAnchor] = useState(() => new Date());
  const [bookings, setBookings] = useState<Booking[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState('');
  const [pendingOnly, setPendingOnly] = useState(false);
  const range = useMemo(() => rangeOf(anchor, 'month'), [anchor]);

  useEffect(() => {
    let live = true;
    setBookings(null);
    api
      .bookings(range.from, range.to)
      .then(loaded => {
        if (live) {
          setBookings(loaded);
          setError(null);
        }
      })
      .catch(e => live && setError((e as Error).message));
    return () => {
      live = false;
    };
  }, [range, version]);

  const rows = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase('es');
    return (bookings ?? [])
      .filter(booking => !needle || `${booking.CustomerName} ${booking.Description}`.toLocaleLowerCase('es').includes(needle))
      .filter(b => !status || b.AssignmentStatus === status)
      .filter(b => !pendingOnly || b.Trips.some(isUnassigned))
      .sort((a, b) => (a.Start ?? '').localeCompare(b.Start ?? ''));
  }, [bookings, query, status, pendingOnly]);

  const counts = useMemo(() => {
    const list = bookings ?? [];
    const by = (assignment: string) => list.filter(b => b.AssignmentStatus === assignment).length;
    return { total: list.length, full: by('FullyAssigned'), partial: by('PartiallyAssigned'), none: by('Unassigned') };
  }, [bookings]);
  const now = new Date();

  return (
    <div className="page">
      <PageHeader
        title={t.app.bookings}
        icon={ClipboardList}
        subtitle={t.bookings.intro}
      >
        <button type="button" className="btn btn-primary" onClick={() => openPanel({ create: true })}>
          <Plus className="h-4 w-4" aria-hidden />
          {t.create.newBooking}
        </button>
      </PageHeader>

      <div className="mb-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-4" role="group" aria-label={t.bookings.monthFigures}>
        {bookings === null && !error ? (
          [0, 1, 2, 3].map(index => (
            <div key={index} className="card h-[88px] p-4">
              <Skeleton className="h-full w-full" />
            </div>
          ))
        ) : (
          <>
            <Stat icon={CalendarClock} label={t.bookings.ofMonth} value={counts.total} foot={rangeLabel(range, 'month')} />
            <Stat icon={CircleCheck} tone="success" label={t.bookings.assigned} value={counts.full} foot={t.bookings.allCovered} />
            <Stat
              icon={CircleDashed}
              tone={counts.partial ? 'warning' : 'neutral'}
              label={t.bookings.partial}
              value={counts.partial}
              foot={t.bookings.someUncovered}
            />
            <Stat
              icon={AlertOctagon}
              tone={counts.none ? 'critical' : 'neutral'}
              label={t.bookings.uncovered}
              value={counts.none}
              foot={t.bookings.noneCovered}
            />
          </>
        )}
      </div>

      <Toolbar>
        <button
          type="button"
          className="btn btn-text btn-icon"
          aria-label={t.bookings.previousMonth}
          onClick={() => setAnchor(shift(anchor, 'month', -1))}
        >
          <ChevronLeft className="h-4 w-4" aria-hidden />
        </button>
        <button
          type="button"
          className="btn btn-text btn-icon"
          aria-label={t.bookings.nextMonth}
          onClick={() => setAnchor(shift(anchor, 'month', 1))}
        >
          <ChevronRight className="h-4 w-4" aria-hidden />
        </button>
        <h2 className="mr-auto pl-1 text-module font-semibold first-letter:uppercase">{rangeLabel(range, 'month')}</h2>
        <label className="relative">
          <span className="sr-only">{t.bookings.search}</span>
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-3" aria-hidden />
          <input
            type="search"
            className="input w-64 pl-9"
            placeholder={t.bookings.searchWhat}
            value={query}
            onChange={e => setQuery(e.target.value)}
          />
        </label>
        <label>
          <span className="sr-only">{t.common.state}</span>
          <select className="input" value={status} onChange={e => setStatus(e.target.value)}>
            <option value="">{t.bookings.allStates}</option>
            <option value="FullyAssigned">{t.common.assigned}</option>
            <option value="PartiallyAssigned">{t.common.partial}</option>
            <option value="Unassigned">{t.bookings.uncoveredOne}</option>
          </select>
        </label>
        <label className="flex h-9 items-center gap-2 rounded-md px-2 text-meta text-ink-2 hover:bg-sunken">
          <input
            type="checkbox"
            className="h-4 w-4 accent-[rgb(var(--blue))]"
            checked={pendingOnly}
            onChange={e => setPendingOnly(e.target.checked)}
          />
          {t.bookings.withUnassigned}
        </label>
      </Toolbar>

      {error && (
        <div className="mb-4">
          <ErrorBox>{t.bookings.failed(error)}</ErrorBox>
        </div>
      )}
      <div className="card overflow-x-auto">
        <table className="tbl min-w-[760px]" aria-label={t.app.bookings}>
          <thead>
            <tr>
              <th scope="col">{t.common.customer}</th>
              <th scope="col">{t.common.period}</th>
              <th scope="col">{t.bookings.covered}</th>
              <th scope="col">{t.common.state}</th>
            </tr>
          </thead>
          <tbody>
            {bookings === null &&
              !error &&
              [0, 1, 2, 3, 4].map(index => (
                <tr key={index}>
                  <td colSpan={4}>
                    <div role="status" className="flex items-center gap-3">
                      <span className="sr-only">{t.bookings.loading}</span>
                      <Skeleton className="h-9 w-9 rounded-full" />
                      <Skeleton className="h-4 flex-1" />
                    </div>
                  </td>
                </tr>
              ))}
            {bookings !== null && rows.length === 0 && (
              <tr>
                <td colSpan={4}>
                  <EmptyState icon={SearchX} title={t.bookings.noneMatches}>
                    {t.bookings.tryOther}
                  </EmptyState>
                </td>
              </tr>
            )}
            {rows.map(booking => {
              const badge = bookingStatus(booking.AssignmentStatus, booking.Start, now);
              const summary = bookingSummary(booking.Trips);
              const pct = summary.active ? (summary.covered / summary.active) * 100 : 0;
              return (
                <tr key={booking.Id} className="cursor-pointer" onClick={() => openPanel({ bookingId: booking.Id })}>
                  <td>
                    <div className="flex min-w-0 items-center gap-3">
                      <Avatar name={booking.CustomerName} toneKey={booking.CustomerName} />
                      <div className="min-w-0">
                        <button
                          type="button"
                          className="max-w-full truncate text-left font-semibold text-ink hover:text-blue-ink hover:underline"
                          onClick={event => {
                            event.stopPropagation();
                            openPanel({ bookingId: booking.Id });
                          }}
                        >
                          {booking.CustomerName}
                        </button>
                        <p
                          className="max-w-[420px] truncate text-xs text-ink-2 2xl:max-w-[560px] 3xl:max-w-[720px]"
                          title={booking.Description}
                        >
                          {booking.Description}
                        </p>
                      </div>
                    </div>
                  </td>
                  <td className="whitespace-nowrap text-meta text-ink">
                    {when(booking.Start)} <span className="text-ink-3">–</span> {when(booking.End)}
                  </td>
                  <td>
                    <div className="flex items-center gap-3">
                      <span className="w-12 shrink-0 text-meta font-semibold text-ink">
                        {summary.covered}/{summary.active}
                      </span>
                      <span className="h-1.5 w-24 overflow-hidden rounded-full bg-muted" aria-hidden>
                        <span
                          className={`block h-full rounded-full ${pct === 100 ? 'bg-success' : pct > 0 ? 'bg-warning' : 'bg-critical'}`}
                          style={{ width: `${Math.max(pct, summary.active ? 4 : 0)}%` }}
                        />
                      </span>
                    </div>
                  </td>
                  <td>{badge ? <StatusBadge status={badge} /> : booking.AssignmentStatus}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
