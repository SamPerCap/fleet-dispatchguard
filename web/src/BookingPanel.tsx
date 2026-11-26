import { t } from './i18n/t';
import { AlertOctagon, CalendarDays, ChevronDown, CircleCheck, Wand2 } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api } from './api';
import type { MasterData } from './App';
import { AssignForm } from './AssignForm';
import {
  bookingSummary,
  coverage,
  dateRangeText,
  longDayText,
  pendingNote,
  tripDays,
  type BookingSummary,
  type CoveragePart,
  type NumberedTrip,
} from './booking';
import { CreateBooking } from './CreateBooking';
import { InfoButton } from './ExplanationDialog';
import { parseApiDate } from './gantt';
import { createLatestGuard } from './latest';
import { prefersReducedMotion } from './motion';
import { isRecommendable } from './recommend';
import { RecommendPanel } from './RecommendPanel';
import { bookingStatus, STATUS_TEXT, type StatusKey } from './status';
import type { BarExplanation } from './Timeline';
import { canAssign, TripBlock, tripActionId, tripBlockId, tripFormTitleId, tripTitleId } from './TripBlock';
import type { Booking } from './types';
import { Avatar, ErrorBox, Sheet, SkeletonLines, StatusBadge } from './ui';
import type { BusProposal } from './vehicleChange';

/** `proposal`: the "Ahorro de km" report proposes that bus for `tripId` (SPEC 0072). */
export type PanelTarget = { bookingId: number; tripId?: number; proposal?: BusProposal } | { create: true };

interface Props {
  target: PanelTarget | null;
  onClose: () => void;
  master: MasterData;
  onChanged: () => void;
  onAllocated: (tripId: number, info: BarExplanation) => void;
  explained?: Map<number, BarExplanation>;
}

type HeadingLevel = 'h4' | 'h5';

const NO_FORMS: ReadonlySet<number> = new Set();

function focusFirst(ids: string[]) {
  const element = ids.map(id => document.getElementById(id)).find(found => found != null);
  element?.focus({ preventScroll: true });
  return element;
}

/** In the next frame, once what has just opened is drawn. */
function showTrip(tripId: number, smooth: boolean) {
  requestAnimationFrame(() => {
    document
      .getElementById(tripBlockId(tripId))
      ?.scrollIntoView({ behavior: smooth && !prefersReducedMotion() ? 'smooth' : 'auto', block: 'start' });
    focusFirst([tripFormTitleId(tripId), tripTitleId(tripId)]);
  });
}

