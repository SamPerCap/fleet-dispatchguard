import { describe, expect, it } from 'vitest';
import { assignLabel, confirmation, outcome, retryLabel, retryQueue, type Names } from './assignPeriod';
import { toLines, withResult, type ApplyResult, type ProposalLine } from './recommend';
import type { Recommendation, RecommendationLine, RecommendedResource, Trip } from './types';

const now = new Date('2026-12-01T06:00:00Z');
const res = (Id: number): RecommendedResource => ({
  Id,
  Name: `R${Id}`,
  DistanceKm: 1,
  StepKm: 5,
  Where: '',
  SameBooking: false,
  Late: null,
});
const line = (TripId: number, solved = true): RecommendationLine => ({
  TripId,
  BookingId: 1,
  CustomerName: 'Colegio',
  BaseId: 1,
  Start: '2026-12-01T08:00:00Z',
  End: '2026-12-01T10:00:00Z',
  Origin: 'A',
  Destination: 'B',
  RequiredSeats: 20,
  Status: solved ? 'Proposed' : 'NoSolution',
  Driver: solved ? res(TripId * 10) : null,
  Vehicle: solved ? res(TripId * 100) : null,
  Reason: solved ? 'Propuesta' : 'Nadie a 30 km: 2 conductores más lejos',
  Warnings: [],
  Explanation: null,
});
const proposal = (Lines: RecommendationLine[], LeftOut = 0): Recommendation => ({
  CalculatedAt: '2026-12-01T06:00:00Z',
  Lines,
  LeftOut,
  MaxTrips: 100,
  MaxDays: 31,
  StepsKm: [5, 15, 30],
  Note: 'n',
  ArrivalNote: 'a',
});
const trip = (Id: number, over: Partial<Trip> = {}): Trip => ({
  Id,
  OriginLocationId: 1,
  DestinationLocationId: 2,
  Start: '2026-12-01T08:00:00Z',
  End: '2026-12-01T10:00:00Z',
  RequiredSeats: 20,
  ServiceType: 'Regular',
  Status: 'Pending',
  Description: null,
  AssignedDriverId: null,
  AssignedVehicleId: null,
  IsLocked: false,
  IsActive: true,
  BookingId: 1,
  BaseId: 1,
  ...over,
});
const ours = (id: number) => trip(id, { Status: 'Assigned', AssignedDriverId: id * 10, AssignedVehicleId: id * 100 });
const names: Names = {
  driver: id => `Cond ${id}`,
  vehicle: id => `Mat ${id}`,
  location: id => `Lugar ${id}`,
  customer: id => (id === 1 ? 'Colegio' : `Reserva #${id}`),
};
const applied: ApplyResult = { state: 'applied', text: 'Asignado' };
const unknown: ApplyResult = { state: 'unknown', text: 'No se sabe si se guardó' };
const busy: ApplyResult = { state: 'busy', text: 'Otra operación en curso' };
const rejected: ApplyResult = { state: 'rejected', text: 'CE 561 · Jornada' };
const see = (proposed: Recommendation, lines: ProposalLine[], trips: Trip[] | null, over: Partial<Parameters<typeof outcome>[0]> = {}) =>
  outcome({ proposal: proposed, lines, trips, baseId: null, end: 'done', now, names, ...over });
const keys = (result: ReturnType<typeof outcome>) => result.left.map(r => [r.tripId, r.key]);

describe('button', () => {
  it('says what is on screen', () => {
    expect((['day', 'week', 'month'] as const).map(assignLabel)).toEqual(['Asignar día', 'Asignar semana', 'Asignar mes']);
  });
});

describe('confirmation', () => {
  it('how many, how many without solution and how many out of the limits', () => {
    const proposed = proposal([...Array.from({ length: 98 }, (_, index) => line(index + 1)), line(99, false), line(100, false)], 30);
    expect(confirmation(proposed, toLines(proposed))).toEqual({
      assign: 98,
      headline: 'Se van a asignar 98 viajes.',
      button: 'Asignar 98 viajes',
      notes: [
        '2 viajes no tienen solución y quedan sin asignar.',
        '30 viajes pendientes quedan fuera: como mucho 100 viajes y 31 días desde la primera salida.',
      ],
    });
  });
  it('one trip, nothing else to say', () => {
    const proposed = proposal([line(1)]);
    expect(confirmation(proposed, toLines(proposed))).toEqual({
      assign: 1,
      headline: 'Se va a asignar 1 viaje.',
      button: 'Asignar 1 viaje',
      notes: [],
    });
  });
  it('nothing pending', () => {
    expect(confirmation(proposal([]), [])).toEqual({
      assign: 0,
      headline: 'No hay nada que asignar en este periodo.',
      button: null,
      notes: [],
    });
  });
  it('all without solution: it says so and does not offer to apply', () => {
    const proposed = proposal([line(1, false)]);
    expect(confirmation(proposed, toLines(proposed))).toEqual({
      assign: 0,
      headline: 'No hay nada que asignar en este periodo.',
      button: null,
      notes: ['1 viaje no tiene solución y queda sin asignar.'],
    });
  });
});

