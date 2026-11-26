import { describe, expect, it } from 'vitest';
import {
  arrivalDayText,
  bookingSummary,
  clockChanges,
  coverage,
  dateRangeText,
  durationText,
  pendingNote,
  tripDays,
  tripName,
} from './booking';
import type { BookingTrip } from './types';

const trip = (id: number, start: string, extra: Partial<BookingTrip> = {}): BookingTrip => ({
  Id: id,
  OriginLocationId: 1,
  DestinationLocationId: 2,
  Start: start,
  End: start,
  RequiredSeats: 20,
  ServiceType: 'Regular',
  Status: 'Scheduled',
  Description: null,
  AssignedDriverId: 1,
  AssignedDriverName: 'Ana',
  AssignedVehicleId: 1,
  AssignedVehiclePlate: '1234ABC',
  IsLocked: false,
  ...extra,
});
const at = (iso: string) => new Date(iso);

describe('bookingSummary', () => {
  it('counts what is covered and what is missing, without the cancelled ones', () => {
    const summary = bookingSummary([
      trip(1, '2026-11-18T08:00:00'),
      trip(2, '2026-11-18T12:00:00', { AssignedDriverId: null, RequiredSeats: 50 }),
      trip(3, '2026-11-18T10:00:00', { AssignedDriverId: null, AssignedVehicleId: null }),
      trip(4, '2026-11-18T09:00:00', { Status: 'Cancelled', AssignedDriverId: null, RequiredSeats: 80 }),
    ]);
    expect(summary).toMatchObject({ active: 3, covered: 1, noDriver: 2, noVehicle: 1, cancelled: 1, maxSeats: 50 });
    expect(summary.reasons).toEqual(['Viaje 3 (servicio #3): sin conductor ni vehículo.', 'Viaje 4 (servicio #2): sin conductor.']);
    expect(summary.nextUncovered?.trip.Id).toBe(3);
  });
  it('the next uncovered one skips the locked ones and keeps its ordinal', () => {
    const summary = bookingSummary([
      trip(1, '2026-11-18T08:00:00', { AssignedVehicleId: null, IsLocked: true }),
      trip(2, '2026-11-18T09:00:00', { AssignedVehicleId: null }),
    ]);
    expect(summary.nextUncovered).toMatchObject({ trip: { Id: 2 }, ordinal: 2, total: 2 });
  });
  it('empty states', () => {
    expect(bookingSummary([]).reasons).toEqual(['La reserva no tiene viajes.']);
    expect(bookingSummary([]).nextUncovered).toBeNull();
    expect(bookingSummary([trip(1, '2026-11-18T08:00:00', { Status: 'Cancelled' })]).reasons).toEqual([
      'Todos los viajes están cancelados.',
    ]);
    expect(bookingSummary([trip(1, '2026-11-18T08:00:00')]).reasons).toEqual(['Todos los viajes tienen conductor y vehículo.']);
  });
});

describe('order, ordinals and days', () => {
  const days = (trips: BookingTrip[]) =>
    tripDays(trips).map(day => [day.date, day.trips.map(({ trip: { Id }, ordinal, total }) => `${Id}: ${ordinal}/${total}`)]);

  it('sorts by departure and the cancelled ones keep their number', () => {
    expect(
      days([
        trip(4, '2026-11-18T12:00:00'),
        trip(7, '2026-11-18T08:00:00'),
        trip(5, '2026-11-18T09:00:00', { Status: 'Cancelled' }),
      ]),
    ).toEqual([['2026-11-18', ['7: 1/3', '5: 2/3', '4: 3/3']]]);
  });
  it('compares instants, not texts, and breaks ties by arrival and then by Id', () => {
    expect(
      days([
        trip(9, '2026-11-18T09:00:00Z', { End: '2026-11-18T10:00:00' }),
        trip(8, '2026-11-18T09:00:00', { End: '2026-11-18T10:00:00' }),
        trip(30, '2026-11-18T10:00:00+01:00', { End: '2026-11-18T09:30:00Z' }),
      ]),
    ).toEqual([['2026-11-18', ['30: 1/3', '8: 2/3', '9: 3/3']]]);
  });
  it('groups by the Madrid day of departure and the ordinals go on from one day to the next', () => {
    expect(
      days([
        trip(1, '2026-11-24T22:59:00', { End: '2026-11-25T02:00:00' }), // 23:59 of the 24th
        trip(2, '2026-11-24T23:00:00'), // 00:00 of the 25th
        trip(3, '2026-11-25T08:00:00'),
      ]),
    ).toEqual([
      ['2026-11-24', ['1: 1/3']],
      ['2026-11-25', ['2: 2/3', '3: 3/3']],
    ]);
  });
  it('the two hours 02:30 of the October change stay in order and in the same day', () => {
    expect(days([trip(2, '2026-10-25T01:30:00'), trip(1, '2026-10-25T00:30:00')])).toEqual([['2026-10-25', ['1: 1/2', '2: 2/2']]]);
  });
  it('a booking without trips has no days', () => expect(tripDays([])).toEqual([]));
  it('the name of a trip', () => expect(tripName({ ordinal: 2, total: 3 })).toBe('Viaje 2 de 3'));
});

