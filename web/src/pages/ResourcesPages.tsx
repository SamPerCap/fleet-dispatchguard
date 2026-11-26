import { t } from '../i18n/t';
import { BusFront, CalendarDays, CircleCheck, MapPin, Search, SearchX, ShieldOff, UserCheck, Users } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { api } from '../api';
import type { MasterData } from '../App';
import { addDays, parseApiDate, startOfDay, TIME_ZONE } from '../gantt';
import { IncidentPanel } from '../IncidentPanel';
import { isUnavailableAt, matchesSearch, upcomingTrips, vehiclePosition } from '../resources';
import { ResourceState } from '../ResourceModal';
import type { BarExplanation } from '../Timeline';
import { Avatar, EmptyState, ErrorBox, PageHeader, Stat } from '../ui';
import type { Trip } from '../types';
import { locale } from '../language';

const dateInput = (date: Date) =>
  `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
const formatWhen = (instant: string) =>
  parseApiDate(instant).toLocaleString(locale(), {
    weekday: 'short',
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: TIME_ZONE,
  });

/** The last 7 days decide the location (SPEC 0032); the next 14, the upcoming services. */
function useResourceTrips(version: number) {
  const [trips, setTrips] = useState<Trip[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const now = new Date();
    api
      .trips(addDays(now, -7), addDays(now, 15))
      .then(loaded => {
        setTrips(loaded);
        setError(null);
      })
      .catch(e => setError((e as Error).message));
  }, [version]);
  return { trips: trips ?? [], loaded: trips !== null, error };
}

function SearchBox({ value, onChange, label }: { value: string; onChange: (value: string) => void; label: string }) {
  return (
    <label className="relative block w-72">
      <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-3" aria-hidden />
      <input
        type="search"
        className="input w-full pl-9"
        aria-label={label}
        placeholder={label}
        value={value}
        onChange={e => onChange(e.target.value)}
      />
    </label>
  );
}

const nextOf = (trips: Trip[], now: Date) => upcomingTrips(trips, now)[0] ?? null;

export function DriversPage({
  master,
  version,
  onChanged,
  onAllocated,
  onOpen,
}: {
  master: MasterData;
  version: number;
  onOpen: (driverId: number) => void;
  onChanged: () => void;
  onAllocated: (tripId: number, info: BarExplanation) => void;
}) {
  const [day, setDay] = useState(() => startOfDay(new Date()));
  const [trips, setTrips] = useState<Trip[]>([]);
  const [query, setQuery] = useState('');
  const resourceTrips = useResourceTrips(version);
  const load = useCallback(async () => {
    setTrips((await api.trips(day, addDays(day, 1))).filter(t => t.Status !== 'Cancelled'));
  }, [day]);
  useEffect(() => {
    void load().catch(() => setTrips([]));
  }, [load, version]);
  const driverName = (id: number | null) => {
    const driver = master.drivers.find(x => x.Id === id);
    return driver ? `${driver.FirstName} ${driver.LastName}` : '—';
  };
  const now = new Date();
  const list = master.drivers.filter(driver => matchesSearch(query, `${driver.FirstName} ${driver.LastName}`, driver.IdentificationNumber));
  const stats = useMemo(() => {
    const active = master.drivers.filter(d => d.IsActive);
    const off = active.filter(d => isUnavailableAt(d.Unavailabilities, now)).length;
    return { active: active.length, available: active.length - off, off, external: active.filter(d => d.IsExternal).length };
  }, [master.drivers]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div className="page">
      <PageHeader
        title={t.common.drivers}
        icon={Users}
        subtitle={t.resources.driversIntro}
      >
        <SearchBox value={query} onChange={setQuery} label={t.resources.searchDriver} />
      </PageHeader>
      <div className="mb-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-4" role="group" aria-label={t.resources.driverFigures}>
        <Stat icon={Users} label={t.resources.activeDrivers} value={stats.active} />
        <Stat icon={UserCheck} tone="success" label={t.resources.noneUnavailable} value={stats.available} />
        <Stat
          icon={ShieldOff}
          tone={stats.off ? 'warning' : 'neutral'}
          label={t.resources.unavailableNow}
          value={stats.off}
          foot="cualquier indisponibilidad registrada"
        />
        <Stat icon={BusFront} tone="neutral" label={t.resources.externals} value={stats.external} foot={t.resources.notProposed} />
      </div>
      {resourceTrips.error && (
        <div className="mb-3">
          <ErrorBox>{t.resources.servicesFailed(resourceTrips.error)}</ErrorBox>
        </div>
      )}
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_400px] 2xl:grid-cols-[minmax(0,1fr)_440px] 3xl:grid-cols-[minmax(0,1fr)_480px]">
        <div className="card self-start overflow-x-auto">
          <table className="tbl min-w-[640px]" aria-label={t.common.drivers}>
            <thead>
              <tr>
                <th scope="col">{t.common.name}</th>
                <th scope="col">{t.common.base}</th>
                <th scope="col">{t.resources.nextService}</th>
                <th scope="col">{t.common.state}</th>
              </tr>
            </thead>
            <tbody>
              {list.map(driver => {
                const next = nextOf(
                  resourceTrips.trips.filter(t => t.AssignedDriverId === driver.Id),
                  now,
                );
                return (
                  <tr key={driver.Id}>
                    <td>
                      <div className="flex min-w-0 items-center gap-3">
                        <Avatar name={`${driver.FirstName} ${driver.LastName}`} />
                        <div className="min-w-0">
                          <button
                            type="button"
                            className="max-w-full truncate text-left font-semibold text-ink hover:text-blue-ink hover:underline"
                            aria-haspopup="dialog"
                            onClick={() => onOpen(driver.Id)}
                          >
                            {driver.FirstName} {driver.LastName}
                          </button>
                          <p className="text-xs text-ink-3">{driver.IdentificationNumber}</p>
                        </div>
                      </div>
                    </td>
                    <td
                      className="max-w-[220px] truncate 3xl:max-w-[360px] text-meta text-ink-2"
                      title={driver.HomeLocationId ? master.locName(driver.HomeLocationId) : undefined}
                    >
                      {driver.HomeLocationId ? master.locName(driver.HomeLocationId) : '—'}
                    </td>
                    <td className="whitespace-nowrap text-meta">
                      {!resourceTrips.loaded ? (
                        <span className="text-ink-3">…</span>
                      ) : next ? (
                        <span className="inline-flex items-center gap-1.5 text-ink">
                          <CalendarDays className="h-3.5 w-3.5 text-ink-3" aria-hidden />
                          {formatWhen(next.Start)}
                        </span>
                      ) : (
                        <span className="text-ink-3">{t.resources.noServices}</span>
                      )}
                    </td>
                    <td>
                      <ResourceState
                        active={driver.IsActive}
                        unavailable={isUnavailableAt(driver.Unavailabilities, now)}
                        external={driver.IsExternal}
                      />
                    </td>
                  </tr>
                );
              })}
              {list.length === 0 && (
                <tr>
                  <td colSpan={4}>
                    <EmptyState icon={SearchX} title={t.resources.noDriverMatches}>
                      {t.resources.tryOtherName}
                    </EmptyState>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
        <div className="space-y-3 self-start">
          <label className="card flex items-center justify-between gap-3 px-4 py-3">
            <span className="text-meta font-medium text-ink-2">{t.resources.dayOfServices}</span>
            <input
              type="date"
              className="input"
              aria-label={t.common.day}
              value={dateInput(day)}
              onChange={event => event.target.value && setDay(startOfDay(new Date(`${event.target.value}T00:00`)))}
            />
          </label>
          <IncidentPanel
            day={day}
            drivers={master.drivers}
            fleet={master.vehicles}
            trips={trips}
            driverName={driverName}
            describe={trip => `${master.locName(trip.OriginLocationId)} → ${master.locName(trip.DestinationLocationId)}`}
            onChanged={async () => {
              await load();
              onChanged();
            }}
            onAllocated={onAllocated}
          />
        </div>
      </div>
    </div>
  );
}

export function VehiclesPage({ master, version, onOpen }: { master: MasterData; version: number; onOpen: (vehicleId: number) => void }) {
  const [query, setQuery] = useState('');
  const { trips, loaded, error } = useResourceTrips(version);
  const now = new Date();
  const list = master.vehicles.filter(v => matchesSearch(query, v.LicensePlate, v.Brand, v.Model));
  const stats = useMemo(() => {
    const active = master.vehicles.filter(v => v.IsActive);
    const off = active.filter(v => isUnavailableAt(v.Unavailabilities, now)).length;
    return {
      active: active.length,
      available: active.length - off,
      off,
      seats: active.reduce((sum, vehicle) => sum + vehicle.SeatingCapacity, 0),
    };
  }, [master.vehicles]); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <div className="page">
      <PageHeader title={t.common.vehicles} icon={BusFront} subtitle={t.resources.vehiclesIntro}>
        <SearchBox value={query} onChange={setQuery} label={t.resources.searchVehicle} />
      </PageHeader>
      <div className="mb-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-4" role="group" aria-label={t.resources.vehicleFigures}>
        <Stat icon={BusFront} label={t.resources.activeVehicles} value={stats.active} />
        <Stat icon={CircleCheck} tone="success" label={t.resources.noneUnavailable} value={stats.available} />
        <Stat
          icon={ShieldOff}
          tone={stats.off ? 'warning' : 'neutral'}
          label={t.resources.unavailableNow}
          value={stats.off}
          foot="cualquier indisponibilidad registrada"
        />
        <Stat icon={Users} tone="neutral" label={t.resources.fleetSeats} value={stats.seats.toLocaleString(locale())} />
      </div>
      {error && (
        <div className="mb-3">
          <ErrorBox>{t.resources.servicesFailed(error)}</ErrorBox>
        </div>
      )}
      <div className="card overflow-x-auto">
        <table className="tbl min-w-[760px]" aria-label={t.common.vehicles}>
          <thead>
            <tr>
              {[t.common.plate, t.common.model, t.common.seats, t.card.currentPlace, t.common.state].map(heading => (
                <th key={heading} scope="col">
                  {heading}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {list.map(vehicle => {
              const pos = vehiclePosition(
                trips.filter(t => t.AssignedVehicleId === vehicle.Id),
                vehicle.HomeLocationId,
                now,
              );
              return (
                <tr key={vehicle.Id}>
                  <td>
                    <div className="flex items-center gap-3">
                      <span aria-hidden className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-muted text-ink-2">
                        <BusFront className="h-[18px] w-[18px]" />
                      </span>
                      <button
                        type="button"
                        className="whitespace-nowrap font-semibold text-ink hover:text-blue-ink hover:underline"
                        aria-haspopup="dialog"
                        onClick={() => onOpen(vehicle.Id)}
                      >
                        {vehicle.LicensePlate}
                      </button>
                    </div>
                  </td>
                  <td className="text-meta text-ink">
                    {vehicle.Brand} {vehicle.Model}
                  </td>
                  <td className="text-meta font-semibold text-ink">{vehicle.SeatingCapacity}</td>
                  <td className="max-w-[260px] truncate 3xl:max-w-[420px] text-meta">
                    {!loaded ? (
                      <span className="text-ink-3">…</span>
                    ) : pos.locationId ? (
                      <span className="inline-flex items-center gap-1.5 text-ink">
                        <MapPin className="h-3.5 w-3.5 text-ink-3" aria-hidden />
                        {master.locName(pos.locationId)}
                        {pos.source === 'Home' && <span className="text-ink-3"> {t.resources.isBase}</span>}
                      </span>
                    ) : (
                      <span className="text-ink-3">—</span>
                    )}
                  </td>
                  <td>
                    <ResourceState
                      active={vehicle.IsActive}
                      unavailable={isUnavailableAt(vehicle.Unavailabilities, now)}
                      external={vehicle.IsExternal}
                    />
                  </td>
                </tr>
              );
            })}
            {list.length === 0 && (
              <tr>
                <td colSpan={5}>
                  <EmptyState icon={SearchX} title={t.resources.noVehicleMatches}>
                    {t.resources.tryOtherPlate}
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