describe('summary', () => {
  it('everything assigned: nothing left', () => {
    const proposed = proposal([line(1), line(2)]);
    const result = see(proposed, withResult(withResult(toLines(proposed), 1, applied), 2, applied), [ours(1), ours(2)]);
    expect([result.assigned, result.left, result.untouched, result.headline]).toEqual([
      2,
      [],
      [],
      '2 viajes asignados. No queda ninguno pendiente en este periodo.',
    ]);
  });

  it('each trip that was not, with why: rejected, busy, without solution and not tried', () => {
    const proposed = proposal([line(1), line(2), line(3), line(4, false), line(5)]);
    let current = withResult(toLines(proposed), 1, applied);
    current = withResult(withResult(current, 2, rejected), 3, busy);
    const result = see(proposed, current, [ours(1), trip(2), trip(3), trip(4), trip(5)], { end: 'stopped' });
    expect(keys(result)).toEqual([
      [2, 'rejected'],
      [3, 'busy'],
      [4, 'noSolution'],
      [5, 'untried'],
    ]);
    expect(result.left.map(r => r.reason)).toEqual([
      'CE 561 · Jornada',
      'Otra operación en curso',
      'Nadie a 30 km: 2 conductores más lejos',
      'No se intentó: detuviste la asignación.',
    ]);
    expect(result.headline).toBe('1 viaje asignado. Sin asignar: 1 sin solución, 1 rechazado, 1 ocupado y 1 sin intentar.');
    expect(result.left[0]).toMatchObject({ bookingId: 1, customer: 'Colegio', origin: 'A', destination: 'B', pending: true });
    expect(see(proposed, current, null, { end: 'api' }).left[3].reason).toBe('No se intentó: la API no respondió o pidió esperar.');
    expect(see(proposed, current, null, { end: 'done' }).left[3].reason).toBe('No se intentó.'); // nobody stopped it
  });

  it('over the limit: the rest are listed one by one by departure, with their customer and route', () => {
    const proposed = proposal([line(1)], 2);
    const result = see(proposed, withResult(toLines(proposed), 1, applied), [
      ours(1),
      trip(8, { BookingId: 7, Start: '2026-12-01T09:00:00Z' }),
      trip(9),
    ]);
    expect(keys(result)).toEqual([
      [9, 'leftOut'],
      [8, 'leftOut'],
    ]);
    expect(result.left[1]).toMatchObject({ customer: 'Reserva #7', origin: 'Lugar 1', destination: 'Lugar 2', bookingId: 7 });
    expect(result.left[0].reason).toBe('Fuera del tope de 100 viajes y 31 días. Vuelve a proponer para los siguientes.');
    expect([result.headline, result.note]).toEqual(['1 viaje asignado. Sin asignar: 2 fuera del tope.', null]);
  });

  it('without reading the period again, how many are out is still said and nothing is taken for granted', () => {
    const proposed = proposal([line(1)], 2);
    const result = see(proposed, withResult(toLines(proposed), 1, applied), null);
    expect(result.note).toMatch(/^2 viajes pendientes quedan fuera/);
    expect(result.headline).toBe('1 viaje asignado.');
  });

  it('a pending trip that appeared later is not hidden', () => {
    expect(see(proposal([]), [], [trip(9)]).left[0]).toMatchObject({
      tripId: 9,
      key: 'new',
      reason: 'No estaba pendiente al calcular la propuesta. Vuelve a proponer.',
    });
  });

  it('what never enters goes apart with why; finished, cancelled and other bases do not appear', () => {
    const result = see(
      proposal([]),
      [],
      [
        trip(1, { Start: '2026-12-01T06:00:00Z' }),
        trip(2, { IsLocked: true }),
        trip(3, { AssignedDriverId: 5 }),
        trip(4, { AssignedVehicleId: 5 }),
        trip(5, { Status: 'Cancelled' }),
        trip(6, { Status: 'Completed' }),
        trip(7, { IsActive: false }),
        trip(8, { BaseId: 2 }),
        ours(9),
      ],
      { baseId: 1 },
    );
    expect(result.untouched.map(r => [r.tripId, r.reason])).toEqual([
      [1, 'Ya ha salido.'],
      [2, 'Está bloqueado.'],
      [3, 'Ya tiene conductor (Cond 5) y le falta el vehículo.'],
      [4, 'Ya tiene vehículo (Mat 5) y le falta el conductor.'],
    ]);
    expect(result.left).toEqual([]);
    expect(result.headline).toBe('0 viajes asignados.'); // something is still pending
  });

  it('all the bases unless one is chosen', () => {
    expect(see(proposal([]), [], [trip(1), trip(2, { BaseId: 2 })]).left).toHaveLength(2);
  });
});