describe('arrival day', () => {
  const start = at('2026-11-25T22:30:00Z'); // 23:30 in Madrid
  it('same day: nothing to say', () => expect(arrivalDayText(start, at('2026-11-25T22:59:00Z'))).toBeNull());
  it('midnight is already the next day', () => expect(arrivalDayText(start, at('2026-11-25T23:00:00Z'))).toBe('Llega el jue, 26 nov'));
  it('several days later', () => expect(arrivalDayText(start, at('2026-11-28T09:00:00Z'))).toBe('Llega el sáb, 28 nov'));
  it('another UTC day but the same Madrid day', () =>
    expect(arrivalDayText(at('2026-11-24T23:30:00Z'), at('2026-11-25T08:00:00Z'))).toBeNull());
  it('the night of the clock change', () =>
    expect(arrivalDayText(at('2026-03-28T22:30:00Z'), at('2026-03-29T01:30:00Z'))).toBe('Llega el dom, 29 mar'));
});

describe('duration', () => {
  it.each([
    ['2026-11-25T08:00:00Z', '2026-11-25T08:45:00Z', '45 min'],
    ['2026-11-25T08:00:00Z', '2026-11-25T10:00:00Z', '2 h'],
    ['2026-11-25T22:30:00Z', '2026-11-26T00:15:00Z', '1 h 45 min'],
    ['2026-11-25T08:00:00Z', '2026-11-27T09:00:00Z', '49 h'],
    ['2026-03-29T00:30:00Z', '2026-03-29T01:30:00Z', '1 h'], // 01:30 → 03:30 in Madrid
    ['2026-10-24T23:30:00Z', '2026-10-25T02:30:00Z', '3 h'], // 01:30 → 03:30 in Madrid
  ])('%s → %s lasts %s', (start, end, text) => expect(durationText(at(start), at(end))).toBe(text));

  it('says whether the clock changes on the way', () => {
    expect(clockChanges(at('2026-03-29T00:30:00Z'), at('2026-03-29T01:30:00Z'))).toBe(true);
    expect(clockChanges(at('2026-10-24T23:30:00Z'), at('2026-10-25T02:30:00Z'))).toBe(true);
    expect(clockChanges(at('2026-03-28T22:00:00Z'), at('2026-03-29T00:59:00Z'))).toBe(false); // arrives a minute before
    expect(clockChanges(at('2026-11-25T08:00:00Z'), at('2026-11-25T10:00:00Z'))).toBe(false);
  });
});

describe('dates of the booking', () => {
  it('one day', () =>
    expect(dateRangeText(at('2026-11-25T07:00:00Z'), at('2026-11-25T17:30:00Z'))).toBe('mié, 25 nov 2026 · 08:00 – 18:30'));
  it('several days', () =>
    expect(dateRangeText(at('2026-12-31T07:00:00Z'), at('2027-01-01T00:15:00Z'))).toBe('jue, 31 dic 2026, 08:00 – vie, 1 ene 2027, 01:15'));
});

describe('coverage line', () => {
  const line = (trips: BookingTrip[]) => coverage(bookingSummary(trips)).map(part => part.text);
  const start = '2026-11-18T08:00:00';

  it('counts over the trips that are not cancelled', () => {
    const summary = bookingSummary([
      trip(1, start),
      trip(2, start, { AssignedDriverId: null }),
      trip(3, start, { AssignedVehicleId: null }),
      trip(4, start, { Status: 'Cancelled' }),
    ]);
    expect(coverage(summary)).toEqual([
      { text: '4 viajes', tone: 'neutral' },
      { text: '1 de 3 cubiertos', tone: 'neutral' },
      { text: '1 sin conductor', tone: 'missing' },
      { text: '1 sin vehículo', tone: 'missing' },
      { text: '1 cancelado', tone: 'neutral' },
    ]);
  });
  it('all covered', () => {
    const [, covered] = coverage(bookingSummary([trip(1, start), trip(2, start)]));
    expect(covered).toEqual({ text: '2 de 2 cubiertos', tone: 'covered' });
  });
  it('singular', () => expect(line([trip(1, start, { AssignedDriverId: null })])).toEqual(['1 viaje', '0 de 1 cubierto', '1 sin conductor']));
  it('all cancelled, and none', () => {
    const cancelled = { Status: 'Cancelled' };
    expect(line([trip(1, start, cancelled), trip(2, start, cancelled)])).toEqual(['2 viajes', '2 cancelados']);
    expect(line([])).toEqual(['0 viajes']);
  });
});

describe('when there is nothing to assign next', () => {
  const start = '2026-11-18T08:00:00';
  const note = (trips: BookingTrip[]) => pendingNote(bookingSummary(trips));
  it('says nothing if there is', () => expect(note([trip(1, start, { AssignedDriverId: null })])).toBeNull());
  it('no trips to assign', () => {
    expect(note([])).toBe('No hay viajes que asignar.');
    expect(note([trip(1, start, { Status: 'Cancelled' })])).toBe('No hay viajes que asignar.');
  });
  it('all covered', () => expect(note([trip(1, start)])).toBe('Nada pendiente. Para cambiar un viaje, pulsa «Reasignar» en él.'));
  it('only locked ones are left: that is not "nothing pending"', () =>
    expect(note([trip(1, start, { AssignedDriverId: null, IsLocked: true })])).toBe('Lo que falta por cubrir está bloqueado.'));
});
