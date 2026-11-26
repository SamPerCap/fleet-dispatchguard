import { t } from './i18n/t';
import { AlertTriangle, ArrowRight } from 'lucide-react';
import { useState } from 'react';
import type { MasterData } from './App';
import { formatDateTime, parseApiDate } from './gantt';
import type { AffectedTrip } from './types';

const SHOWN = 5;

export function AffectedList({
  title,
  trips,
  master,
  detail,
  onOpenTrip,
}: {
  title: string;
  trips: AffectedTrip[];
  master: MasterData;
  /** What to add about each service. */
  detail?: (trip: AffectedTrip) => string;
  onOpenTrip: (bookingId: number, tripId: number) => void;
}) {
  const [all, setAll] = useState(false);
  const shown = all ? trips : trips.slice(0, SHOWN);
  return (
    <div className="rounded-md bg-warning-soft px-3 py-2 text-warning-ink">
      <p className="flex items-center gap-1.5 text-meta font-semibold">
        <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden />
        {title}
      </p>
      <ul className="mt-1.5 space-y-1" aria-label={title}>
        {shown.map(trip => (
          <li key={trip.TripId}>
            <button
              type="button"
              className="flex w-full items-center justify-between gap-3 rounded-md bg-surface px-2.5 py-1.5 text-left text-xs text-ink hover:bg-sunken"
              onClick={() => onOpenTrip(trip.BookingId, trip.TripId)}
            >
              <span className="min-w-0">
                <span className="block truncate font-medium">
                  {master.locName(trip.OriginLocationId)} → {master.locName(trip.DestinationLocationId)}
                </span>
                <span className="block truncate text-ink-2">
                  {t.affected.booking(trip.BookingId, trip.CustomerName, detail ? ` · ${detail(trip)}` : '')}
                </span>
              </span>
              <span className="flex shrink-0 items-center gap-2 font-medium text-ink-2">
                {formatDateTime(parseApiDate(trip.Start))}
                <ArrowRight className="h-3.5 w-3.5" aria-hidden />
              </span>
            </button>
          </li>
        ))}
      </ul>
      {trips.length > shown.length && (
        <button type="button" className="btn btn-link mt-1" onClick={() => setAll(true)}>
          {t.affected.seeAll(trips.length)}
        </button>
      )}
    </div>
  );
}
