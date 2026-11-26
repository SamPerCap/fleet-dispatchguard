import { t } from './i18n/t';
import { Plus, Trash2 } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { api, ApiError } from './api';
import type { MasterData } from './App';
import { SERVICE_TYPES, serviceTypeLabel } from './calendar';
import { LocationPicker, type SuggestedLocations } from './LocationPicker';
import { suggestedEnd, travelWarning, type TravelTime } from './travel';
import type { Booking, Location } from './types';
import { ErrorBox } from './ui';
import { useFormVisit } from './visit';

/** `endTouched`: the user typed the arrival time, so it is never autofilled again. */
interface Row {
  origin: string;
  destination: string;
  date: string;
  start: string;
  end: string;
  endTouched: boolean;
  seats: string;
  type: string;
}
const emptyRow = (): Row => ({
  origin: '',
  destination: '',
  date: '',
  start: '08:00',
  end: '10:00',
  endTouched: false,
  seats: '20',
  type: 'Discretionary',
});
const pairKey = (row: Row) => `${row.origin}-${row.destination}`;

/** A pair that fails to load has no figure: neither warning nor autofill. */
function useTravelTimes(rows: Row[]): Record<string, TravelTime> {
  const [travel, setTravel] = useState<Record<string, TravelTime>>({});
  const requested = useRef(new Set<string>());
  useEffect(() => {
    for (const row of rows) {
      if (!row.origin || !row.destination) continue;
      const key = pairKey(row);
      if (requested.current.has(key)) continue;
      requested.current.add(key);
      api
        .travelTime(Number(row.origin), Number(row.destination))
        .then(travelTime => setTravel(m => ({ ...m, [key]: travelTime })))
        .catch(() => {
          /* No figure: the form works the same. */
        });
    }
  }, [rows]);
  return travel;
}

function TripFields({
  row,
  index,
  locations,
  usedLocations,
  travel,
  onChange,
  onRemove,
}: {
  row: Row;
  index: number;
  locations: Location[];
  usedLocations: SuggestedLocations;
  travel?: TravelTime;
  onChange: (patch: Partial<Row>) => void;
  onRemove?: () => void;
}) {
  const locationName = (id: string) => locations.find(l => String(l.Id) === id)?.Name ?? '';
  const warning =
    row.origin && row.destination
      ? travelWarning(travel, locationName(row.origin), locationName(row.destination), row.start, row.end)
      : null;
  const locationField = { locations, suggested: usedLocations, required: true };
  return (
    <fieldset className="rounded-lg bg-sunken p-4">
      <legend className="float-left mb-3 w-full text-meta font-semibold text-ink">{t.create.trip(index + 1)}</legend>
      <div className="clear-both grid grid-cols-2 gap-3">
        <LocationPicker {...locationField} label={t.create.origin} value={row.origin} onChange={origin => onChange({ origin })} />
        <LocationPicker {...locationField} label={t.create.destination} value={row.destination} onChange={destination => onChange({ destination })} />
        <label>
          <span className="label">{t.common.date}</span>
          <input type="date" className="input mt-1 w-full" value={row.date} onChange={e => onChange({ date: e.target.value })} required />
        </label>
        <div className="grid grid-cols-2 gap-2">
          <label>
            <span className="label">{t.common.departure}</span>
            <input type="time" className="input mt-1 w-full" value={row.start} onChange={e => onChange({ start: e.target.value })} />
          </label>
          <label>
            <span className="label">{t.common.arrival}</span>
            <input
              type="time"
              className="input mt-1 w-full"
              value={row.end}
              onChange={e => onChange({ end: e.target.value, endTouched: true })}
            />
          </label>
        </div>
        <label>
          <span className="label">{t.common.seats}</span>
          <input
            type="number"
            min={1}
            className="input mt-1 w-full"
            value={row.seats}
            onChange={e => onChange({ seats: e.target.value })}
          />
        </label>
        <label>
          <span className="label">{t.common.type}</span>
          <select className="input mt-1 w-full" value={row.type} onChange={e => onChange({ type: e.target.value })}>
            {SERVICE_TYPES.map(serviceType => (
              <option key={serviceType} value={serviceType}>
                {serviceTypeLabel(serviceType)}
              </option>
            ))}
          </select>
        </label>
      </div>
      {warning && (
        <p role="status" className="tone-warn mt-2 rounded-md px-2 py-1 text-sm">
          {warning}
        </p>
      )}
      {onRemove && (
        <button type="button" className="btn btn-text btn-sm mt-2 text-critical-ink" onClick={onRemove}>
          <Trash2 className="h-4 w-4" aria-hidden />
          {t.create.removeTrip}
        </button>
      )}
    </fieldset>
  );
}