describe('no doubts about what was saved', () => {
  it('without an answer but saved with that pair counts as assigned and is not retried', () => {
    const proposed = proposal([line(1), line(2)]);
    const current = withResult(toLines(proposed), 1, unknown);
    expect(see(proposed, current, [trip(1), trip(2)], { end: 'api' }).left[0]).toMatchObject({ tripId: 1, key: 'unknown' });
    const result = see(proposed, current, [ours(1), trip(2)], { end: 'api' });
    expect([result.assigned, keys(result)]).toEqual([1, [[2, 'untried']]]);
    expect(retryQueue(current, [ours(1), trip(2)]).map(x => x.view.TripId)).toEqual([2]);
    expect(retryQueue(current, null).map(x => x.view.TripId)).toEqual([1, 2]);
  });

  it('assigned by another meanwhile: it says who, does not count as ours nor is retried, even with the same pair', () => {
    const proposed = proposal([line(1), line(2), line(3), line(4)]);
    let current = withResult(withResult(toLines(proposed), 1, rejected), 2, unknown);
    current = withResult(current, 4, busy);
    const trips = [ours(1), trip(2, { AssignedDriverId: 7, AssignedVehicleId: 8 }), ours(3), trip(4, { AssignedDriverId: 7 })];
    const result = see(proposed, current, trips, { end: 'stopped' });
    expect(result.assigned).toBe(0);
    expect(result.left.map(r => [r.tripId, r.key, r.pending])).toEqual([
      [1, 'elsewhere', false],
      [2, 'elsewhere', false],
      [3, 'elsewhere', false],
      [4, 'partial', true],
    ]);
    expect(result.left.map(r => r.reason)).toEqual([
      'Asignado a mano o desde otra pantalla: Cond 10 · Mat 100.',
      'Asignado a mano o desde otra pantalla: Cond 7 · Mat 8.',
      'Asignado a mano o desde otra pantalla: Cond 30 · Mat 300.',
      'Ya tiene conductor (Cond 7) y le falta el vehículo.',
    ]);
    expect(retryQueue(current, trips)).toEqual([]);
    expect(result.headline).toBe(
      '0 viajes asignados. Sin asignar: 1 con solo conductor o solo vehículo. 3 asignados a mano o desde otra pantalla.',
    );
  });

  it('cancelled or locked meanwhile: it is not pending nor retried', () => {
    const proposed = proposal([line(1), line(2, false), line(3)]);
    const current = withResult(toLines(proposed), 1, busy);
    const trips = [trip(1, { Status: 'Cancelled' }), trip(2, { IsActive: false }), trip(3, { IsLocked: true })];
    const result = see(proposed, current, trips, { end: 'stopped' });
    expect(result.left.map(r => [r.key, r.reason, r.pending])).toEqual([
      ['cancelled', 'Se ha cancelado.', false],
      ['cancelled', 'Se ha cancelado.', false],
      ['locked', 'Está bloqueado.', true],
    ]);
    expect(retryQueue(current, trips)).toEqual([]);
    expect(result.headline).toBe('0 viajes asignados. Sin asignar: 1 bloqueado. 2 cancelados.');
  });

  it('a trip that is no longer in the period keeps what is known', () => {
    const proposed = proposal([line(1)]);
    expect(see(proposed, withResult(toLines(proposed), 1, rejected), []).left[0]).toMatchObject({
      key: 'rejected',
      reason: 'CE 561 · Jornada',
    });
  });
});

describe('what can be repeated', () => {
  it('not tried and busy, never rejected nor without solution', () => {
    const proposed = proposal([line(1), line(2), line(3), line(4, false), line(5)]);
    let current = withResult(withResult(toLines(proposed), 1, applied), 2, rejected);
    current = withResult(current, 3, busy);
    const queue = retryQueue(current, null);
    expect(queue.map(x => x.view.TripId)).toEqual([3, 5]);
    expect([retryLabel(queue), retryLabel(queue.slice(0, 1)), retryLabel([])]).toEqual([
      'Continuar con 2 viajes',
      'Reintentar 1 viaje',
      null,
    ]);
  });
});