export function BookingPanel({ target, onClose, master, onChanged, onAllocated, explained }: Props) {
  const creating = target != null && 'create' in target;
  const opened = target && 'bookingId' in target ? target : null;
  const bookingId = opened?.bookingId ?? null;
  const markedId = opened?.tripId ?? null;
  const proposal = opened?.proposal ?? null;
  const [booking, setBooking] = useState<Booking | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Several at once: opening one never discards what is half done in another.
  const [forms, setForms] = useState(NO_FORMS);
  const [recommending, setRecommending] = useState(false);
  const [saved, setSaved] = useState(false); // After a stored change, the report the proposal came from is no longer valid (SPEC 0072).
  const guard = useRef(createLatestGuard()).current;

  // If another booking opens before the previous one arrives, the stale response is discarded.
  const load = useCallback(
    async (id: number, openTripId: number | null = null) => {
      const current = guard.begin();
      try {
        const loaded = await api.booking(id);
        if (!current()) return;
        setBooking(loaded);
        setError(null);
        const opening = loaded.Trips.find(trip => trip.Id === openTripId);
        if (opening && canAssign(opening)) setForms(new Set([opening.Id]));
      } catch (cause) {
        if (current()) setError((cause as Error).message);
      }
    },
    [guard],
  );
  useEffect(() => {
    setBooking(null);
    setError(null);
    setRecommending(false);
    setSaved(false);
    setForms(NO_FORMS);
    if (bookingId != null) void load(bookingId, markedId);
    else guard.begin(); // Invalidates any load in progress when closing or creating.
  }, [target, bookingId, markedId, load, guard]);

  // Opened from a specific trip (strip, alert, timeline): go straight to it.
  const loadedId = booking?.Id;
  useEffect(() => {
    if (loadedId != null && loadedId === bookingId && markedId != null) showTrip(markedId, false);
  }, [loadedId, bookingId, markedId, target]);

  const openForm = (tripId: number) => {
    setForms(open => new Set(open).add(tripId));
    showTrip(tripId, true);
  };
  const closeForm = (tripId: number) => {
    setForms(open => new Set([...open].filter(id => id !== tripId)));
    // Without the form, the trip may be left above what is seen.
    requestAnimationFrame(() => focusFirst([tripActionId(tripId), tripTitleId(tripId)])?.scrollIntoView({ block: 'nearest' }));
  };
  // The booking open now, not the one when the operation started: closing halfway through applying may leave a different one.
  const openId = useRef(bookingId);
  openId.current = bookingId;
  const allocated = (tripId: number, info: BarExplanation) => {
    setSaved(true);
    onAllocated(tripId, info);
  };
  const refresh = () => {
    if (openId.current != null) void load(openId.current);
    onChanged();
  };
  const title = creating ? t.create.newBooking : booking ? t.bookingPanel.title(booking.CustomerName) : t.common.booking;
  const summary = useMemo(() => (booking ? bookingSummary(booking.Trips) : null), [booking]);
  const now = new Date();

  const block = (item: NumberedTrip, headingLevel: HeadingLevel) => {
    const { trip } = item;
    const fromReport = proposal != null && markedId === trip.Id;
    return (
      <TripBlock
        item={item}
        master={master}
        now={now}
        headingLevel={headingLevel}
        marked={markedId === trip.Id}
        onAssign={() => openForm(trip.Id)}
        form={
          booking && forms.has(trip.Id) ? (
            <AssignForm
              trip={trip}
              booking={booking}
              fleet={master.vehicles}
              onAllocated={allocated}
              onRefresh={refresh}
              keepDriver={fromReport}
              proposal={fromReport && !saved ? proposal : null}
              onClose={() => closeForm(trip.Id)}
            />
          ) : null
        }
      />
    );
  };

  return (
    <Sheet open={target != null} onClose={onClose} title={title} wide flush>
      <div className="px-6 py-4">
        {creating && (
          <CreateBooking
            master={master}
            onCreated={created => {
              onChanged();
              setBooking(created);
              onClose();
            }}
          />
        )}
        {error && <ErrorBox>{t.bookingPanel.failed(error)}</ErrorBox>}
        {!creating && !booking && !error && <SkeletonLines lines={6} label={t.bookingPanel.loading} />}
        {booking && summary && (
          <div className="space-y-6">
            <BookingHeader booking={booking} summary={summary} />
            <section aria-label={t.bookingPanel.actions}>
              {recommending ? (
                <RecommendPanel
                  request={{ BookingId: booking.Id }}
                  onApplied={refresh}
                  onAllocated={allocated}
                  onClose={() => setRecommending(false)}
                />
              ) : (
                <BookingActions
                  summary={summary}
                  recommendable={booking.Trips.filter(trip => isRecommendable(trip, now)).length}
                  onAssign={openForm}
                  onRecommend={() => setRecommending(true)}
                />
              )}
            </section>
            <TripList booking={booking} block={block} />
            <BookingExplanation booking={booking} summary={summary} explained={explained} />
          </div>
        )}
      </div>
    </Sheet>
  );
}

const statusOf = (booking: Booking): StatusKey | null => bookingStatus(booking.AssignmentStatus, booking.Start, new Date());

const COVERAGE_TONE: Record<CoveragePart['tone'], string> = {
  neutral: 'text-ink',
  covered: 'text-success-ink',
  missing: 'text-critical-ink',
};
const COVERAGE_ICON = { neutral: null, covered: CircleCheck, missing: AlertOctagon } as const;

function BookingHeader({ booking, summary }: { booking: Booking; summary: BookingSummary }) {
  const status = statusOf(booking);
  return (
    <header className="flex items-start gap-4 rounded-md border border-line bg-sunken p-4">
      <Avatar name={booking.CustomerName} toneKey={booking.CustomerName} />
      <div className="min-w-0 flex-1 space-y-1.5 [overflow-wrap:anywhere]">
        <p className="flex flex-wrap items-center gap-2 text-meta text-ink-2">
          {status && <StatusBadge status={status} />}
          {t.common.bookingId(booking.Id)}
        </p>
        <p className="flex items-center gap-1.5 text-data font-semibold text-ink">
          <CalendarDays className="h-4 w-4 shrink-0 text-ink-3" aria-hidden />
          {booking.Start && booking.End ? dateRangeText(parseApiDate(booking.Start), parseApiDate(booking.End)) : t.bookingPanel.noDates}
        </p>
        {booking.Description && <p className="text-meta text-ink-2">{booking.Description}</p>}
        <ul aria-label={t.bookingPanel.coverage} className="flex flex-wrap gap-x-4 gap-y-1 border-t border-line pt-2 text-meta font-semibold">
          {coverage(summary).map(part => {
            const Icon = COVERAGE_ICON[part.tone];
            return (
              <li key={part.text} className={`inline-flex items-center gap-1 ${COVERAGE_TONE[part.tone]}`}>
                {Icon && <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden />}
                {part.text}
              </li>
            );
          })}
        </ul>
      </div>
    </header>
  );
}