/** Times are typed in Madrid time. */
export function CreateBooking({ master, onCreated }: { master: MasterData; onCreated: (booking: Booking) => void }) {
  useFormVisit(t.create.newBooking);
  const [customer, setCustomer] = useState('');
  const [description, setDescription] = useState('');
  const [rows, setRows] = useState<Row[]>([emptyRow()]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const set = (index: number, patch: Partial<Row>) =>
    setRows(previous => previous.map((row, rowIndex) => (rowIndex === index ? { ...row, ...patch } : row)));
  const valid = customer.trim() && description.trim() && rows.every(r => r.origin && r.destination && r.date && r.start < r.end);

  const usedLocations: SuggestedLocations = {
    label: t.create.alreadyHere,
    ids: rows.flatMap(row => [row.origin, row.destination]).filter(Boolean).map(Number),
  };
  const travel = useTravelTimes(rows);
  useEffect(() => {
    setRows(previous => {
      let changed = false;
      const next = previous.map(row => {
        const end = row.origin && row.destination ? suggestedEnd(row.start, row.endTouched, travel[pairKey(row)]) : null;
        if (end === null || end === row.end) return row;
        changed = true;
        return { ...row, end };
      });
      return changed ? next : previous;
    });
  }, [rows, travel]);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      const trips = rows.map(row => ({
        OriginLocationId: Number(row.origin),
        DestinationLocationId: Number(row.destination),
        Start: new Date(`${row.date}T${row.start}`).toISOString(),
        End: new Date(`${row.date}T${row.end}`).toISOString(),
        RequiredSeats: Number(row.seats),
        ServiceType: row.type,
      }));
      onCreated(await api.createBooking(customer.trim(), description.trim(), trips));
    } catch (cause) {
      const apiError = cause instanceof ApiError ? cause : null;
      setError(apiError ? [apiError.message, ...apiError.violations].join(' · ') : (cause as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <form
      className="space-y-4"
      onSubmit={event => {
        event.preventDefault();
        if (valid) void submit();
      }}
    >
      <label className="block">
        <span className="label">{t.common.customer}</span>
        <input className="input mt-1 w-full" value={customer} onChange={e => setCustomer(e.target.value)} required maxLength={200} />
      </label>
      <label className="block">
        <span className="label">{t.create.description}</span>
        <input className="input mt-1 w-full" value={description} onChange={e => setDescription(e.target.value)} required maxLength={500} />
      </label>
      {rows.map((row, index) => (
        <TripFields
          key={index}
          row={row}
          index={index}
          locations={master.locations}
          usedLocations={usedLocations}
          travel={travel[pairKey(row)]}
          onChange={patch => set(index, patch)}
          onRemove={rows.length > 1 ? () => setRows(previous => previous.filter((_, rowIndex) => rowIndex !== index)) : undefined}
        />
      ))}
      <button type="button" className="btn btn-sm" onClick={() => setRows(rs => [...rs, { ...rs[rs.length - 1] }])}>
        <Plus className="h-4 w-4" aria-hidden />
        {t.create.addTrip}
      </button>
      {error && <ErrorBox>{error}</ErrorBox>}
      <div>
        <button type="submit" className="btn btn-primary" disabled={!valid || busy}>
          {busy ? t.create.creating : t.create.create}
        </button>
      </div>
    </form>
  );
}
