import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { tripDays } from './booking';
import { TripBlock } from './TripBlock';
import type { BookingTrip, Driver, Unavailability, Vehicle } from './types';

const now = new Date('2026-11-25T06:00:00Z');
const trip = (extra: Partial<BookingTrip> = {}): BookingTrip => ({
  Id: 12,
  OriginLocationId: 1,
  DestinationLocationId: 2,
  Start: '2026-11-25T22:30:00',
  End: '2026-11-26T00:15:00',
  RequiredSeats: 50,
  ServiceType: 'Discretionary',
  Status: 'Assigned',
  Description: null,
  AssignedDriverId: 1,
  AssignedDriverName: 'Ana García',
  AssignedVehicleId: 70,
  AssignedVehiclePlate: '7000AAA',
  IsLocked: false,
  ...extra,
});
const itv: Unavailability = { Id: 5, Start: '2026-11-25T23:00:00', End: '2026-11-26T09:00:00', Type: 'TechnicalInspection', Notes: null };
const fleet = (entries: Unavailability[]) => [{ Id: 70, LicensePlate: '7000AAA', Unavailabilities: entries } as Vehicle];

interface Options {
  trips?: BookingTrip[];
  marked?: boolean;
  form?: string;
  vehicles?: Vehicle[];
  at?: Date;
}
const html = (of: BookingTrip, { trips = [of], marked = false, form, vehicles = fleet([]), at = now }: Options = {}) =>
  renderToStaticMarkup(
    createElement(TripBlock, {
      item: tripDays(trips)
        .flatMap(day => day.trips)
        .find(item => item.trip === of)!,
      master: { drivers: [] as Driver[], vehicles, locName: id => `Sitio ${id}` },
      now: at,
      headingLevel: 'h4',
      marked,
      form: form ?? null,
      onAssign: () => undefined,
    }),
  );
const labels = (markup: string) => [...markup.matchAll(/<dt[^>]*>(?:<svg.*?<\/svg>)?([^<]+)<\/dt>/g)].map(match => match[1]);

describe('block of a trip', () => {
  it('names itself with its ordinal and its day', () => {
    const second = trip();
    const others = [trip({ Id: 11, Start: '2026-11-25T08:00:00' }), trip({ Id: 13, Start: '2026-11-27T08:00:00' })];
    const markup = html(second, { trips: [second, ...others] });
    expect(markup).toMatch(/<h4 id="bp-trip-12-title"[^>]*><span[^>]*>Viaje 2 de 3<\/span> · <span[^>]*>mié, 25 nov<\/span><\/h4>/);
    expect(markup).toContain('aria-labelledby="bp-trip-12-title"');
    expect(markup).toMatch(/aria-label="Reasignar viaje 2"[^>]*>Reasignar</);
  });
  it('always the same fields, labelled and in the same order', () => {
    const fields = ['Salida', 'Llegada', 'Duración', 'Conductor', 'Vehículo', 'Plazas', 'Tipo', 'Servicio'];
    expect(labels(html(trip()))).toEqual(fields);
    const nobody = { AssignedDriverId: null, AssignedDriverName: null, AssignedVehicleId: null, AssignedVehiclePlate: null };
    expect(labels(html(trip({ ...nobody, Status: 'Cancelled' })))).toEqual(fields);
  });
  it('each time goes with its place, and an arrival on another day says so', () => {
    const markup = html(trip());
    expect(markup).toMatch(/>23:30<\/time><span[^>]*>Sitio 1<\/span>/);
    expect(markup).toMatch(/>01:15<\/time><span[^>]*>Sitio 2<\/span><span[^>]*>Llega el jue, 26 nov</);
    expect(markup).toContain('1 h 45 min');
    expect(markup).not.toContain('cambio de hora');
    expect(html(trip({ End: '2026-11-25T22:45:00' }))).not.toContain('Llega el');
  });
  it('what is missing stands out only while it can be assigned', () => {
    const missing = { AssignedVehicleId: null, AssignedVehiclePlate: null };
    expect(html(trip(missing))).toMatch(/critical-ink">.*Sin vehículo/);
    expect(html(trip(missing))).toContain('aria-label="Asignar viaje 1"');
    expect(html(trip({ ...missing, Status: 'Cancelled' }))).toContain('Sin vehículo');
    expect(html(trip({ ...missing, Status: 'Cancelled' }))).not.toMatch(/critical-ink">.*Sin vehículo/);
  });
  it('a cancelled or locked trip has its notice and no button', () => {
    const cancelled = html(trip({ Status: 'Cancelled', IsLocked: true }));
    expect(cancelled).toContain('no se le asigna conductor ni vehículo');
    expect(cancelled).not.toContain('Bloqueado');
    expect(cancelled).not.toContain('<button');
    const locked = html(trip({ IsLocked: true }));
    expect(locked).toContain('No se puede asignar ni reasignar');
    expect(locked).not.toContain('<button');
  });
  it('a forced trip keeps its description', () => {
    expect(html(trip({ ForcedDescription: 'Forzado por Ana: motivo' }))).toContain('<p>Forzado por Ana: motivo</p>');
    expect(html(trip())).not.toContain('data-status="forced"');
  });
  it('a clash with the agenda, with who, what and when, until it is over', () => {
    const notice = 'En conflicto con la agenda';
    const markup = html(trip(), { vehicles: fleet([itv]) });
    expect(markup).toContain('7000AAA. Indisponible: ITV');
    expect(markup).toContain(notice);
    expect(html(trip(), { vehicles: fleet([{ ...itv, Start: '2026-11-26T00:15:00' }]) })).not.toContain(notice);
    expect(html(trip({ Status: 'Cancelled' }), { vehicles: fleet([itv]) })).not.toContain(notice);
    expect(html(trip(), { vehicles: fleet([itv]), at: new Date('2026-11-26T00:15:00Z') })).not.toContain(notice);
  });
  it('the form opens inside, named after the trip, and replaces the button', () => {
    const markup = html(trip(), { form: 'FORM' });
    expect(markup).toMatch(/<section aria-labelledby="bp-trip-12-form"/);
    expect(markup).toMatch(/<p id="bp-trip-12-form"[^>]*>Reasignar viaje 1<\/p>FORM<\/section><\/article>/);
    expect(markup).not.toContain('<button');
  });
  it('the one the panel was opened from is marked with text, unless it is the only one', () => {
    const first = trip();
    expect(html(first, { marked: true })).toContain('aria-current="true"');
    expect(html(first, { marked: true })).not.toContain('Seleccionado');
    expect(html(first, { marked: true, trips: [first, trip({ Id: 13 })] })).toContain('Seleccionado');
    expect(html(first)).not.toContain('aria-current');
  });
});