function BookingActions({
  summary,
  recommendable,
  onAssign,
  onRecommend,
}: {
  summary: BookingSummary;
  recommendable: number;
  onAssign: (tripId: number) => void;
  onRecommend: () => void;
}) {
  const next = summary.nextUncovered;
  if (!next) return <p className="text-meta text-ink-2">{pendingNote(summary)}</p>;
  return (
    <div className="flex flex-wrap gap-2">
      <button type="button" className="btn btn-primary h-auto min-h-9 whitespace-normal py-1.5" onClick={() => onAssign(next.trip.Id)}>
        {t.bookingPanel.assignNext(next.ordinal)}
      </button>
      {recommendable > 0 && (
        <button type="button" className="btn border-ink-3/75 hover:border-ink-3" onClick={onRecommend}>
          <Wand2 className="h-4 w-4" aria-hidden />
          {t.bookingPanel.recommendTrips(recommendable)}
        </button>
      )}
    </div>
  );
}

function TripList({ booking, block }: { booking: Booking; block: (item: NumberedTrip, headingLevel: HeadingLevel) => JSX.Element }) {
  const days = useMemo(() => tripDays(booking.Trips), [booking]);
  const byDay = days.length > 1;
  return (
    <section aria-labelledby="bp-trips" className="space-y-4">
      <h3 id="bp-trips" className="text-module font-semibold text-ink">
        {t.bookingPanel.trips(booking.Trips.length)}
      </h3>
      {days.length === 0 && <p className="text-meta text-ink-2">{t.bookingPanel.noTrips}</p>}
      {days.map(day => (
        <div key={day.date} className="space-y-4">
          {byDay && (
            <h4
              id={`bp-day-${day.date}`}
              className="border-b-2 border-ink-3/75 pb-1 pt-2 text-data font-bold text-ink first-letter:uppercase"
            >
              {longDayText(day.start)}
            </h4>
          )}
          <ol aria-labelledby={byDay ? `bp-day-${day.date}` : 'bp-trips'} className="space-y-6">
            {day.trips.map(item => (
              <li key={item.trip.Id}>{block(item, byDay ? 'h5' : 'h4')}</li>
            ))}
          </ol>
        </div>
      ))}
    </section>
  );
}

function BookingExplanation({
  booking,
  summary,
  explained,
}: {
  booking: Booking;
  summary: BookingSummary;
  explained?: Map<number, BarExplanation>;
}) {
  const status = statusOf(booking);
  const ofSession = booking.Trips.flatMap(trip => {
    const info = explained?.get(trip.Id);
    return info ? [{ tripId: trip.Id, info }] : [];
  });
  return (
    <details className="group rounded-lg bg-sunken">
      <summary className="flex cursor-pointer items-center gap-2 rounded-lg px-4 py-3 text-meta font-semibold text-ink hover:bg-muted">
        <ChevronDown className="h-4 w-4 transition group-open:rotate-180" aria-hidden />
        {t.common.explanation}
      </summary>
      <div className="space-y-3 border-t border-line px-4 py-3 text-meta">
        <div>
          <p className="font-medium">{t.bookingPanel.whyStatus(status ? STATUS_TEXT[status].label.toLowerCase() : booking.AssignmentStatus)}</p>
          <ul className="mt-1 list-disc space-y-0.5 pl-5 text-ink-2">
            {summary.reasons.map(reason => (
              <li key={reason}>{reason}</li>
            ))}
          </ul>
        </div>
        {ofSession.length > 0 && (
          <div>
            <p className="font-medium">{t.bookingPanel.sessionAssignments}</p>
            <ul className="mt-1 space-y-1">
              {ofSession.map(({ tripId, info }) => (
                <li key={tripId} className="flex items-center gap-1 text-ink-2">
                  {info.label}
                  <InfoButton explanation={info.explanation} label={info.label} />
                </li>
              ))}
            </ul>
          </div>
        )}
        <p className="text-xs text-ink-3">{t.bookingPanel.rulesAre}</p>
      </div>
    </details>
  );
}
